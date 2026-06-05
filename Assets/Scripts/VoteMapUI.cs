using System;
using System.Collections.Generic;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Handles Screen_VoteMap — shown once after a user fully completes a map.
///
/// Hierarchy expected:
///   Txt_Info            → TMP_Text      – shows map name
///   Dropdown_Rating     → TMP_Dropdown  – options "1" … "5" (index 0 = 1 star)
///   Input_MapFeedback   → TMP_InputField – optional written comment
///   Txt_Status          → TMP_Text      – feedback / error messages
///   Btn_Submit          → Button
///
/// Firebase structure written (under maps/{mapDbKey}/):
///   reviews/{uid}  →  { rating, feedback, userName, uid, timestamp }
///   ratingSum      →  updated aggregate (int)
///   ratingCount    →  updated aggregate (int)
///
/// OnEnable resets all state so the screen is clean for every new map.
/// </summary>
public class VoteMapUI : MonoBehaviour
{
    [Header("UI References")]
    public TMP_Text       txtInfo;
    public TMP_Dropdown   dropdownRating;
    public TMP_InputField inputFeedback;
    public TMP_Text       txtStatus;
    public Button         btnSubmit;

    // ── internal state ──────────────────────────────────────────────────────

    private string            _mapDbKey;
    private string            _mapName;
    private DatabaseReference _db;

    // SC-3 timing
    private float _submitPressTime;
    private int   _writeAttempts;
    private int   _writeSuccesses;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        _db = FirebaseInitializer.DB;

        // Snapshot the map that was just completed.
        var provider = FindFirstObjectByType<MapRootProvider>(FindObjectsInactive.Include);
        if (provider != null)
        {
            _mapDbKey = provider.SelectedMapDbKey;
            _mapName  = provider.mapName;
        }
        else
        {
            _mapDbKey = "";
            _mapName  = "";
        }

        // ── Reset UI every time the screen opens ────────────────────────────
        if (txtInfo != null)
            txtInfo.text = string.IsNullOrEmpty(_mapName) ? "Map" : _mapName;

        if (dropdownRating != null)
            dropdownRating.value = 0;           // back to "1 star"

        if (inputFeedback != null)
            inputFeedback.text = "";

        SetStatus("");

