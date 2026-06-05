using System;
using System.Collections.Generic;
using Firebase.Database;
using UnityEngine;

public class CreateMapsListUI : MonoBehaviour
{
    [Header("Refs")]
    public AppUIManager ui;

    [Header("List")]
    public Transform  contentRoot;
    public GameObject mapRowTemplate;

    // ── Real-time listener state ─────────────────────────────────────────────
    private Query                              _query;
    private EventHandler<ValueChangedEventArgs> _handler;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        if (mapRowTemplate != null)
            mapRowTemplate.SetActive(false);
    }

    private void OnEnable()  => SubscribeRealtime();
    private void OnDisable() => UnsubscribeRealtime();
    private void OnDestroy() => UnsubscribeRealtime();

    // ── Real-time subscription ───────────────────────────────────────────────

    private void SubscribeRealtime()
    {
        UnsubscribeRealtime();

        if (!FirebaseInitializer.Ready || FirebaseInitializer.DB == null)
        {
            Debug.LogWarning("[CreateMapsListUI] Firebase not ready.");
            return;
        }

        _query = FirebaseInitializer.DB.Child("maps");

        _handler = (_, args) =>
        {
            if (args.DatabaseError != null)
            {
                Debug.LogWarning("[CreateMapsListUI] " + args.DatabaseError.Message);
                return;
            }

            RebuildList(args.Snapshot);
        };

        _query.ValueChanged += _handler;
    }

    private void UnsubscribeRealtime()
    {
        if (_query != null && _handler != null)
            _query.ValueChanged -= _handler;

        _query   = null;
        _handler = null;
    }

    // ── List rebuild ─────────────────────────────────────────────────────────

    private void RebuildList(DataSnapshot snapshot)
    {
        if (contentRoot == null || mapRowTemplate == null) return;

        mapRowTemplate.SetActive(false);
        ClearList();

        string uid = CurrentUid;
        if (string.IsNullOrEmpty(uid)) return;

        if (snapshot == null || !snapshot.Exists) return;

        var rows = new List<(string id, string name, string category, string status, long createdAt)>();

        foreach (var child in snapshot.Children)
        {
            string creatorUid = child.Child("creatorUid").Value?.ToString() ?? "";
            if (creatorUid != uid) continue;

            string mapId    = child.Key;
            string name     = child.Child("name").Value?.ToString() ?? mapId;
            string category = child.Child("category").Value?.ToString() ?? "-";
            string status   = child.Child("approvalStatus").Value?.ToString() ?? "draft";

            long createdAt = 0;
            long.TryParse(child.Child("createdAt").Value?.ToString(), out createdAt);

            rows.Add((mapId, name, category, status, createdAt));
        }

        rows.Sort((a, b) => b.createdAt.CompareTo(a.createdAt));

        foreach (var (mapId, name, category, status, _) in rows)
            CreateRow(mapId, name, category, status);
    }

    // ── Row creation ─────────────────────────────────────────────────────────

    private void CreateRow(string mapRecordId, string name, string category, string status)
    {
        var row = Instantiate(mapRowTemplate, contentRoot);
        row.SetActive(true);

        var rowUI = row.GetComponent<CreateMapRowUI>();
        if (rowUI != null)
            rowUI.Setup(mapRecordId, name, category, status, ui);
    }

    // ── Manual refresh (no-op: real-time listener handles updates automatically) ──

    /// <summary>
    /// Legacy call-site compatibility. The ValueChanged listener already
    /// refreshes the list whenever Firebase data changes, so this is a no-op.
    /// </summary>
    public void RefreshList() { }

    // ── Clear ────────────────────────────────────────────────────────────────

    private void ClearList()
    {
        for (int i = contentRoot.childCount - 1; i >= 0; i--)
        {
            var child = contentRoot.GetChild(i).gameObject;
            if (child == mapRowTemplate) continue;
            Destroy(child);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private string CurrentUid
    {
        get
        {
            if (AuthManager.Instance != null && AuthManager.Instance.CurrentUser != null)
                return AuthManager.Instance.CurrentUser.UserId;
            try
            {
                var user = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser;
                return user != null ? user.UserId : "";
            }
            catch { return ""; }
        }
    }
}
