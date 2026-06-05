using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class EditClueUI : MonoBehaviour
{
    [Header("Refs")]
    public AnchorsRealtime anchors;

    [Header("Inputs")]
    public TMP_InputField inputClueName;
    public TMP_InputField inputClueIndex;

    public TMP_Dropdown dropdownPrefab;
    public TMP_Dropdown dropdownClueType;

    [Header("Popup")]
    public GameObject popupFieldsRoot;
    public TMP_InputField inputPopupMessage;

    [Header("Puzzle")]
    public GameObject puzzleFieldsRoot;
    public TMP_InputField inputPuzzleHint;
    public TMP_InputField inputPuzzlePassword;
    public TMP_InputField inputPuzzleSolvedMessage;

    private string _lastClueType = "clue";


    private void OnEnable()
    {
        SetupDropdowns();
        LoadData();
        _lastClueType = GetClueTypeString();
        UpdateFieldVisibility();
    }

    private void SetupDropdowns()
    {
        // Prefab dropdown
        if (dropdownPrefab != null)
        {
            dropdownPrefab.ClearOptions();
            List<string> options = new List<string>();

            foreach (var opt in anchors.prefabOptions)
            {
                options.Add(string.IsNullOrEmpty(opt.displayName) ? opt.key : opt.displayName);
            }

            dropdownPrefab.AddOptions(options);
        }

        // ClueType dropdown
        if (dropdownClueType != null)
        {
            dropdownClueType.ClearOptions();
            dropdownClueType.AddOptions(new List<string> { "clue", "popup", "puzzle" });

            dropdownClueType.onValueChanged.RemoveListener(OnClueTypeChanged);
            dropdownClueType.onValueChanged.AddListener(OnClueTypeChanged);
        }
    }

    private void LoadData()
    {
        if (anchors == null) return;

        if (!anchors.TryGetSelectedClueEditData(out var data))
        {
            Debug.LogWarning("[EditClueUI] No selected anchor.");
            return;
        }

        inputClueName.text = data.clueName;
        inputClueIndex.text = data.clueIndex.ToString();

        // Prefab seç
        int prefabIndex = 0;
        for (int i = 0; i < anchors.prefabOptions.Count; i++)
        {
            if (anchors.prefabOptions[i].key == data.prefabKey)
            {
                prefabIndex = i;
                break;
            }
        }
        dropdownPrefab.value = prefabIndex;

        // Clue type
        dropdownClueType.value = GetClueTypeIndex(data.clueType);

        // Popup
        inputPopupMessage.text = data.popupMessage;

        // Puzzle
        inputPuzzleHint.text = data.puzzleHint;
        inputPuzzlePassword.text = data.puzzlePassword;
        inputPuzzleSolvedMessage.text = data.puzzleSolvedMessage;

        UpdateFieldVisibility();
    }

    private int GetClueTypeIndex(string type)
    {
        switch (type)
        {
            case "popup": return 1;
            case "puzzle": return 2;
            default: return 0;
        }
    }

    private string GetClueTypeString()
    {
        switch (dropdownClueType.value)
        {
            case 1: return "popup";
            case 2: return "puzzle";
            default: return "clue";
        }
    }

    private void OnClueTypeChanged(int _)
    {
        string newType = GetClueTypeString();
        AdaptFieldsForTypeChange(_lastClueType, newType);
        UpdateFieldVisibility();
        _lastClueType = newType;
    }

    private void UpdateFieldVisibility()
    {
        string type = GetClueTypeString();

        if (popupFieldsRoot != null)
            popupFieldsRoot.SetActive(type == "popup");

        if (puzzleFieldsRoot != null)
            puzzleFieldsRoot.SetActive(type == "puzzle");
    }

    private void AdaptFieldsForTypeChange(string oldType, string newType)
    {
        if (oldType == newType)
            return;

        if (oldType == "puzzle" && newType == "popup")
        {
            string question = inputPuzzleHint != null ? inputPuzzleHint.text : "";
            string solvedMessage = inputPuzzleSolvedMessage != null ? inputPuzzleSolvedMessage.text : "";

            if (inputPopupMessage != null)
                inputPopupMessage.text = !string.IsNullOrWhiteSpace(question) ? question : solvedMessage;
        }
        else if (oldType == "popup" && newType == "puzzle")
        {
            string message = inputPopupMessage != null ? inputPopupMessage.text : "";

            if (inputPuzzleHint != null)
                inputPuzzleHint.text = message;

            if (inputPuzzleSolvedMessage != null && string.IsNullOrWhiteSpace(inputPuzzleSolvedMessage.text))
                inputPuzzleSolvedMessage.text = message;
        }
    }

    // 🔥 SAVE
    public void OnClickSave()
    {
        if (anchors == null) return;

        if (!anchors.TryGetSelectedClueEditData(out var data))
        {
            Debug.LogWarning("[EditClueUI] No anchor selected.");
            return;
        }

        data.clueName = inputClueName.text;

        if (int.TryParse(inputClueIndex.text, out int index))
            data.clueIndex = index;

        data.prefabKey = anchors.prefabOptions[dropdownPrefab.value].key;
        data.clueType = GetClueTypeString();

        if (data.clueType == "popup")
        {
            data.popupMessage = inputPopupMessage != null ? inputPopupMessage.text : "";
            data.puzzleHint = "";
            data.puzzlePassword = "";
            data.puzzleSolvedMessage = "";
        }
        else if (data.clueType == "puzzle")
        {
            data.popupMessage = "";
            data.puzzleHint = inputPuzzleHint != null ? inputPuzzleHint.text : "";
            data.puzzlePassword = inputPuzzlePassword != null ? inputPuzzlePassword.text : "";
            data.puzzleSolvedMessage = inputPuzzleSolvedMessage != null ? inputPuzzleSolvedMessage.text : "";
        }
        else
        {
            data.popupMessage = "";
            data.puzzleHint = "";
            data.puzzlePassword = "";
            data.puzzleSolvedMessage = "";
        }

        anchors.SaveClueEditData(data);

        // Clue değişikliği map içeriğini değiştirir → yeniden onay gerekir.
        anchors.ResetMapApprovalToPending();

        Debug.Log("[EditClueUI] Saved");

        // 🔄 Refresh EditClues list if available
        var listUI = FindFirstObjectByType<EditCluesUI>();
        if (listUI != null)
        {
            listUI.RefreshList();
        }

        // ✅ Return to EditClues list after saving
        var ui = FindFirstObjectByType<AppUIManager>();
        if (ui != null)
        {
            ui.GoBack();
        }
    }

    // ❌ DELETE
    public void OnClickDelete()
    {
        if (anchors == null) return;

        anchors.DeleteSelectedAnchorForEdit();

        // Clue silinmesi map içeriğini değiştirir → yeniden onay gerekir.
        anchors.ResetMapApprovalToPending();

        Debug.Log("[EditClueUI] Deleted");

        // 🔄 Refresh list after delete
        var listUI = FindFirstObjectByType<EditCluesUI>();
        if (listUI != null)
        {
            listUI.RefreshList();
        }

        // ✅ Return to EditClues list after deleting
        var ui = FindFirstObjectByType<AppUIManager>();
        if (ui != null)
        {
            ui.GoBack();
        }
    }
}