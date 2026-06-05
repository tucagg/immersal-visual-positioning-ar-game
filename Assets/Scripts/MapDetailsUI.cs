using System;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attached to Screen_MapDetails.
/// Reads MapsScreenUI.SelectedMapDbKey and loads the full map detail from Firebase.
///
/// Inspector wiring:
///   Title_MapName          → TopBar_MapDetails / Title_MapName
///   txtCategoryValue       → CategorySection / Txt_CategoryValue
///   txtRatingValue         → RatingSection / Txt_RatingValue
///   txtCommentsTitle       → RatingSection / Txt_CommentsTitle
///   commentRowTemplate     → CommentRow_Template  (kept inactive)
///   commentRowContainer    → CommentsScrollView / Viewport / Content
///   txtCreatorValue        → CreatorSection / Txt_CreatorValue
///   txtCreatedAtValue      → CreatorSection / Txt_CreatedAtValue
/// </summary>
public class MapDetailsUI : MonoBehaviour
{
    [Header("Top Bar")]
    public TMP_Text txtMapName;

    [Header("Category")]
    public TMP_Text txtCategoryValue;

    [Header("Rating")]
    public TMP_Text txtRatingValue;

    [Header("Comments")]
    public GameObject commentRowTemplate;
    public Transform  commentRowContainer;

    [Header("Creator")]
    public TMP_Text txtCreatorValue;
    public TMP_Text txtCreatedAtValue;

    // ── Internal ─────────────────────────────────────────────────────────────

    private readonly List<GameObject> _spawnedRows = new();
    private DatabaseReference _db;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        _db = FirebaseInitializer.DB;

        if (commentRowTemplate != null)
            commentRowTemplate.SetActive(false);

        ClearComments();
        ResetUI();
        LoadMap(MapsScreenUI.SelectedMapDbKey);
    }

    private void OnDisable() => ClearComments();

    // ── Load ─────────────────────────────────────────────────────────────────

    private void LoadMap(string mapDbKey)
    {
        if (string.IsNullOrEmpty(mapDbKey))
        {
            Debug.LogWarning("[MapDetailsUI] SelectedMapDbKey is empty.");
            return;
        }

        _db.Child("maps").Child(mapDbKey).GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsCompletedSuccessfully || task.Result == null || !task.Result.Exists)
                {
                    Debug.LogWarning("[MapDetailsUI] Map not found: " + mapDbKey);
                    return;
                }

                var snap = task.Result;

                // ── Basic fields ─────────────────────────────────────────────
                string name        = ChildStr(snap, "name",       "–");
                string category    = ChildStr(snap, "category",   "–");
                int    ratingSum   = ChildInt(snap, "ratingSum");
                int    ratingCount = ChildInt(snap, "ratingCount");
                string creatorUid  = ChildStr(snap, "creatorUid", ChildStr(snap, "createdByUid", ""));
                long   createdAt   = ChildLong(snap, "createdAt");

                // ── Map name ─────────────────────────────────────────────────
                if (txtMapName != null)      txtMapName.text      = name;
                if (txtCategoryValue != null) txtCategoryValue.text = $"Category: {category}";

                // ── Rating ───────────────────────────────────────────────────
                if (txtRatingValue != null)
                {
                    txtRatingValue.text = ratingCount > 0
                        ? $"Rating: {(float)ratingSum / ratingCount:F1}/5"
                        : "Rating: –";
                }

                // ── Created at ───────────────────────────────────────────────
                if (txtCreatedAtValue != null)
                {
                    txtCreatedAtValue.text = createdAt > 0
                        ? DateTimeOffset.FromUnixTimeMilliseconds(createdAt)
                                        .LocalDateTime.ToString("MMM d, yyyy")
                        : "–";
                }

                // ── Creator username (second Firebase read) ──────────────────
                LoadCreatorName(creatorUid);

                // ── Reviews ──────────────────────────────────────────────────
                var reviewsSnap = snap.Child("reviews");
                LoadComments(reviewsSnap);
            });
    }

    // ── Creator ──────────────────────────────────────────────────────────────

    private void LoadCreatorName(string creatorUid)
    {
        if (string.IsNullOrEmpty(creatorUid))
        {
            if (txtCreatorValue != null) txtCreatorValue.text = "–";
            return;
        }

        _db.Child("users").Child(creatorUid).Child("userName").GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                string name = "–";
                if (task.IsCompletedSuccessfully && task.Result != null
                    && task.Result.Exists && task.Result.Value != null)
                {
                    name = task.Result.Value.ToString();
                }

                if (txtCreatorValue != null) txtCreatorValue.text = name;
            });
    }

    // ── Comments ─────────────────────────────────────────────────────────────

    private void LoadComments(DataSnapshot reviewsSnap)
    {
        ClearComments();

        if (reviewsSnap == null || !reviewsSnap.Exists) return;

        // Collect non-empty feedback reviews, newest first.
        var reviews = new List<(string username, int rating, string feedback, long timestamp)>();

        foreach (DataSnapshot r in reviewsSnap.Children)
        {
            string feedback = ChildStr(r, "feedback", "").Trim();

            reviews.Add((
                username:  ChildStr(r, "userName", "Anonymous"),
                rating:    ChildInt(r, "rating"),
                feedback:  string.IsNullOrEmpty(feedback) ? "–" : feedback,
                timestamp: ChildLong(r, "timestamp")
            ));
        }

        reviews.Sort((a, b) => b.timestamp.CompareTo(a.timestamp));

        if (commentRowTemplate == null || commentRowContainer == null) return;

        foreach (var (username, rating, feedback, _) in reviews)
        {
            GameObject row = Instantiate(commentRowTemplate, commentRowContainer);
            row.SetActive(true);
            _spawnedRows.Add(row);

            SetChildText(row, "Txt_Username", username);
            SetChildText(row, "Txt_Rating",   rating > 0 ? $"{rating} / 5" : "");
            SetChildText(row, "Txt_Comment",  feedback);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ResetUI()
    {
        if (txtMapName        != null) txtMapName.text        = "";
        if (txtCategoryValue  != null) txtCategoryValue.text  = "";
        if (txtRatingValue    != null) txtRatingValue.text    = "";
        if (txtCreatorValue   != null) txtCreatorValue.text   = "";
        if (txtCreatedAtValue != null) txtCreatedAtValue.text = "";
    }

    private void ClearComments()
    {
        foreach (var go in _spawnedRows)
            if (go != null) Destroy(go);
        _spawnedRows.Clear();
    }

    private static void SetChildText(GameObject root, string childName, string value)
    {
        var t = root.transform.Find(childName);
        if (t == null) return;
        var txt = t.GetComponent<TMP_Text>();
        if (txt != null) txt.text = value;
    }

    private static string ChildStr(DataSnapshot s, string key, string fallback)
    {
        var c = s.Child(key);
        return (c.Exists && c.Value != null) ? c.Value.ToString() : fallback;
    }

    private static int ChildInt(DataSnapshot s, string key)
    {
        var c = s.Child(key);
        if (!c.Exists || c.Value == null) return 0;
        int.TryParse(c.Value.ToString(), out int v);
        return v;
    }

    private static long ChildLong(DataSnapshot s, string key)
    {
        var c = s.Child(key);
        if (!c.Exists || c.Value == null) return 0;
        long.TryParse(c.Value.ToString(), out long v);
        return v;
    }
}
