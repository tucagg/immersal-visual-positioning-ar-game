using System;
using Firebase;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class AuthManager : MonoBehaviour
{
    public static AuthManager Instance { get; private set; }

    [Header("Root Panels")]
    public GameObject authPanel;          // Tüm auth UI’nin root’u
    public GameObject modeSelectPanel;    // Login/Register/Google seçim ekranı
    public GameObject loginPanel;         // Sadece email+şifre login
    public GameObject registerPanel;      // Email+şifre+username register
    public TMP_Text statusLabel;          // Alt taraftaki uyarı/metin

    [Header("Login UI")]
    public TMP_InputField loginEmailInput;
    public TMP_InputField loginPasswordInput;

    [Header("Register UI")]
    public TMP_InputField registerEmailInput;
    public TMP_InputField registerPasswordInput;
    public TMP_InputField registerUsernameInput;

    [Header("Stay Signed In")]
    public Toggle staySignedInToggle;

    [Header("Game Roots")]
    public GameObject gameRoot;           // XR, Immersal vs.
    public GameObject adminUiRoot;        // Admin panel Canvas (isteğe bağlı)

    [Header("Logout UI")]
    public GameObject logoutButtonRoot;   // Logout butonunun (veya parent'ının) root'u
    [Header("Logout Behavior")]
    [Tooltip("If true, the current scene will be reloaded on logout to fully reinitialize runtime state.")]
    public bool reloadSceneOnLogout = true;


    public string CurrentRole { get; private set; } = "guest";
    public FirebaseUser CurrentUser { get; private set; }

    private FirebaseAuth _auth;
    private DatabaseReference _db;

    private const string StaySignedInKey = "stay_signed_in";

    private bool StaySignedInPreference
    {
        get => PlayerPrefs.GetInt(StaySignedInKey, 1) == 1; // default: stay signed in
        set
        {
            PlayerPrefs.SetInt(StaySignedInKey, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }

    #region Singleton
    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }
    #endregion

    void Start()
    {
        // FirebaseInitializer hazır olmadan auth'a dokunma
        InvokeRepeating(nameof(TryInit), 0.5f, 0.5f);
    }

    void TryInit()
    {
        if (!FirebaseInitializer.Ready) return;

        CancelInvoke(nameof(TryInit));

        _auth = FirebaseAuth.DefaultInstance;
        _db = FirebaseInitializer.DB;

        // Varsayılan UI state: auth görünürken logout gizli olsun
        ShowAuthUi(true);

        // UI toggle'ı kaydedilmiş tercihe eşitle
        if (staySignedInToggle != null)
        {
            staySignedInToggle.isOn = StaySignedInPreference;
        }

        // Stay signed in ayarına göre mevcut kullanıcıyı kullan veya çıkış yap
        if (!StaySignedInPreference && _auth.CurrentUser != null)
        {
            _auth.SignOut();
        }

        // Daha önce login olmuş ve stay signed in açık bir kullanıcı var mı?
        if (StaySignedInPreference && _auth.CurrentUser != null)
        {
            CurrentUser = _auth.CurrentUser;
            ShowAuthUi(false);
            LoadUserRoleAndEnterGame();
        }
        else
        {
            // Login ekranını aç
            ApplyRole("guest");
            ShowAuthUi(true);
            ShowModeSelect();
        }
    }

    // ========== UI SEÇİM EKRANI CALLBACK'LERİ ==========

    // İlk ekrandaki "Login with Email" butonu
    public void OnClickChooseLoginEmail()
    {
        ClearStatus();
        ClearLoginInputs();
        ShowOnlyPanel(loginPanel);
    }

    // İlk ekrandaki "Register with Email" butonu
    public void OnClickChooseRegisterEmail()
    {
        ClearStatus();
        ClearRegisterInputs();
        ShowOnlyPanel(registerPanel);
    }

    // Login/Register ekranlarındaki geri tuşu
    public void OnClickBackToModeSelect()
    {
        ClearStatus();
        ClearLoginInputs();
        ClearRegisterInputs();
        ShowModeSelect();
    }

    // ========== LOGIN / REGISTER BUTONLARI ==========

    public void OnClickLogin()
    {
        var email = loginEmailInput.text.Trim();
        var pass = loginPasswordInput.text;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(pass))
        {
            SetStatus("Please enter your email and password.");
            return;
        }

        if (pass.Length < 6)
        {
            SetStatus("Password must be at least 6 characters.");
            return;
        }

        SetStatus("Signing in...");
        _auth.SignInWithEmailAndPasswordAsync(email, pass)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    var msg = GetReadableFirebaseError(task.Exception?.ToString() ?? "Login failed.");
                    SetStatus(msg);
                    Debug.LogError(task.Exception);
                    return;
                }

                CurrentUser = task.Result.User;

                // Stay signed in tercihini kaydet
                if (staySignedInToggle != null)
                {
                    StaySignedInPreference = staySignedInToggle.isOn;
                }
                else
                {
                    StaySignedInPreference = true; // toggle yoksa varsayılan true
                }

                LoadUserRoleAndEnterGame();
            });
    }

    public void OnClickRegister()
    {
        var email = registerEmailInput.text.Trim();
        var pass = registerPasswordInput.text;
        var username = registerUsernameInput.text.Trim();

        if (string.IsNullOrEmpty(email) ||
            string.IsNullOrEmpty(pass) ||
            string.IsNullOrEmpty(username))
        {
            SetStatus("Please fill all fields.");
            return;
        }

        if (!email.Contains("@") || !email.Contains("."))
        {
            SetStatus("Please enter a valid email address.");
            return;
        }

        if (pass.Length < 6)
        {
            SetStatus("Password must be at least 6 characters.");
            return;
        }

        SetStatus("Creating account...");
        _auth.CreateUserWithEmailAndPasswordAsync(email, pass)
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCanceled || task.IsFaulted)
                {
                    var msg = GetReadableFirebaseError(task.Exception?.ToString() ?? "Register failed.");
                    SetStatus(msg);
                    Debug.LogError(task.Exception);
                    return;
                }

                CurrentUser = task.Result.User;

                // Stay signed in tercihini kaydet
                if (staySignedInToggle != null)
                {
                    StaySignedInPreference = staySignedInToggle.isOn;
                }
                else
                {
                    StaySignedInPreference = true;
                }

                SetStatus("Account created. Saving profile...");

                // DB'de rol + profil kaydı → default: user
                var userData = new UserProfile
                {
                    email = email,
                    userName = username,
                    role = "user"
                };

                _db.Child("users").Child(CurrentUser.UserId)
                    .SetRawJsonValueAsync(JsonUtility.ToJson(userData))
                    .ContinueWithOnMainThread(_ =>
                    {
                        LoadUserRoleAndEnterGame();
                    });
            });
    }

    // İleride logout tuşuna bağlanabilir
    public void OnClickLogout()
    {
        // Auth henüz init olmadıysa güvenli çık
        if (_auth == null)
        {
            Debug.LogWarning("[Auth] Logout called before FirebaseAuth init.");
        }
        else
        {
            _auth.SignOut();
        }

        CurrentUser = null;
        ApplyRole("guest");

        // Kullanıcı logout yaptıysa bir sonraki açılışta otomatik login olmasın
        StaySignedInPreference = false;

        // UI toggle varsa kapat
        if (staySignedInToggle != null)
        {
            staySignedInToggle.isOn = false;
        }
        if (reloadSceneOnLogout)
        {
            // Fully reset runtime state by reloading the active scene.
            // Important: reset singleton so the new scene instance can initialize normally.
            Instance = null;

            // Ensure we don't carry any references/state into the next session.
            CurrentRole = "guest";
            CurrentUser = null;
            anchorsRealtime = null;

            // Reload the current scene. The new AuthManager in the scene will initialize like a fresh launch.
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

            // Destroy this persistent instance (it was marked DontDestroyOnLoad).
            Destroy(gameObject);
            return;
        }

        // Fallback: old behavior (no scene reload)
        if (gameRoot) gameRoot.SetActive(false);
        ShowAuthUi(true);
        ShowModeSelect();
        SetStatus("Logged out.");
    }

    // Called when the user selects a new map. We want a full scene reset like a fresh launch,
    // but WITHOUT signing out (so the user doesn't have to authenticate again).
    public void ReloadSceneForMapChangeKeepAuth(int newMapId, string newMapName)
    {
        // Persist selection for one-shot apply after reload (MapRootProvider reads these on Start).
        PlayerPrefs.SetInt(MapRootProvider.PREFS_FORCE_MAP_ONCE, 1);
        PlayerPrefs.SetInt(MapRootProvider.PREFS_SELECTED_MAP_ID, newMapId);
        if (!string.IsNullOrEmpty(newMapName))
            PlayerPrefs.SetString(MapRootProvider.PREFS_SELECTED_MAP_NAME, newMapName);
        PlayerPrefs.Save();

        // IMPORTANT: Do NOT sign out. We want to keep FirebaseAuth.DefaultInstance.CurrentUser.
        // Also do NOT change StaySignedInPreference here.

        // Reset singleton so the new scene instance can initialize normally.
        Instance = null;

        // Clear any scene-bound references so we don't accidentally use stale ones.
        anchorsRealtime = null;

        // Reload the current scene. A new AuthManager in the scene will auto-enter the game
        // if FirebaseAuth still has a CurrentUser and StaySignedInPreference is true.
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

        // Destroy this persistent instance (it was marked DontDestroyOnLoad).
        Destroy(gameObject);
    }

    // ========== İÇ MANTIK ==========

    [Serializable]
    private class UserProfile
    {
        public string email;
        public string userName;
        public string role;
    }

    void LoadUserRoleAndEnterGame()
    {
        if (CurrentUser == null)
        {
            SetStatus("No user.");
            ShowModeSelect();
            return;
        }

        string uid = CurrentUser.UserId;
        SetStatus("Loading role...");

        _db.Child("users").Child(uid).Child("role").GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsCompletedSuccessfully || task.Result == null || !task.Result.Exists)
                {
                    // Kayıt yoksa default user gibi davran
                    Debug.LogWarning("[Auth] Role not found. Using 'user'.");
                    ApplyRole("user");
                }
                else
                {
                    string role = task.Result.Value.ToString();
                    ApplyRole(role);
                }

                // Oyuna geç
                ShowAuthUi(false);
                if (gameRoot) gameRoot.SetActive(true);
                ClearStatus();
            });
    }

    [Header("Gameplay Refs")]
    public AnchorsRealtime anchorsRealtime;

    void ApplyRole(string role)
    {
        CurrentRole = role;
        Debug.Log("[Auth] Role = " + role);

        bool isAdmin = (role == "admin");

        if (anchorsRealtime != null)
        {
            anchorsRealtime.adminMode = isAdmin;
            Debug.Log("[Auth] anchors.adminMode = " + anchorsRealtime.adminMode);
        }

        if (adminUiRoot != null)
        {
            adminUiRoot.SetActive(isAdmin);
        }
    }

    // ========== UI Yardımcıları ==========

    void ShowAuthUi(bool show)
    {
        if (authPanel) authPanel.SetActive(show);

        // Auth ekranı açıksa logout görünmemeli; oyuna geçince görünmeli
        if (logoutButtonRoot != null)
        {
            logoutButtonRoot.SetActive(!show);
        }
    }

    void ShowModeSelect()
    {
        ShowOnlyPanel(modeSelectPanel);
    }

    void ShowOnlyPanel(GameObject panelToShow)
    {
        if (!authPanel) return;

        if (modeSelectPanel) modeSelectPanel.SetActive(false);
        if (loginPanel) loginPanel.SetActive(false);
        if (registerPanel) registerPanel.SetActive(false);

        if (panelToShow) panelToShow.SetActive(true);
    }

    void ClearLoginInputs()
    {
        if (loginEmailInput) loginEmailInput.text = "";
        if (loginPasswordInput) loginPasswordInput.text = "";
    }

    void ClearRegisterInputs()
    {
        if (registerEmailInput) registerEmailInput.text = "";
        if (registerPasswordInput) registerPasswordInput.text = "";
        if (registerUsernameInput) registerUsernameInput.text = "";
    }

    void ClearStatus()
    {
        SetStatus("");
    }

    void SetStatus(string msg)
    {
        Debug.Log("[Auth] " + msg);
        if (statusLabel != null)
            statusLabel.text = msg;
    }

    // Firebase hata mesajlarını insanca çevir
    string GetReadableFirebaseError(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return "An error occurred.";

        raw = raw.ToUpperInvariant();

        if (raw.Contains("EMAIL_EXISTS"))
            return "An account already exists for this email.";

        if (raw.Contains("INVALID_EMAIL"))
            return "Please enter a valid email address.";

        if (raw.Contains("WEAK_PASSWORD"))
            return "Password is too weak (min 6 characters).";

        if (raw.Contains("USER_NOT_FOUND"))
            return "No account found with this email.";

        if (raw.Contains("WRONG_PASSWORD"))
            return "Incorrect password.";

        if (raw.Contains("NETWORK_REQUEST_FAILED"))
            return "No internet connection.";

        return "An error occurred. Please try again.";
    }
}