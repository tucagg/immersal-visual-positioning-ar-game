using TMPro;
using System.Collections.Generic;
using Firebase.Extensions;
using UnityEngine;
using UnityEngine.UI;

public class AppUIManager : MonoBehaviour
{
    private enum ScreenId
    {
        AR_Main,
        AR_Popup,
        AR_Puzzle,
        AR_VoteMap,

        Map_Maps,
        Map_Details,

        Profile_Main,
        Profile_Edit,
        Profile_Settings,
        Profile_ChangePassword,
        Profile_CompletedMaps,

        Social_Main,
        Social_DetailedUser,
        Social_CompletedMaps,

        Create_Main,
        Create_AddNewMap,
        Create_EditMap,
        Create_EditClues,
        Create_EditClue,
        Create_EditClueLocAR,

        Admin_MapsStatus,
        Admin_MapDetailsApproval,
        Admin_ClueDetailsApproval,
        Admin_MapRejectFeedback
    }

    [Header("Main Panels")]
    public GameObject panelAR;
    public GameObject panelMap;
    public GameObject panelProfile;
    public GameObject panelSocial;
    public GameObject panelCreate;
    public GameObject panelAdmin;

    [Header("Bottom Bar")]
    public GameObject bottomBar;
    public Button btnCreate;
    public Button btnMap;
    public Button btnHome;
    public Button btnSocial;
    public Button btnProfile;
    public Button btnAdmin;

    [Header("AR Runtime")]
    [Tooltip("Root object that contains AR Camera / AR Session / Immersal runtime. Active while Panel_AR is open and on Screen_EditClueLocAR.")]
    public GameObject arRuntimeRoot;

    [Tooltip("SpatialPanelManager — needed to enter/exit AR mode programmatically (e.g. from Btn_GoToAR).")]
    public SpatialPanelManager spatialPanelManager;

    [Tooltip("ARSession — paused when not on AR screens to save battery/GPU.")]
    public UnityEngine.XR.ARFoundation.ARSession arSession;

    [Tooltip("MapRootProvider used to reset localization when AR runtime is stopped/restarted.")]
    public MapRootProvider mapRootProvider;

    [Header("Access")]
    [SerializeField] private bool currentUserIsAdmin = false;

    [Header("AR Screens")]
    public GameObject screenMainAR;
    public GameObject screenPopup;
    public GameObject screenPuzzle;
    public GameObject screenVoteMap;

    [Header("AR Popup UI")]
    public TMP_Text popupTitleText;
    public TMP_Text popupMessageText;

    [Header("AR Puzzle UI")]
    public TMP_Text puzzleTitleText;
    public TMP_Text puzzleHintText;
    public TMP_InputField puzzleAnswerInput;
    public TMP_Text puzzleFeedbackText;
    public Button puzzleSubmitButton;
    public AnchorsRealtime anchorsRealtime;

    private string _activePuzzleAnchorId;

    [Header("Map Screens")]
    public GameObject screenMaps;
    public GameObject screenMapDetails;

    [Header("Profile Screens")]
    public GameObject screenProfile;
    public GameObject screenEditProfile;
    public GameObject screenSettings;
    public GameObject screenChangePassword;
    public GameObject screenCompletedMaps;

    [Header("Social Screens")]
    public GameObject screenSocial;
    public GameObject screenDetailedUser;
    public GameObject screenSocialCompletedMaps;

    [Header("Create Screens")]
    public GameObject screenCreateMain;
    public GameObject screenEditClues;
    public GameObject screenAddNewMap;
    public GameObject screenEditMap;
    public GameObject screenEditClue;
    public GameObject screenEditClueLocAR;

    [Header("Admin Screens")]
    public GameObject screenMapsStatus;
    public GameObject screenMapDetailsApproval;
    public GameObject screenClueDetailsApproval;
    public GameObject screenMapRejectFeedback;

    // ── Per-panel navigasyon sistemi ──────────────────────────────────────────

    private enum PanelId { AR, Map, Profile, Social, Create, Admin }