        if (btnSubmit != null)
        {
            btnSubmit.interactable = true;
            btnSubmit.onClick.RemoveListener(OnClickSubmit);
            btnSubmit.onClick.AddListener(OnClickSubmit);
        }
    }

    private void OnDisable()
    {
        if (btnSubmit != null)
            btnSubmit.onClick.RemoveListener(OnClickSubmit);
    }

    // ── Submit ───────────────────────────────────────────────────────────────

    private void OnClickSubmit()
    {
        // Dropdown index 0 → 1 star … index 4 → 5 stars.
        int    rating   = dropdownRating != null ? dropdownRating.value + 1 : 1;
        string feedback = inputFeedback  != null ? inputFeedback.text.Trim() : "";

        string uid = FirebaseAuth.DefaultInstance.CurrentUser?.UserId;
        if (string.IsNullOrEmpty(uid))      { SetStatus("Not signed in."); return; }
        if (string.IsNullOrEmpty(_mapDbKey)){ SetStatus("Map not found."); return; }

        _submitPressTime = Time.realtimeSinceStartup;
        _writeAttempts++;
        Debug.Log(
            $"[SC3][RATING] Submit pressed" +
            $" | mapKey={_mapDbKey}" +
            $" | rating={rating}★" +
            $" | hasFeedback={!string.IsNullOrEmpty(feedback)}" +
            $" | attempt=#{_writeAttempts}" +
            $" | time={DateTime.UtcNow:HH:mm:ss.fff} UTC"
        );

        SetStatus("Submitting…");
        if (btnSubmit != null) btnSubmit.interactable = false;

        SubmitReview(uid, _mapDbKey, rating, feedback);
    }

    // ── Firebase ─────────────────────────────────────────────────────────────

    private void SubmitReview(string uid, string mapDbKey, int rating, string feedback)
    {
        var mapRef    = _db.Child("maps").Child(mapDbKey);
        var reviewRef = mapRef.Child("reviews").Child(uid);

        // Prevent double-voting: abort silently if the user already reviewed.
        reviewRef.GetValueAsync().ContinueWithOnMainThread(checkTask =>
        {
            bool alreadyVoted = checkTask.IsCompletedSuccessfully
                                && checkTask.Result != null
                                && checkTask.Result.Exists;

            if (alreadyVoted)
            {
                Debug.Log("[VoteMapUI] User already reviewed this map — closing.");
                Close();
                return;
            }

            // Fetch userName so reviews can be displayed nicely later.
            _db.Child("users").Child(uid).Child("userName").GetValueAsync()
                .ContinueWithOnMainThread(nameTask =>
                {
                    string userName = "";
                    if (nameTask.IsCompletedSuccessfully
                        && nameTask.Result != null
                        && nameTask.Result.Exists
                        && nameTask.Result.Value != null)
                    {
                        userName = nameTask.Result.Value.ToString();
                    }

                    // Build the review document.
                    var review = new Dictionary<string, object>
                    {
                        { "rating",    rating             },
                        { "feedback",  feedback           },
                        { "userName",  userName           },
                        { "uid",       uid                },
                        { "timestamp", ServerValue.Timestamp }
                    };

                    reviewRef.SetValueAsync(review).ContinueWithOnMainThread(writeTask =>
                    {
                        float writeElapsed = (Time.realtimeSinceStartup - _submitPressTime) * 1000f;

                        if (!writeTask.IsCompletedSuccessfully)
                        {
                            Debug.LogError(
                                $"[SC3][RATING] Review write FAILED" +
                                $" | attempt=#{_writeAttempts}" +
                                $" | success_rate={_writeSuccesses}/{_writeAttempts}" +
                                $" | elapsed={writeElapsed:F0}ms" +
                                $" | error={writeTask.Exception?.GetBaseException()?.Message}"
                            );
                            SetStatus("Submit failed. Try again.");
                            if (btnSubmit != null) btnSubmit.interactable = true;
                            return;
                        }

                        _writeSuccesses++;
                        Debug.Log(
                            $"[SC3][RATING] Review write SUCCESS" +
                            $" | rating={rating}★  user={userName}" +
                            $" | elapsed={writeElapsed:F0}ms" +
                            $" | success_rate={_writeSuccesses}/{_writeAttempts}" +
                            $" | write_status={(writeElapsed <= 10000f ? "PASS ✓" : "SLOW ✗")}"
                        );

                        // Update the map's aggregate counters.
                        UpdateAggregateRating(mapRef, rating, feedback);
                    });
                });
        });
    }

    /// <summary>
    /// Reads current ratingSum / ratingCount, increments both, writes back.
    /// If the reviewer left a comment (feedback != ""), also awards XP to the map creator.
    /// Closes the screen when done (even on failure — the review was already saved).
    /// </summary>
    private void UpdateAggregateRating(DatabaseReference mapRef, int newRating, string feedback)
    {
        mapRef.GetValueAsync().ContinueWithOnMainThread(snapTask =>
        {
            int    currentSum   = 0;
            int    currentCount = 0;
            string creatorUid   = "";

            if (snapTask.IsCompletedSuccessfully && snapTask.Result != null && snapTask.Result.Exists)
            {
                var snap = snapTask.Result;
                TryParseChild(snap, "ratingSum",   out currentSum);
                TryParseChild(snap, "ratingCount", out currentCount);

                if (snap.Child("creatorUid").Exists && snap.Child("creatorUid").Value != null)
                    creatorUid = snap.Child("creatorUid").Value.ToString();
            }

            int newSum   = currentSum   + newRating;
            int newCount = currentCount + 1;

            var updates = new Dictionary<string, object>
            {
                { "ratingSum",   newSum   },
                { "ratingCount", newCount }
            };

            mapRef.UpdateChildrenAsync(updates).ContinueWithOnMainThread(updateTask =>
            {
                float totalElapsed = (Time.realtimeSinceStartup - _submitPressTime) * 1000f;

                if (!updateTask.IsCompletedSuccessfully)
                {
                    Debug.LogWarning(
                        $"[SC3][RATING] Aggregate update FAILED" +
                        $" | elapsed_from_submit={totalElapsed:F0}ms" +
                        $" | error={updateTask.Exception?.GetBaseException()?.Message}"
                    );
                }
                else
                {
                    Debug.Log(
                        $"[SC3][RATING] Aggregate updated" +
                        $" | ratingSum={newSum}  ratingCount={newCount}  avg={newSum / (float)newCount:F2}" +
                        $" | elapsed_from_submit={totalElapsed:F0}ms" +
                        $" | reflection_status={(totalElapsed <= 10000f ? "PASS ✓ (≤10 s)" : "FAIL ✗ (>10 s)")}"
                    );
                }

                // Award XP to the map creator only if the reviewer left a written comment.
                if (!string.IsNullOrEmpty(feedback) && !string.IsNullOrEmpty(creatorUid))
                {
                    const int XpMultiplier = 10;          // 1★ = 10 XP … 5★ = 50 XP
                    int xpAmount = newRating * XpMultiplier;
                    AddXpForUser(creatorUid, xpAmount, newRating);
                }

                Close();  // always close after successful review write
            });
        });
    }

    /// <summary>
    /// Reads the user's current XP, adds <paramref name="amount"/>, recalculates level, writes back.
    /// </summary>
    private void AddXpForUser(string uid, int amount, int rating)
    {
        if (string.IsNullOrEmpty(uid)) return;

        var userRef = _db.Child("users").Child(uid);
        userRef.Child("xp").GetValueAsync().ContinueWithOnMainThread(t =>
        {
            int current = 0;
            if (t.IsCompletedSuccessfully && t.Result != null && t.Result.Exists)
                int.TryParse(t.Result.Value.ToString(), out current);

            int newXp    = current + amount;
            int newLevel = Mathf.FloorToInt(Mathf.Sqrt(newXp / 100f)) + 1;

            userRef.UpdateChildrenAsync(new Dictionary<string, object>
            {
                { "xp",    newXp    },
                { "level", newLevel }
            });

            int expectedLevel = Mathf.FloorToInt(Mathf.Sqrt(newXp / 100f)) + 1;
            Debug.Log(
                $"[SC1][XP] REVIEW_REWARD" +
                $" | uid={uid}" +
                $" | rating={rating}★  formula={rating}×10" +
                $" | awarded=+{amount} XP" +
                $" | before={current}  after={newXp}" +
                $" | level={Mathf.FloorToInt(Mathf.Sqrt(current / 100f)) + 1}→{newLevel}" +
                $" | calc_match={(newLevel == expectedLevel ? "✓ PASS" : "✗ FAIL")}"
            );
        });
    }

    // ── Close ────────────────────────────────────────────────────────────────

    private void Close()
    {
        var appUI = FindFirstObjectByType<AppUIManager>();
        if (appUI != null)
            appUI.GoBack();
        else
            gameObject.SetActive(false);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SetStatus(string msg)
    {
        if (txtStatus != null)
            txtStatus.text = msg;
    }

    private static void TryParseChild(DataSnapshot snap, string key, out int result)
    {
        result = 0;
        if (snap.Child(key).Exists && snap.Child(key).Value != null)
            int.TryParse(snap.Child(key).Value.ToString(), out result);
    }
}
