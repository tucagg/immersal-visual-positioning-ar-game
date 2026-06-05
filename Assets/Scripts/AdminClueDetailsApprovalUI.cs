using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;

public class AdminClueDetailsApprovalUI : MonoBehaviour
{
    public TMP_Text infoClueName;
    public TMP_Text infoClueIndex;
    public TMP_Text infoPrefabType;
    public TMP_Text infoClueType;

    public GameObject popupFieldsRoot;
    public TMP_Text txtPopupMessage;

    public GameObject puzzleFieldsRoot;
    public TMP_Text txtClueQuestion;
    public TMP_Text txtClueAnswer;
    public TMP_Text txtClueSolvedMessage;

    private DatabaseReference _db;

    private void OnEnable()
    {
        _db = FirebaseInitializer.DB;
        LoadSelectedClue();
    }

    private void LoadSelectedClue()
    {
        ClearUI();

        string mapKey = AdminMapsStatusUI.SelectedMapDbKey;
        string clueKey = AdminMapDetailsApprovalUI.SelectedClueKey;

        if (string.IsNullOrEmpty(mapKey) || string.IsNullOrEmpty(clueKey))
            return;

        // Anchors are stored at anchors/{mapKey}/{clueKey}, NOT maps/{mapKey}/anchors/{clueKey}
        _db.Child("anchors").Child(mapKey).Child(clueKey)
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsCompletedSuccessfully || task.Result == null || !task.Result.Exists)
                {
                    Debug.LogWarning($"[AdminClueDetailsApprovalUI] Clue not found at anchors/{mapKey}/{clueKey}");
                    return;
                }

                DataSnapshot data = task.Result;

                // Firebase stores clueType as "message" (not "popup"), "puzzle", or "default"
                string clueType = GetString(data, "clueType", "default").ToLowerInvariant();

                SetText(infoClueName, GetString(data, "clueName", "Unnamed Clue"));
                SetText(infoClueIndex, GetInt(data, "clueIndex").ToString());
                SetText(infoPrefabType, GetString(data, "prefabKey", "-"));
                SetText(infoClueType, clueType);

                // "message" is the DB value for what the UI calls "popup"
                bool isPopup = clueType == "message";
                bool isPuzzle = clueType == "puzzle";

                if (popupFieldsRoot != null)
                    popupFieldsRoot.SetActive(isPopup);

                if (puzzleFieldsRoot != null)
                    puzzleFieldsRoot.SetActive(isPuzzle);

                if (isPopup)
                {
                    SetText(txtPopupMessage, GetString(data, "popupMessage", ""));
                }
                else if (isPuzzle)
                {
                    // Puzzle fields are stored nested under a "puzzle" child node
                    DataSnapshot puzzleNode = data.Child("puzzle");
                    SetText(txtClueQuestion, GetString(puzzleNode, "hint", ""));
                    SetText(txtClueAnswer, GetString(puzzleNode, "password", ""));
                    SetText(txtClueSolvedMessage, GetString(puzzleNode, "solvedMessage", ""));
                }
            });
    }

    private void ClearUI()
    {
        SetText(infoClueName, "");
        SetText(infoClueIndex, "");
        SetText(infoPrefabType, "");
        SetText(infoClueType, "");
        SetText(txtPopupMessage, "");
        SetText(txtClueQuestion, "");
        SetText(txtClueAnswer, "");
        SetText(txtClueSolvedMessage, "");

        if (popupFieldsRoot != null)
            popupFieldsRoot.SetActive(false);

        if (puzzleFieldsRoot != null)
            puzzleFieldsRoot.SetActive(false);
    }

    private void SetText(TMP_Text target, string value)
    {
        if (target != null)
            target.text = value ?? "";
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