    private static PanelId GetPanel(ScreenId s) => s switch
    {
        ScreenId.AR_Main or ScreenId.AR_Popup or ScreenId.AR_Puzzle or ScreenId.AR_VoteMap
            => PanelId.AR,
        ScreenId.Map_Maps or ScreenId.Map_Details
            => PanelId.Map,
        ScreenId.Profile_Main or ScreenId.Profile_Edit or
        ScreenId.Profile_Settings or ScreenId.Profile_ChangePassword or
        ScreenId.Profile_CompletedMaps
            => PanelId.Profile,
        ScreenId.Social_Main or ScreenId.Social_DetailedUser or ScreenId.Social_CompletedMaps
            => PanelId.Social,
        ScreenId.Create_Main or ScreenId.Create_AddNewMap or ScreenId.Create_EditMap or
        ScreenId.Create_EditClues or ScreenId.Create_EditClue or ScreenId.Create_EditClueLocAR
            => PanelId.Create,
        _ => PanelId.Admin
    };

    private static readonly Dictionary<PanelId, ScreenId> PanelDefault = new()
    {
        { PanelId.AR,      ScreenId.AR_Main },
        { PanelId.Map,     ScreenId.Map_Maps },
        { PanelId.Profile, ScreenId.Profile_Main },
        { PanelId.Social,  ScreenId.Social_Main },
        { PanelId.Create,  ScreenId.Create_Main },
        { PanelId.Admin,   ScreenId.Admin_MapsStatus },
    };

    private readonly Dictionary<PanelId, Stack<ScreenId>> _panelStacks = new()
    {
        { PanelId.AR,      new() },
        { PanelId.Map,     new() },
        { PanelId.Profile, new() },
        { PanelId.Social,  new() },
        { PanelId.Create,  new() },
        { PanelId.Admin,   new() },
    };

    private readonly Dictionary<PanelId, ScreenId> _panelCurrent = new()
    {
        { PanelId.AR,      ScreenId.AR_Main },
        { PanelId.Map,     ScreenId.Map_Maps },
        { PanelId.Profile, ScreenId.Profile_Main },
        { PanelId.Social,  ScreenId.Social_Main },
        { PanelId.Create,  ScreenId.Create_Main },
        { PanelId.Admin,   ScreenId.Admin_MapsStatus },
    };

    private void Awake()
    {
        // Spatial UI: AR kamera her zaman açık — Awake'te başlat.
        if (arRuntimeRoot != null)
            arRuntimeRoot.SetActive(true);
    }
    private void Start()
    {
        WireBottomBar();
        WireARClueUI();
        SetAdminButtonVisible(currentUserIsAdmin);

        // Spatial UI: BottomBar gizli, tüm paneller aynı anda açık başlar.
        if (bottomBar != null) bottomBar.SetActive(false);
        InitAllSpatialScreens();
    }

    /// <summary>
    /// Spatial UI başlangıcı: her panelin varsayılan ekranını açar.
    /// Kamera Awake'te zaten başlatıldı, burada dokunulmaz.
    /// </summary>
    private void InitAllSpatialScreens()
    {
        // Her panelin varsayılan ekranını hazırla
        HideAllAR();      Set(screenMainAR,     true);
        HideAllMap();     Set(screenMaps,        true);
        HideAllProfile(); Set(screenProfile,     true);
        HideAllSocial();  Set(screenSocial,      true);
        HideAllCreate();  Set(screenCreateMain,  true);
        HideAllAdmin();   Set(screenMapsStatus,  true);

        // Panel görünürlükleri — admin durumu korunur
        Set(panelAR,      true);
        Set(panelMap,     true);
        Set(panelProfile, true);
        Set(panelSocial,  true);
        Set(panelCreate,  true);
        Set(panelAdmin,   currentUserIsAdmin);
    }

    /// <summary>SpatialPanelManager swipe-up → AR moduna girildi. Kamera zaten açık.</summary>
    public void OnARModeEntered()
    {
        _panelCurrent[PanelId.AR] = ScreenId.AR_Main;
        HideAllAR();
        Set(screenMainAR, true);
    }

    /// <summary>SpatialPanelManager swipe-down → AR modundan çıkıldı. Kamera açık kalmaya devam eder.</summary>
    public void OnARModeExited()
    {
        // Ensure all panels are active — SpatialPanelManager may not manage all of them.
        Set(panelAR,      true);
        Set(panelMap,     true);
        Set(panelProfile, true);
        Set(panelSocial,  true);
        Set(panelCreate,  true);
        Set(panelAdmin,   currentUserIsAdmin);
        if (bottomBar != null) bottomBar.SetActive(true);
    }

