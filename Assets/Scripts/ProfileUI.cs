using System;
using Firebase.Auth;
using Firebase.Database;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ProfileUI : MonoBehaviour
{
    [Header("User Info")]
    public TMP_Text txtFullName;
    public TMP_Text txtLevel;
    public TMP_Text txtXP;

    [Header("Stats")]
    public TMP_Text txtCreatedMaps;
    public TMP_Text txtCompletedMaps;
    public TMP_Text txtBio;

    [Header("Navigation")]
    public AppUIManager appUIManager;
    public Button btnCompletedMapsCard;
    public Button btnCreatedMapsCard;

    [Header("Top Bar")]
    public TMP_Text txtTitleProfile;

    [Header("Profile Photo")]
    public Image imgProfilePhoto;

    // ── Real-time listener state ─────────────────────────────────────────────
    private DatabaseReference                  _userRef;
    private EventHandler<ValueChangedEventArgs> _handler;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (btnCompletedMapsCard != null)
            btnCompletedMapsCard.onClick.AddListener(OnClickCompletedMaps);
        if (btnCreatedMapsCard != null)
            btnCreatedMapsCard.onClick.AddListener(OnClickCreatedMaps);
    }

    private void OnEnable()  => SubscribeRealtime();
    private void OnDisable() => UnsubscribeRealtime();
    private void OnDestroy() => UnsubscribeRealtime();

    // ── Real-time subscription ───────────────────────────────────────────────

    private void SubscribeRealtime()
    {
        UnsubscribeRealtime();

        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            Debug.LogWarning("[ProfileUI] No user.");
            return;
        }

        _userRef = FirebaseInitializer.DB
            .Child("users").Child(user.UserId);

        _handler = (_, args) =>
        {
            if (args.DatabaseError != null)
            {
                Debug.LogWarning("[ProfileUI] Firebase error: " + args.DatabaseError.Message);
                return;
            }

            var data = args.Snapshot;
            if (data == null || !data.Exists) return;

            ApplyData(data);
        };

        _userRef.ValueChanged += _handler;
    }

    private void UnsubscribeRealtime()
    {
        if (_userRef != null && _handler != null)
            _userRef.ValueChanged -= _handler;

        _userRef = null;
        _handler = null;
    }

    // ── Data apply ───────────────────────────────────────────────────────────

    private void ApplyData(DataSnapshot data)
    {
        if (txtFullName != null)
            txtFullName.text = GetValue(data, "fullName", GetValue(data, "userName", "No Name"));

        if (txtTitleProfile != null)
            txtTitleProfile.text = GetValue(data, "userName", "Profile");

        if (txtBio != null)
            txtBio.text = GetValue(data, "bio", "-");

        string photoUrl = GetValue(data, "photoUrl", "");
        if (!string.IsNullOrEmpty(photoUrl) && imgProfilePhoto != null)
            ImageCache.Load(photoUrl, sprite =>
            {
                if (imgProfilePhoto == null) return;
                imgProfilePhoto.sprite = sprite;
                imgProfilePhoto.preserveAspect = true;
            });

        int xp    = GetInt(data, "xp");
        int level = GetInt(data, "level");

        if (txtXP    != null) txtXP.text    = "XP: "    + xp;
        if (txtLevel != null) txtLevel.text = "Level: " + level;

        var uid2 = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser?.UserId;
        if (uid2 != null)
        {
            // Count dynamically so deleted maps don't inflate the numbers.
            if (txtCreatedMaps   != null)
                CountCreatedMaps(uid2,    count => { if (txtCreatedMaps   != null) txtCreatedMaps.text   = count.ToString(); });
            if (txtCompletedMaps != null)
                CountCompletedMaps(uid2,  count => { if (txtCompletedMaps != null) txtCompletedMaps.text = count.ToString(); });
        }
    }

    private static void CountCreatedMaps(string uid, Action<int> callback)
    {
        FirebaseInitializer.DB.Child("maps")
            .GetValueAsync().ContinueWith(t =>
            {
                int count = 0;
                if (t.IsCompletedSuccessfully && t.Result != null && t.Result.Exists)
                    foreach (var child in t.Result.Children)
                    {
                        var creatorSnap = child.Child("creatorUid");
                        if (!creatorSnap.Exists || creatorSnap.Value?.ToString() != uid) continue;
                        var statusSnap  = child.Child("approvalStatus");
                        if (statusSnap.Exists && statusSnap.Value?.ToString() == "approved")
                            count++;
                    }
                callback(count);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
    }

    private static void CountCompletedMaps(string uid, Action<int> callback)
    {
        FirebaseInitializer.DB.Child("users").Child(uid).Child("progress")
            .GetValueAsync().ContinueWith(t =>
            {
                int count = 0;
                if (t.IsCompletedSuccessfully && t.Result != null && t.Result.Exists)
                    foreach (var child in t.Result.Children)
                    {
                        var flag = child.Child("mapCompleted");
                        if (!flag.Exists || flag.Value == null) continue;
                        bool v = false;
                        bool.TryParse(flag.Value.ToString(), out v);
                        if (!v) v = flag.Value.ToString() == "True";
                        if (v) count++;
                    }
                callback(count);
            }, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
    }

    // ── Button handlers ──────────────────────────────────────────────────────

    private void OnClickCompletedMaps()
    {
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;
        if (appUIManager == null) appUIManager = FindFirstObjectByType<AppUIManager>();
        appUIManager?.ShowCompletedMaps(user.UserId);
    }

    private void OnClickCreatedMaps()
    {
        var user = Firebase.Auth.FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null) return;
        if (appUIManager == null) appUIManager = FindFirstObjectByType<AppUIManager>();
        appUIManager?.ShowCreatedMaps(user.UserId);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string GetValue(DataSnapshot data, string key, string fallback)
        => data.Child(key).Exists && data.Child(key).Value != null
            ? data.Child(key).Value.ToString()
            : fallback;

    private static int GetInt(DataSnapshot data, string key)
    {
        var c = data.Child(key);
        if (!c.Exists || c.Value == null) return 0;
        int.TryParse(c.Value.ToString(), out int val);
        return val;
    }
}
