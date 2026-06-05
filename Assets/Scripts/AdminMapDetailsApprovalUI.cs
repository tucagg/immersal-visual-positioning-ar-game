using System;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AdminMapDetailsApprovalUI : MonoBehaviour
{
    public Transform clueListContent;
    public GameObject clueRowTemplate;

    public Button btnApproveMap;
    public Button btnRejectMap;

    private DatabaseReference _db;
    private int _loadVersion = 0;

    public static string SelectedClueKey { get; private set; }
    public static AdminClueData SelectedClue { get; private set; }

    [Serializable]
    public class AdminClueData
    {
        public string clueKey;
        public string clueName;
        public string clueType;
        public int clueIndex;
    }

    private void OnEnable()
    {
        _db = FirebaseInitializer.DB;

        if (clueRowTemplate != null)
            clueRowTemplate.SetActive(false);

        WireButtons();
        LoadClues();
    }

    private void WireButtons()
    {
        if (btnApproveMap != null)
        {
            btnApproveMap.onClick.RemoveListener(OnClickApproveMap);
            btnApproveMap.onClick.AddListener(OnClickApproveMap);
        }

        if (btnRejectMap != null)
        {
            btnRejectMap.onClick.RemoveListener(OnClickRejectMap);
            btnRejectMap.onClick.AddListener(OnClickRejectMap);
        }
    }

    private void LoadClues()
    {
        ClearList();

        string mapKey = AdminMapsStatusUI.SelectedMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            Debug.LogWarning("[AdminMapDetailsApprovalUI] No selected map.");
            return;
        }

        int requestVersion = ++_loadVersion;

        // Anchors are stored at anchors/{mapKey}/{anchorId}, NOT maps/{mapKey}/anchors
        _db.Child("anchors").Child(mapKey)
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (requestVersion != _loadVersion)
                    return;

                ClearList();

                if (clueRowTemplate != null)
                    clueRowTemplate.SetActive(false);

                if (!task.IsCompletedSuccessfully || task.Result == null || !task.Result.Exists)
                {
                    Debug.LogWarning("[AdminMapDetailsApprovalUI] No clues found.");
                    return;
                }

                foreach (DataSnapshot child in task.Result.Children)
                {
                    var data = new AdminClueData
                    {
                        clueKey = child.Key,
                        clueName = GetString(child, "clueName", "Unnamed Clue"),
                        clueType = GetString(child, "clueType", "clue"),
                        clueIndex = GetInt(child, "clueIndex")
                    };

                    CreateClueRow(data);
                }
            });
    }

    private void CreateClueRow(AdminClueData data)
    {
        if (clueRowTemplate == null || clueListContent == null)
            return;

        GameObject row = Instantiate(clueRowTemplate, clueListContent);
        row.SetActive(true);

        SetText(row, "Txt_ClueIndex", data.clueIndex.ToString());
        SetText(row, "Txt_ClueName", data.clueName);
        SetText(row, "Txt_ClueType", data.clueType);

        Button btnEdit = FindChildRecursive(row.transform, "Btn_Edit")?.GetComponent<Button>();
        if (btnEdit != null)
        {
            btnEdit.onClick.RemoveAllListeners();
            btnEdit.onClick.AddListener(() =>
            {
                SelectedClueKey = data.clueKey;
                SelectedClue = data;

                var appUI = FindFirstObjectByType<AppUIManager>();
                if (appUI != null)
                    appUI.ShowAdminClueDetailsApproval();
            });
        }
    }

    private void OnClickApproveMap()
    {
        string mapKey = AdminMapsStatusUI.SelectedMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
            return;

        string adminUid = FirebaseAuth.DefaultInstance.CurrentUser?.UserId ?? "";

        Debug.Log($"[SC1][APPROVAL] Map approval triggered — mapKey={mapKey} | adminUid={adminUid} | time={DateTime.UtcNow:HH:mm:ss.fff} UTC");

        var updates = new System.Collections.Generic.Dictionary<string, object>
        {
            { "approvalStatus",    "approved"                                      },
            { "approvedAt",        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()  },
            { "approvedByUid",     adminUid                                        },
            // Clear any previous rejection data so the owner no longer sees stale feedback.
            { "rejectionFeedback", null },
            { "rejectedAt",        null },
            { "rejectedByUid",     null }
        };

        _db.Child("maps").Child(mapKey).UpdateChildrenAsync(updates)
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    Debug.LogError("[AdminMapDetailsApprovalUI] Approve failed: " + task.Exception);
                    return;
                }

                IncrementCreatorCreatedMaps();
                AddXpForUser(AdminMapsStatusUI.SelectedMap?.creatorUid, 200);

                var appUI = FindFirstObjectByType<AppUIManager>();
                if (appUI != null)
                    appUI.GoBack();
            });
    }

    private void OnClickRejectMap()
    {
        var appUI = FindFirstObjectByType<AppUIManager>();
        if (appUI != null)
            appUI.ShowAdminMapRejectFeedback();
    }

    private void AddXpForUser(string uid, int amount)
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

            userRef.UpdateChildrenAsync(new System.Collections.Generic.Dictionary<string, object>
            {
                { "xp",    newXp    },
                { "level", newLevel }
            });

            int expectedLevel = Mathf.FloorToInt(Mathf.Sqrt(newXp / 100f)) + 1;
            Debug.Log(
                $"[SC1][XP] MAP_APPROVAL" +
                $" | uid={uid}" +
                $" | before={current} XP" +
                $" | awarded=+{amount} XP" +
                $" | after={newXp} XP" +
                $" | level={Mathf.FloorToInt(Mathf.Sqrt(current / 100f)) + 1}→{newLevel}" +
                $" | formula=floor(sqrt({newXp}/100))+1={expectedLevel}" +
                $" | calc_match={(newLevel == expectedLevel ? "✓ PASS" : "✗ FAIL")}" +
                $" | time={DateTime.UtcNow:HH:mm:ss.fff} UTC"
            );
        });
    }

    private void IncrementCreatorCreatedMaps()
    {
        string creatorUid = AdminMapsStatusUI.SelectedMap?.creatorUid;
        if (string.IsNullOrEmpty(creatorUid))
            return;

        _db.Child("users").Child(creatorUid).Child("createdMaps")
            .RunTransaction(mutableData =>
            {
                int current = 0;

                if (mutableData.Value != null)
                    int.TryParse(mutableData.Value.ToString(), out current);

                mutableData.Value = current + 1;
                return TransactionResult.Success(mutableData);
            });
    }

    private void ClearList()
    {
        if (clueListContent == null)
            return;

        for (int i = clueListContent.childCount - 1; i >= 0; i--)
        {
            Transform child = clueListContent.GetChild(i);

            if (clueRowTemplate != null && child.gameObject == clueRowTemplate)
                continue;

            Destroy(child.gameObject);
        }
    }

    private void SetText(GameObject row, string childName, string value)
    {
        TMP_Text txt = FindChildRecursive(row.transform, childName)?.GetComponent<TMP_Text>();
        if (txt != null)
            txt.text = value;
    }

    private Transform FindChildRecursive(Transform parent, string childName)
    {
        if (parent == null)
            return null;

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform child = parent.GetChild(i);

            if (child.name == childName)
                return child;

            Transform found = FindChildRecursive(child, childName);
            if (found != null)
                return found;
        }

        return null;
    }

    private string GetString(DataSnapshot data, string key, string fallback)
    {
        return data.Child(key).Exists && data.Child(key).Value != null
            ? data.Child(key).Value.ToString()
            : fallback;
    }

    private int GetInt(DataSnapshot data, string key)
    {
        if (!data.Child(key).Exists || data.Child(key).Value == null)
            return 0;

        int.TryParse(data.Child(key).Value.ToString(), out int value);
        return value;
    }
}