    private void OnDestroy()
    {
        if (btnCreate != null) btnCreate.onClick.RemoveListener(ShowCreate);
        if (btnMap != null) btnMap.onClick.RemoveListener(ShowMap);
        if (btnHome != null) btnHome.onClick.RemoveListener(ShowAR);
        if (btnSocial != null) btnSocial.onClick.RemoveListener(ShowSocial);
        if (btnProfile != null) btnProfile.onClick.RemoveListener(ShowProfile);
        if (btnAdmin != null) btnAdmin.onClick.RemoveListener(ShowAdmin);
        if (puzzleSubmitButton != null) puzzleSubmitButton.onClick.RemoveListener(OnPuzzleSubmitClicked);
    }

    private void WireBottomBar()
    {
        if (btnCreate != null) btnCreate.onClick.AddListener(ShowCreate);
        if (btnMap != null) btnMap.onClick.AddListener(ShowMap);
        if (btnHome != null) btnHome.onClick.AddListener(ShowAR);
        if (btnSocial != null) btnSocial.onClick.AddListener(ShowSocial);
        if (btnProfile != null) btnProfile.onClick.AddListener(ShowProfile);
        if (btnAdmin != null) btnAdmin.onClick.AddListener(ShowAdmin);
    }

    private void WireARClueUI()
    {
        if (puzzleSubmitButton != null)
        {
            puzzleSubmitButton.onClick.RemoveListener(OnPuzzleSubmitClicked);
            puzzleSubmitButton.onClick.AddListener(OnPuzzleSubmitClicked);
        }
    }

    // En son NavigateTo çağrısının paneli — GoBack() için kullanılır.
    private PanelId _lastPanel = PanelId.Map;

    private void NavigateTo(ScreenId target, bool addToBackStack = true)
    {
        PanelId panel = GetPanel(target);
        _lastPanel = panel;
        ScreenId current = _panelCurrent[panel];

        if (target == current) { ApplyScreen(target); return; }

        if (addToBackStack) _panelStacks[panel].Push(current);
        _panelCurrent[panel] = target;
        ApplyScreen(target);
    }

    // ── Panel geri tuşları ────────────────────────────────────────────────────

    /// <summary>
    /// Geriye uyumluluk: scriptteki GoBack() çağrıları en son aktif panelde geri gider.
    /// Tercihen panel-spesifik metodları (GoBackMap, GoBackProfile…) kullanın.
    /// </summary>
    public void GoBack() => GoBackPanel(_lastPanel);

    public void GoBackAR()      => GoBackPanel(PanelId.AR);
    public void GoBackMap()     => GoBackPanel(PanelId.Map);
    public void GoBackProfile() => GoBackPanel(PanelId.Profile);
    public void GoBackSocial()  => GoBackPanel(PanelId.Social);
    public void GoBackCreate()  => GoBackPanel(PanelId.Create);
    public void GoBackAdmin()   => GoBackPanel(PanelId.Admin);

    private void GoBackPanel(PanelId panel)
    {
        var stack = _panelStacks[panel];
        if (stack.Count == 0) return;
        ScreenId prev = stack.Pop();
        _panelCurrent[panel] = prev;
        ApplyScreen(prev);
    }

    /// <summary>Paneli ilk ekranına sıfırlar (stack temizlenir).</summary>
    private void ResetPanel(PanelId panel)
    {
        _panelStacks[panel].Clear();
        _panelCurrent[panel] = PanelDefault[panel];
        ApplyScreen(PanelDefault[panel]);
    }

    private void SetEditClueLocARMapLock(bool locked)
    {
        if (mapRootProvider == null)
            mapRootProvider = FindFirstObjectByType<MapRootProvider>();

        if (mapRootProvider != null)
            mapRootProvider.SetEditClueLocARMapLock(locked);
    }

