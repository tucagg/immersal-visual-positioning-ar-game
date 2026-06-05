using System;
using System.Collections.Generic;
using Firebase.Database;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AdminMapsStatusUI : MonoBehaviour
{
    public Transform  listContent;
    public GameObject adminMapRowTemplate;

    private Query                              _query;
    private EventHandler<ValueChangedEventArgs> _handler;

    public static string      SelectedMapDbKey { get; private set; }
    public static AdminMapData SelectedMap     { get; private set; }

    [System.Serializable]
    public class AdminMapData
    {
        public string dbKey;
        public string mapName;
        public string mapType;
        public string approvalStatus;
        public string creatorUid;
        public int    immersalId;
    }

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (adminMapRowTemplate != null)
            adminMapRowTemplate.SetActive(false);

        SubscribeRealtime();
    }

    private void OnDisable() => UnsubscribeRealtime();
    private void OnDestroy() => UnsubscribeRealtime();

    // ── Real-time subscription ───────────────────────────────────────────────

    public void ForceRefresh() => SubscribeRealtime();

    private void SubscribeRealtime()
    {
        UnsubscribeRealtime();

        _query = FirebaseInitializer.DB.Child("maps");

        _handler = (_, args) =>
        {
            if (args.DatabaseError != null)
            {
                Debug.LogWarning("[AdminMapsStatusUI] " + args.DatabaseError.Message);
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
        ClearList();

        if (adminMapRowTemplate != null)
            adminMapRowTemplate.SetActive(false);

        if (snapshot == null || !snapshot.Exists)
            return;

        foreach (DataSnapshot child in snapshot.Children)
        {
            string status = GetString(child, "approvalStatus", "");
            if (status != "pending") continue;

            var data = new AdminMapData
            {
                dbKey          = child.Key,
                mapName        = GetString(child, "mapName", GetString(child, "name", "Unnamed Map")),
                mapType        = GetString(child, "mapType", GetString(child, "category", "-")),
                approvalStatus = status,
                creatorUid     = GetString(child, "creatorUid", GetString(child, "createdByUid", "")),
                immersalId     = GetInt(child, "immersalId")
            };

            CreateRow(data);
        }

        // Force layout rebuild so ScrollRect / VerticalLayoutGroup reflects the new rows immediately.
        if (listContent is RectTransform rt)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
    }

    // ── Row creation ─────────────────────────────────────────────────────────

    private void CreateRow(AdminMapData data)
    {
        if (adminMapRowTemplate == null || listContent == null)
            return;

        GameObject row = Instantiate(adminMapRowTemplate, listContent);
        row.SetActive(true);

        SetText(row, "Txt_MapName",   data.mapName);
        SetText(row, "Txt_MapType",   data.mapType);
        SetText(row, "Txt_MapStatus", data.approvalStatus);

        Button btnEdit = row.transform.Find("Btn_Edit")?.GetComponent<Button>();
        if (btnEdit != null)
        {
            btnEdit.onClick.RemoveAllListeners();
            btnEdit.onClick.AddListener(() =>
            {
                SelectedMapDbKey = data.dbKey;
                SelectedMap      = data;

                var appUI = FindFirstObjectByType<AppUIManager>();
                if (appUI != null)
                    appUI.ShowAdminMapDetailsApproval();
            });
        }
    }

    // ── Clear ────────────────────────────────────────────────────────────────

    private void ClearList()
    {
        if (listContent == null) return;

        for (int i = listContent.childCount - 1; i >= 0; i--)
        {
            Transform child = listContent.GetChild(i);
            if (adminMapRowTemplate != null && child.gameObject == adminMapRowTemplate) continue;
            Destroy(child.gameObject);
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void SetText(GameObject row, string childName, string value)
    {
        TMP_Text txt = row.transform.Find(childName)?.GetComponent<TMP_Text>();
        if (txt != null) txt.text = value;
    }

    private static string GetString(DataSnapshot data, string key, string fallback)
        => data.Child(key).Exists && data.Child(key).Value != null
            ? data.Child(key).Value.ToString()
            : fallback;

    private static int GetInt(DataSnapshot data, string key)
    {
        if (!data.Child(key).Exists || data.Child(key).Value == null) return 0;
        int.TryParse(data.Child(key).Value.ToString(), out int value);
        return value;
    }
}
