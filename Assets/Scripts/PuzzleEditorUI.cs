using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PuzzleEditorUI : MonoBehaviour
{
    [Header("Refs")]
    public AnchorsRealtime anchors;

    [Header("UI")]
    public TMP_Text selectedAnchorText;
    public TMP_InputField hintInput;
    public TMP_InputField passwordInput;
    public TMP_InputField solvedMessageInput;

    public Button saveButton;
    // UI root for puzzle fields (hint/password/solved/save)
    [Tooltip("Root GameObject containing puzzle editor fields (hint/password/solved/save).")]
    public GameObject editorFieldsRoot;

    void Awake()
    {
        if (saveButton != null)
            saveButton.onClick.AddListener(OnSave);
    }

    void OnEnable()
    {
        RefreshSelectedAnchor();
    }

    // OnSelectAnchor removed per AddPopupMessage style.

    public void RefreshSelectedAnchor()
    {
        if (anchors == null) return;
        string id = anchors.GetCurrentPuzzleAnchorId();
        if (selectedAnchorText != null)
            selectedAnchorText.text = string.IsNullOrEmpty(id)
                ? "Selected Anchor: (none)"
                : $"Selected Anchor: {id}";
        if (editorFieldsRoot != null)
            editorFieldsRoot.SetActive(!string.IsNullOrEmpty(id));
    }

    public void OnSave()
    {
        if (anchors == null) return;
        string id = anchors.GetCurrentPuzzleAnchorId();
        if (string.IsNullOrEmpty(id))
        {
            Debug.LogWarning("[PuzzleEditorUI] No anchor selected for puzzle, cannot save.");
            return;
        }
        string hint = hintInput != null ? hintInput.text : "";
        string pass = passwordInput != null ? passwordInput.text : "";
        string solvedMsg = solvedMessageInput != null ? solvedMessageInput.text : "";
        anchors.SetPuzzleForSelectedAnchor(hint, pass, solvedMsg);
        Debug.Log("[PuzzleEditorUI] Puzzle saved.");
    }
}