    private void ApplyScreen(ScreenId screen)
    {
        SetEditClueLocARMapLock(screen == ScreenId.Create_EditClueLocAR);

        // AR mode: enter when going to AR screens, exit when leaving them.
        bool goingToAR = screen == ScreenId.Create_EditClueLocAR
                      || screen == ScreenId.AR_Main
                      || screen == ScreenId.AR_Popup
                      || screen == ScreenId.AR_Puzzle
                      || screen == ScreenId.AR_VoteMap;
        if (goingToAR && spatialPanelManager != null && !spatialPanelManager.IsInARMode)
            spatialPanelManager.EnterARMode();
        else if (!goingToAR && spatialPanelManager != null && spatialPanelManager.IsInARMode)
            spatialPanelManager.ExitARMode();

        switch (screen)
        {
            case ScreenId.AR_Main:
                ShowOnlyPanel(panelAR);
                HideAllAR();
                Set(screenMainAR, true);
                break;

            case ScreenId.AR_Popup:
                ShowOnlyPanel(panelAR);
                HideAllAR();
                Set(screenPopup, true);
                break;

            case ScreenId.AR_Puzzle:
                ShowOnlyPanel(panelAR);
                HideAllAR();
                Set(screenPuzzle, true);
                break;

            case ScreenId.AR_VoteMap:
                ShowOnlyPanel(panelAR);
                HideAllAR();
                Set(screenVoteMap, true);
                break;

            case ScreenId.Map_Maps:
                ShowOnlyPanel(panelMap);
                HideAllMap();
                Set(screenMaps, true);
                break;

            case ScreenId.Map_Details:
                ShowOnlyPanel(panelMap);
                HideAllMap();
                Set(screenMapDetails, true);
                break;

            case ScreenId.Profile_Main:
                ShowOnlyPanel(panelProfile);
                HideAllProfile();
                Set(screenProfile, true);
                break;

            case ScreenId.Profile_Edit:
                ShowOnlyPanel(panelProfile);
                HideAllProfile();
                Set(screenEditProfile, true);
                break;

            case ScreenId.Profile_Settings:
                ShowOnlyPanel(panelProfile);
                HideAllProfile();
                Set(screenSettings, true);
                break;

            case ScreenId.Profile_ChangePassword:
                ShowOnlyPanel(panelProfile);
                HideAllProfile();
                Set(screenChangePassword, true);
                break;

            case ScreenId.Profile_CompletedMaps:
                ShowOnlyPanel(panelProfile);
                HideAllProfile();
                Set(screenCompletedMaps, true);
                break;

            case ScreenId.Social_Main:
                ShowOnlyPanel(panelSocial);
                HideAllSocial();
                Set(screenSocial, true);
                break;

            case ScreenId.Social_DetailedUser:
                ShowOnlyPanel(panelSocial);
                HideAllSocial();
                Set(screenDetailedUser, true);
                break;

            case ScreenId.Social_CompletedMaps:
                ShowOnlyPanel(panelSocial);
                HideAllSocial();
                Set(screenSocialCompletedMaps, true);
                break;

            case ScreenId.Create_Main:
                ShowOnlyPanel(panelCreate);
                HideAllCreate();
                Set(screenCreateMain, true);
                break;

            case ScreenId.Create_AddNewMap:
                ShowOnlyPanel(panelCreate);
                HideAllCreate();
                Set(screenAddNewMap, true);
                break;

            case ScreenId.Create_EditMap:
                ShowOnlyPanel(panelCreate);
                HideAllCreate();
                Set(screenEditMap, true);
                break;

            case ScreenId.Create_EditClues:
                ShowOnlyPanel(panelCreate);
                HideAllCreate();
                Set(screenEditClues, true);
                break;

            case ScreenId.Create_EditClue:
                ShowOnlyPanel(panelCreate);
                HideAllCreate();
                Set(screenEditClue, true);
                break;

            case ScreenId.Create_EditClueLocAR:
                HideAllAR();
                HideAllCreate();
                Set(screenEditClueLocAR, true);
                break;

            case ScreenId.Admin_MapsStatus:
                ShowOnlyPanel(panelAdmin);
                HideAllAdmin();
                Set(screenMapsStatus, true);
                break;

            case ScreenId.Admin_MapDetailsApproval:
                ShowOnlyPanel(panelAdmin);
                HideAllAdmin();
                Set(screenMapDetailsApproval, true);
                break;

            case ScreenId.Admin_ClueDetailsApproval:
                ShowOnlyPanel(panelAdmin);
                HideAllAdmin();
                Set(screenClueDetailsApproval, true);
                break;

            case ScreenId.Admin_MapRejectFeedback:
                ShowOnlyPanel(panelAdmin);
                HideAllAdmin();
                Set(screenMapRejectFeedback, true);
                break;
        }
    }

