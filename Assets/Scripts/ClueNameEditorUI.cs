using UnityEngine;
using TMPro;

public class ClueNameEditorUI : MonoBehaviour
{
    [Header("Refs")]
    public AnchorsRealtime anchors;
    public TMP_Text selectedAnchorLabel;
    public TMP_Text currentNameLabel;
    public TMP_InputField nameInput;

    public void RefreshSelectedAnchor()
    {
        if (anchors == null)
            return;

        string id = anchors.GetCurrentClueNameAnchorId();

        if (selectedAnchorLabel != null)
        {
            selectedAnchorLabel.text = string.IsNullOrEmpty(id)
                ? "No anchor selected"
                : $"Anchor: {id}";
        }

        if (string.IsNullOrEmpty(id))
        {
            if (currentNameLabel != null) currentNameLabel.text = "";
            if (nameInput != null) nameInput.text = "";
            return;
        }

        string clueName = anchors.GetClueName(id);

        if (currentNameLabel != null)
        {
            currentNameLabel.text = string.IsNullOrEmpty(clueName)
                ? "(no name)"
                : clueName;
        }

        if (nameInput != null)
        {
            nameInput.text = clueName ?? "";
        }
    }

    // Save button için
    public void OnClickSaveName()
    {
        if (anchors == null || nameInput == null)
            return;

        string id = anchors.GetCurrentClueNameAnchorId();
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning("[ClueNameEditorUI] No anchor selected to save name.");
            return;
        }

        string newName = nameInput.text ?? "";
        anchors.SetClueName(id, newName);

        if (currentNameLabel != null)
        {
            currentNameLabel.text = string.IsNullOrEmpty(newName)
                ? "(no name)"
                : newName;
        }

        Debug.Log("[ClueNameEditorUI] Saved new name for anchor " + id);
    }
}