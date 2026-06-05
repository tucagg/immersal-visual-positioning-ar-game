using System;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attached to Screen_CompletedMaps and Screen_SocialCompletedMaps.
/// Displays either completed or created maps for a given user (TargetUid).
/// Set TargetUid and Mode before navigating here.
/// </summary>
public class CompletedMapsUI : MonoBehaviour
{
    // ── Static context ───────────────────────────────────────────────────────

    public enum ListMode { Completed, Created }

    /// <summary>UID of the user whose maps to display. Set before navigating.</summary>
    public static string   TargetUid { get; set; } = "";

    /// <summary>Whether to show completed or created maps.</summary>
    public static ListMode Mode      { get; set; } = ListMode.Completed;

    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("List")]
    public Transform  contentRoot;
    public GameObject rowTemplate;

    [Header("Top Bar")]
    public TMP_Text txtTitle;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (rowTemplate != null) rowTemplate.SetActive(false);
        ClearList();

        if (Mode == ListMode.Created)
            LoadCreatedMaps(TargetUid);
        else
            LoadCompletedMaps(TargetUid);
    }

    private void OnDisable()
    {
        ClearList();
    }

    // ── Data loading ─────────────────────────────────────────────────────────

    // ── Created maps loading ──────────────────────────────────────────────────

    private void LoadCreatedMaps(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return;

        if (txtTitle != null)
            txtTitle.text = "Created Maps";

        FirebaseInitializer.DB.Child("maps")
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsCompletedSuccessfully || task.Result == null || !task.Result.Exists)
                    return;

                var items = new List<(string name, string category, long completedAt)>();

                foreach (var child in task.Result.Children)
                {
                    var creatorSnap = child.Child("creatorUid");
                    if (!creatorSnap.Exists || creatorSnap.Value?.ToString() != uid) continue;

                    string status = child.Child("approvalStatus").Value?.ToString() ?? "draft";
                    if (status != "approved") continue;

                    string name     = child.Child("name").Value?.ToString()     ?? child.Key;
                    string category = child.Child("category").Value?.ToString() ?? "-";

                    long createdAt = 0;
                    var tsSnap = child.Child("createdAt");
                    if (tsSnap.Exists && tsSnap.Value != null)
                        long.TryParse(tsSnap.Value.ToString(), out createdAt);

                    items.Add((name, category, createdAt));
                }

                BuildList(items);
            });
    }

    // ── Completed maps loading ────────────────────────────────────────────────

    private void LoadCompletedMaps(string uid)
    {
        if (string.IsNullOrEmpty(uid)) return;

        if (txtTitle != null)
            txtTitle.text = "Completed Maps";

        // Step 1: read users/{uid}/progress to find completed map keys + completedAt
        FirebaseInitializer.DB
            .Child("users").Child(uid).Child("progress")
            .GetValueAsync()
            .ContinueWithOnMainThread(progressTask =>
            {
                if (!progressTask.IsCompletedSuccessfully || progressTask.Result == null || !progressTask.Result.Exists)
                    return;

                var completedEntries = new List<(string mapKey, long completedAt)>();

                foreach (var child in progressTask.Result.Children)
                {
                    var flagSnap = child.Child("mapCompleted");
                    if (!flagSnap.Exists || flagSnap.Value == null) continue;

                    bool isCompleted = false;
                    bool.TryParse(flagSnap.Value.ToString(), out isCompleted);
                    if (!isCompleted && flagSnap.Value.ToString() != "True") continue;
                    if (!isCompleted) isCompleted = flagSnap.Value.ToString() == "True";
                    if (!isCompleted) continue;

                    long completedAt = 0;
                    var tsSnap = child.Child("completedAt");
                    if (tsSnap.Exists && tsSnap.Value != null)
                        long.TryParse(tsSnap.Value.ToString(), out completedAt);

                    completedEntries.Add((child.Key, completedAt));
                }

                if (completedEntries.Count == 0) return;

                // Step 2: fetch map metadata for each completed key
                FetchMapDetails(completedEntries);
            });
    }

    private void FetchMapDetails(List<(string mapKey, long completedAt)> entries)
    {
        int remaining = entries.Count;
        var results = new List<(string name, string category, long completedAt)>(entries.Count);

        // Pre-fill so we can fill by index
        for (int i = 0; i < entries.Count; i++)
            results.Add(("", "", entries[i].completedAt));

        for (int i = 0; i < entries.Count; i++)
        {
            int idx = i;
            string mapKey = entries[i].mapKey;

            FirebaseInitializer.DB
                .Child("maps").Child(mapKey)
                .GetValueAsync()
                .ContinueWithOnMainThread(mapTask =>
                {
                    string name     = mapKey;
                    string category = "-";

                    if (mapTask.IsCompletedSuccessfully && mapTask.Result != null && mapTask.Result.Exists)
                    {
                        var snap = mapTask.Result;
                        name     = ChildStr(snap, "name",     mapKey);
                        category = ChildStr(snap, "category", "-");
                    }
                    else
                    {
                        // Map deleted — mark as skip
                        name = null;
                    }

                    results[idx] = (name, category, results[idx].completedAt);

                    remaining--;
                    if (remaining == 0)
                        BuildList(results);
                });
        }
    }

    // ── List building ─────────────────────────────────────────────────────────

    private void BuildList(List<(string name, string category, long completedAt)> items)
    {
        ClearList();

        // Sort by completedAt descending (most recent first)
        items.Sort((a, b) => b.completedAt.CompareTo(a.completedAt));

        foreach (var item in items)
        {
            if (item.name == null) continue; // map deleted, skip

            var row = Instantiate(rowTemplate, contentRoot);
            row.SetActive(true);

            SetText(row, "Txt_MapName",     item.name);
            SetText(row, "Txt_MapType",     item.category);
            SetText(row, "Txt_CompletedAt", FormatTimestamp(item.completedAt));
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(contentRoot as RectTransform);
    }

    private void ClearList()
    {
        if (contentRoot == null) return;
        for (int i = contentRoot.childCount - 1; i >= 0; i--)
        {
            var child = contentRoot.GetChild(i).gameObject;
            if (child == rowTemplate) continue;
            Destroy(child);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void SetText(GameObject row, string childName, string value)
    {
        var t = row.transform.Find(childName);
        if (t == null) return;
        var txt = t.GetComponent<TMP_Text>();
        if (txt != null) txt.text = value;
    }

    private static string ChildStr(DataSnapshot snap, string key, string fallback)
    {
        var c = snap.Child(key);
        return (c.Exists && c.Value != null) ? c.Value.ToString() : fallback;
    }

    private static string FormatTimestamp(long ms)
    {
        if (ms <= 0) return "-";
        try
        {
            var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).ToLocalTime();
            return dt.ToString("dd MMM yyyy");
        }
        catch { return "-"; }
    }
}