    // Tracks whether the AR runtime has ever been activated this session.
    // Once Immersal has finished its async ConfigureComponents/ConfigurePlatform chain,
    // we never deactivate the runtime again — toggling mid-init causes Immersal to abort
    // permanently with "Could not find ARCameraManager".
    private bool _arRuntimeEverActivated = false;

    private bool ShouldARRuntimeBeActive(ScreenId screen)
    {
        switch (screen)
        {
            case ScreenId.AR_Main:
            case ScreenId.AR_Popup:
            case ScreenId.AR_Puzzle:
            case ScreenId.AR_VoteMap:
            case ScreenId.Create_EditClueLocAR:
                return true;
        }

        // Track if runtime was externally activated (e.g. by another script or scene init).
        if (arRuntimeRoot != null && arRuntimeRoot.activeSelf)
            _arRuntimeEverActivated = true;

        // Non-AR screens: camera should be off. SetARRuntimeActive will decide
        // whether to truly deactivate or just pause ARSession based on _arRuntimeEverActivated.
        return false;
    }

    private void SetARRuntimeActive(bool active)
    {
        // Spatial UI: AR kamera her zaman açık — deactivate isteği yok sayılır.
        if (!active) return;

        if (arRuntimeRoot == null) return;

        if (!arRuntimeRoot.activeSelf)
        {
            arRuntimeRoot.SetActive(true);
            _arRuntimeEverActivated = true;
            Debug.Log("[AppUI] AR runtime başlatıldı.");
        }
        if (arSession != null && !arSession.enabled)
        {
            arSession.enabled = true;
            Debug.Log("[AppUI] ARSession aktif.");
        }
    }

    public void ForceStopARRuntime()
    {
        if (arRuntimeRoot == null)
            return;

        ResetLocalizationForRuntimeChange();
        arRuntimeRoot.SetActive(false);
    }

    // Resets the localization state so TrySubscribeOnce will wait for re-localization.
    // arRuntimeRoot is NOT toggled — toggling it mid-session causes Immersal to abort
    // permanently with "Could not find ARCameraManager". Immersal re-localizes to the
    // new map automatically; MapRootProvider.OnSuccessfulLocalizations sets localized=true.
    public void RestartARRuntime()
    {
        ResetLocalizationForRuntimeChange();
        Debug.Log("[AppUI] Localization state reset for map change. Waiting for Immersal re-localization.");
    }

    private void ResetLocalizationForRuntimeChange()
    {
        if (mapRootProvider == null)
            mapRootProvider = FindFirstObjectByType<MapRootProvider>();

        if (mapRootProvider != null)
            mapRootProvider.ResetLocalization();
    }

    private void ShowOnlyPanel(GameObject activePanel)
    {
        // Spatial UI: tüm paneller daima aktif — Canvas görünürlüğü SpatialPanelManager tarafından yönetilir.
        // Eski mantık (tek panel görünür) yerine, tüm panelleri açık bırakıyoruz;
        // hangi SCREEN gösterileceği HideAll* + Set(screen, true) ile yönetilir.
        Set(panelAR,      true);
        Set(panelMap,     true);
        Set(panelProfile, true);
        Set(panelSocial,  true);
        Set(panelCreate,  true);
        Set(panelAdmin,   currentUserIsAdmin);

        if (bottomBar != null)
            bottomBar.SetActive(true);
    }

    private void Set(GameObject go, bool active)
    {
        if (go != null)
            go.SetActive(active);
    }

    private void HideAllAR()
    {
        Set(screenMainAR, false);
        Set(screenPopup, false);
        Set(screenPuzzle, false);
        Set(screenVoteMap, false);
        Set(screenEditClueLocAR, false);
    }

    private void HideAllMap()
    {
        Set(screenMaps, false);
        Set(screenMapDetails, false);
    }

    private void HideAllProfile()
    {
        Set(screenProfile, false);
        Set(screenEditProfile, false);
        Set(screenSettings, false);
        Set(screenChangePassword, false);
        Set(screenCompletedMaps, false);
    }

    private void HideAllSocial()
    {
        Set(screenSocial, false);
        Set(screenDetailedUser, false);
        Set(screenSocialCompletedMaps, false);
    }

    private void HideAllCreate()
    {
        Set(screenCreateMain, false);
        Set(screenEditClues, false);
        Set(screenAddNewMap, false);
        Set(screenEditMap, false);
        Set(screenEditClue, false);
    }

