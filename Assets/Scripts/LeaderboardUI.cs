using System;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attached to Screen_Social.
///
/// Subscribes to a Firebase real-time listener on enable so the leaderboard
/// updates automatically whenever any user's XP changes (e.g. after a map
/// approval or a rated review) — no manual refresh needed.
///
/// Inspector wiring:
///   rowTemplate  → UserRow_Template (child of Content, stays inactive — used as source)
///   rowContainer → Content Transform
///   txtStatus    → optional status label
///   appUIManager → AppUIManager
/// </summary>
public class LeaderboardUI : MonoBehaviour
{
    [Header("Row Template / Container")]
    [Tooltip("UserRow_Template inside Content — kept inactive, cloned for each user.")]
    public GameObject rowTemplate;

    [Tooltip("Content Transform of the scroll view.")]
    public Transform rowContainer;

    [Header("Status")]
    public TMP_Text txtStatus;

    [Header("Navigation")]
    public AppUIManager appUIManager;

    private const int TopN = 10;

    private readonly List<GameObject> _spawnedRows = new();

    // Real-time listener state
    private Query                              _query;
    private EventHandler<ValueChangedEventArgs> _handler;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (rowTemplate != null)
            rowTemplate.SetActive(false);

        SubscribeRealtime();
    }

    private void OnDisable()
    {
        UnsubscribeRealtime();
    }

    private void OnDestroy()
    {
        UnsubscribeRealtime();
    }

    // ── Real-time subscription ───────────────────────────────────────────────

    private void SubscribeRealtime()
    {
        UnsubscribeRealtime(); // tear down any previous listener first

        _query = FirebaseInitializer.DB
            .Child("users")
            .OrderByChild("xp")
            .LimitToLast(TopN);

        _handler = (_, args) =>
        {
            // ValueChanged fires on the main thread via Firebase Unity's dispatcher.
            if (args.DatabaseError != null)
            {
                SetStatus("Could not load leaderboard.");
                Debug.LogWarning("[LeaderboardUI] Firebase error: " + args.DatabaseError.Message);
                return;
            }

            var snapshot = args.Snapshot;
            if (snapshot == null || !snapshot.Exists)
            {
                SetStatus("No users found.");
                return;
            }

            var entries = new List<(string uid, string username, int xp, string photoUrl)>();

            foreach (DataSnapshot snap in snapshot.Children)
            {
                string uid      = snap.Key;
                string username = ChildStr(snap, "userName", ChildStr(snap, "fullName", "Unknown"));
                int    xp       = ChildInt(snap, "xp");
                string photo    = ChildStr(snap, "photoUrl", "");
                entries.Add((uid, username, xp, photo));
            }

            entries.Reverse(); // LimitToLast returns ascending → flip to descending

            SetStatus("");
            SpawnRows(entries);
        };

        SetStatus("Loading…");
        _query.ValueChanged += _handler;
    }

    private void UnsubscribeRealtime()
    {
        if (_query != null && _handler != null)
            _query.ValueChanged -= _handler;

        _query   = null;
        _handler = null;
    }

    // ── Row spawning ─────────────────────────────────────────────────────────

    private void SpawnRows(List<(string uid, string username, int xp, string photoUrl)> entries)
    {
        if (rowTemplate == null || rowContainer == null)
        {
            Debug.LogWarning("[LeaderboardUI] rowTemplate or rowContainer not assigned.");
            return;
        }

        ClearRows();

        foreach (var (uid, username, xp, photoUrl) in entries)
        {
            GameObject row = Instantiate(rowTemplate, rowContainer);
            row.SetActive(true);
            _spawnedRows.Add(row);

            var item = row.GetComponent<UserRowItem>();
            if (item != null)
                item.Populate(username, xp, photoUrl);
            else
                Debug.LogWarning("[LeaderboardUI] UserRow_Template clone has no UserRowItem — add the component.");

            var btn = row.GetComponent<Button>() ?? row.GetComponentInChildren<Button>(true);
            if (btn != null)
            {
                string capturedUid = uid;
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnRowTapped(capturedUid));
            }
        }
    }

    // ── Row tap ──────────────────────────────────────────────────────────────

    private void OnRowTapped(string uid)
    {
        Debug.Log($"[LeaderboardUI] Row tapped — uid={uid}");

        if (appUIManager == null)
            appUIManager = FindFirstObjectByType<AppUIManager>();

        if (appUIManager != null)
            appUIManager.ShowDetailedUser(uid);
        else
            Debug.LogWarning("[LeaderboardUI] AppUIManager not found.");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void ClearRows()
    {
        foreach (var go in _spawnedRows)
            if (go != null) DestroyImmediate(go);
        _spawnedRows.Clear();
    }

    private void SetStatus(string msg)
    {
        if (txtStatus != null) txtStatus.text = msg;
    }

    private static string ChildStr(DataSnapshot snap, string key, string fallback)
    {
        var c = snap.Child(key);
        return (c.Exists && c.Value != null) ? c.Value.ToString() : fallback;
    }

    private static int ChildInt(DataSnapshot snap, string key)
    {
        var c = snap.Child(key);
        if (!c.Exists || c.Value == null) return 0;
        int.TryParse(c.Value.ToString(), out int v);
        return v;
    }
}
