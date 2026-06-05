using System;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attached to Screen_DetailedUser.
///
/// Displays another user's public profile — identical layout to Screen_Profile
/// but without any edit controls.
///
/// The uid to display is passed via the static <see cref="SelectedUid"/> property,
/// which AppUIManager sets before navigating here (same pattern as EditMapUI.SelectedMapId).
///
/// Inspector wiring:
///   imgProfilePhoto      → Img_ProfilePhoto
///   txtUsername          → UserInfoBlock / Txt_Username   (top-level display name)
///   txtXP                → UserInfoBlock / Txt_XP         (optional)
///   txtCreatedMapsValue  → CreatedMapsCard / Txt_CreatedMapsValue
///   txtCompletedMapsValue→ CompletedMapsCard / Txt_CompletedMapsValue
///   txtBioValue          → UserBioSection / Txt_BioValue
///   txtTopBarTitle       → TopBar_DetailedUser / Txt_Title (optional)
/// </summary>
public class DetailedUserUI : MonoBehaviour
{
    // ── Static context (set by AppUIManager before navigating) ───────────────

    /// <summary>UID of the user whose profile should be displayed.</summary>
    public static string SelectedUid { get; set; } = "";

    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Top Bar")]
    [Tooltip("Title_DetailedUser — shows the user's username")]
    public TMP_Text txtTopBarTitle;

    [Header("Header")]
    public Image    imgProfilePhoto;
    [Tooltip("Txt_FullName — shows the user's full name")]
    public TMP_Text txtFullName;
    [Tooltip("Txt_Level")]
    public TMP_Text txtLevel;
    [Tooltip("Txt_XP")]
    public TMP_Text txtXP;

    [Header("Stats")]
    public TMP_Text txtCreatedMapsValue;
    public TMP_Text txtCompletedMapsValue;

    [Header("Bio")]
    public TMP_Text txtBioValue;

    [Header("Navigation")]
    public AppUIManager appUIManager;
    public Button btnCompletedMapsCard;
    public Button btnCreatedMapsCard;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (btnCompletedMapsCard != null)
            btnCompletedMapsCard.onClick.AddListener(OnClickCompletedMaps);
        if (btnCreatedMapsCard != null)
            btnCreatedMapsCard.onClick.AddListener(OnClickCreatedMaps);
    }

    private void OnEnable()
    {
        LoadProfile(SelectedUid);
    }

    private void OnClickCompletedMaps()
    {
        if (string.IsNullOrEmpty(SelectedUid)) return;
        if (appUIManager == null) appUIManager = FindFirstObjectByType<AppUIManager>();
        appUIManager?.ShowSocialCompletedMaps(SelectedUid);
    }

    private void OnClickCreatedMaps()
    {
        if (string.IsNullOrEmpty(SelectedUid)) return;
        if (appUIManager == null) appUIManager = FindFirstObjectByType<AppUIManager>();
        appUIManager?.ShowSocialCreatedMaps(SelectedUid);
    }

    // ── Profile loading ──────────────────────────────────────────────────────

    private void LoadProfile(string uid)
    {
        if (string.IsNullOrEmpty(uid))
        {
            Debug.LogWarning("[DetailedUserUI] SelectedUid is empty.");
            return;
        }

        var db = FirebaseInitializer.DB;

        db.Child("users").Child(uid).GetValueAsync()
          .ContinueWithOnMainThread(task =>
          {
              if (!task.IsCompletedSuccessfully || task.Result == null || !task.Result.Exists)
              {
                  Debug.LogWarning("[DetailedUserUI] User data not found for uid: " + uid);
                  return;
              }

              var data = task.Result;

              string username     = ChildStr(data, "userName",  "Unknown");
              string fullName     = ChildStr(data, "fullName",  username);
              string bio          = ChildStr(data, "bio",       "-");
              string photoUrl     = ChildStr(data, "photoUrl",  "");
              int    xp           = ChildInt(data, "xp");
              int    level        = ChildInt(data, "level");

              // Top bar shows @username
              if (txtTopBarTitle       != null) txtTopBarTitle.text        = username;
              // Body fields
              if (txtFullName          != null) txtFullName.text           = fullName;
              if (txtLevel             != null) txtLevel.text              = $"Level {level}";
              if (txtXP                != null) txtXP.text                 = $"{xp} XP";
              if (txtBioValue          != null) txtBioValue.text           = bio;

              // Count dynamically so deleted maps don't inflate the numbers.
              if (txtCreatedMapsValue != null)
                  CountCreatedMaps(uid, count =>
                  {
                      if (txtCreatedMapsValue != null)
                          txtCreatedMapsValue.text = count.ToString();
                  });
              if (txtCompletedMapsValue != null)
                  CountCompletedMaps(uid, count =>
                  {
                      if (txtCompletedMapsValue != null)
                          txtCompletedMapsValue.text = count.ToString();
                  });

              if (!string.IsNullOrEmpty(photoUrl) && imgProfilePhoto != null)
                  ImageCache.Load(photoUrl, sprite =>
                  {
                      if (imgProfilePhoto == null) return;
                      imgProfilePhoto.sprite = sprite;
                      imgProfilePhoto.preserveAspect = true;
                  });
          });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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

    private static string ChildStr(DataSnapshot snap, string key, string fallback)
    {
        var child = snap.Child(key);
        return (child.Exists && child.Value != null) ? child.Value.ToString() : fallback;
    }

    private static int ChildInt(DataSnapshot snap, string key)
    {
        var child = snap.Child(key);
        if (!child.Exists || child.Value == null) return 0;
        int.TryParse(child.Value.ToString(), out int val);
        return val;
    }
}