    private void HideAllAdmin()
    {
        Set(screenMapsStatus, false);
        Set(screenMapDetailsApproval, false);
        Set(screenClueDetailsApproval, false);
        Set(screenMapRejectFeedback, false);
    }

    // Panel sıfırlama — her panelin ilk ekranına döner, geçmiş temizlenir.
    public void ShowAR()      => ResetPanel(PanelId.AR);
    public void ShowMap()     => ResetPanel(PanelId.Map);
    public void ShowProfile() => ResetPanel(PanelId.Profile);
    public void ShowSocial()  => ResetPanel(PanelId.Social);
    public void ShowCreate()  => ResetPanel(PanelId.Create);

    public void ShowAdmin()
    {
        if (!currentUserIsAdmin)
        {
            Debug.LogWarning("[AppUI] Admin navigation blocked: current user is not admin.");
            SetAdminButtonVisible(false);
            return;
        }
        ResetPanel(PanelId.Admin);
    }

    // AR flow
    public void ShowARPopup() => NavigateTo(ScreenId.AR_Popup);
    public void ShowARPuzzle() => NavigateTo(ScreenId.AR_Puzzle);
    public void ShowVoteMap() => NavigateTo(ScreenId.AR_VoteMap);

    public void ShowCluePopup(string title, string message)
    {
        if (popupTitleText != null) popupTitleText.text = title ?? "";
        if (popupMessageText != null) popupMessageText.text = message ?? "";
        NavigateTo(ScreenId.AR_Popup);
    }

    public void ShowCluePuzzle(string anchorId, string title, string hint)
    {
        _activePuzzleAnchorId = anchorId;

        if (puzzleTitleText != null) puzzleTitleText.text = title ?? "";
        if (puzzleHintText != null) puzzleHintText.text = hint ?? "";
        if (puzzleAnswerInput != null) puzzleAnswerInput.text = "";
        if (puzzleFeedbackText != null) puzzleFeedbackText.text = "";

        NavigateTo(ScreenId.AR_Puzzle);
    }

    public void SetPuzzleFeedback(string message)
    {
        if (puzzleFeedbackText != null)
            puzzleFeedbackText.text = message ?? "";
    }

    private void OnPuzzleSubmitClicked()
    {
        if (anchorsRealtime == null)
            anchorsRealtime = FindFirstObjectByType<AnchorsRealtime>();

        if (anchorsRealtime == null)
        {
            Debug.LogWarning("[AppUI] Cannot submit puzzle: AnchorsRealtime not found.");
            return;
        }

        string answer = puzzleAnswerInput != null ? puzzleAnswerInput.text : "";
        anchorsRealtime.SubmitPuzzleAnswer(_activePuzzleAnchorId, answer);
    }

    // Map flow
    public void ShowMapDetails() => NavigateTo(ScreenId.Map_Details);

    // Profile flow
    public void ShowEditProfile() => NavigateTo(ScreenId.Profile_Edit);
    public void ShowSettings() => NavigateTo(ScreenId.Profile_Settings);
    public void ShowChangePassword() => NavigateTo(ScreenId.Profile_ChangePassword);
    public void ShowCompletedMaps(string uid)       { CompletedMapsUI.TargetUid = uid; CompletedMapsUI.Mode = CompletedMapsUI.ListMode.Completed; NavigateTo(ScreenId.Profile_CompletedMaps); }
    public void ShowCreatedMaps(string uid)         { CompletedMapsUI.TargetUid = uid; CompletedMapsUI.Mode = CompletedMapsUI.ListMode.Created;   NavigateTo(ScreenId.Profile_CompletedMaps); }
    public void ShowSocialCompletedMaps(string uid) { CompletedMapsUI.TargetUid = uid; CompletedMapsUI.Mode = CompletedMapsUI.ListMode.Completed; NavigateTo(ScreenId.Social_CompletedMaps); }
    public void ShowSocialCreatedMaps(string uid)   { CompletedMapsUI.TargetUid = uid; CompletedMapsUI.Mode = CompletedMapsUI.ListMode.Created;   NavigateTo(ScreenId.Social_CompletedMaps); }

    // Social flow

    /// <summary>
    /// Navigates to Screen_DetailedUser for the given user uid.
    /// Called by LeaderboardUI when a row is tapped.
    /// </summary>
    public void ShowDetailedUser(string uid)
    {
        DetailedUserUI.SelectedUid = uid;
        NavigateTo(ScreenId.Social_DetailedUser);
    }

    // Create flow
    public void ShowAddNewMap() => NavigateTo(ScreenId.Create_AddNewMap);
    public void ShowEditMap() => NavigateTo(ScreenId.Create_EditMap);
    public void ShowEditClues() => NavigateTo(ScreenId.Create_EditClues);
    public void ShowEditClue() => NavigateTo(ScreenId.Create_EditClue);
    public void ShowEditClueLocAR()
    {
        EnsureCreateSelectedMapAppliedToProvider(() => NavigateTo(ScreenId.Create_EditClueLocAR));
    }

    private void EnsureCreateSelectedMapAppliedToProvider(System.Action onReady = null)
    {
        string dbKey = EditMapUI.SelectedMapId;
        if (string.IsNullOrEmpty(dbKey))
        {
            Debug.LogWarning("[AppUI] Cannot apply selected map before EditClueLocAR: SelectedMapId (dbKey) is empty.");
            onReady?.Invoke();
            return;
        }

        if (mapRootProvider == null)
            mapRootProvider = FindFirstObjectByType<MapRootProvider>();

        if (mapRootProvider == null)
        {
            Debug.LogWarning("[AppUI] Cannot apply selected map before EditClueLocAR: MapRootProvider not found.");
            onReady?.Invoke();
            return;
        }

        // Fast path: static cache already has the immersalMapId
        if (EditMapUI.SelectedImmersalMapId > 0)
        {
            mapRootProvider.SetMapByDbKey(dbKey, EditMapUI.SelectedImmersalMapId, EditMapUI.SelectedMapName);
            Debug.Log($"[AppUI] Applied selected map before EditClueLocAR. dbKey={dbKey}, immersalId={EditMapUI.SelectedImmersalMapId}");
            onReady?.Invoke();
            return;
        }

        // Slow path: static cache is stale — fetch immersalMapId from Firebase using the dbKey.
        Debug.Log($"[AppUI] SelectedImmersalMapId not cached. Fetching from Firebase for dbKey={dbKey}...");
        var db = FirebaseInitializer.DB;
        if (db == null)
        {
            Debug.LogWarning("[AppUI] Firebase DB not ready; navigating without re-applying map.");
            onReady?.Invoke();
            return;
        }

        db.Child("maps").Child(dbKey).GetValueAsync()
          .ContinueWithOnMainThread(t =>
          {
              if (t.IsCompletedSuccessfully && t.Result != null && t.Result.Exists)
              {
                  var snap = t.Result;
                  string name = snap.Child("name").Value?.ToString() ?? "";
                  int immersalId = 0;
                  int.TryParse(snap.Child("immersalMapId").Value?.ToString(), out immersalId);

                  if (immersalId > 0)
                  {
                      EditMapUI.UpdateSelectedMap(dbKey, immersalId, name);
                      mapRootProvider.SetMapByDbKey(dbKey, immersalId, name);
                      Debug.Log($"[AppUI] Applied selected map before EditClueLocAR (via Firebase). dbKey={dbKey}, immersalId={immersalId}");
                  }
                  else
                  {
                      Debug.LogWarning($"[AppUI] Map record {dbKey} has no immersalMapId; cannot apply.");
                  }
              }
              else
              {
                  Debug.LogWarning($"[AppUI] Failed to fetch map record for dbKey={dbKey}.");
              }

              onReady?.Invoke();
          });
    }

    // Admin flow
    public void ShowAdminMapDetailsApproval() => NavigateTo(ScreenId.Admin_MapDetailsApproval);
    public void ShowAdminClueDetailsApproval() => NavigateTo(ScreenId.Admin_ClueDetailsApproval);
    public void ShowAdminMapRejectFeedback() => NavigateTo(ScreenId.Admin_MapRejectFeedback);

    public void SetAdminButtonVisible(bool visible)
    {
        currentUserIsAdmin = visible;

        if (btnAdmin != null)
            btnAdmin.gameObject.SetActive(visible);

        // Spatial UI: admin olarak giriş yapıldığında panelAdmin'i hemen göster;
        // çıkış yapıldığında gizle.
        if (panelAdmin != null)
            panelAdmin.SetActive(visible);

        if (!visible && _panelCurrent[PanelId.Admin] == ScreenId.Admin_MapsStatus)
            ResetPanel(PanelId.Profile);
    }
}