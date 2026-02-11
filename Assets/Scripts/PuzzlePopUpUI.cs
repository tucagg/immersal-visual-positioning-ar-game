using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PuzzlePopUpUI : MonoBehaviour
{
    [Header("UI")]
    public GameObject panelRoot;     // PuzzlePopupPanel
    public CanvasGroup canvasGroup;
    public TMP_Text titleLabel;
    public TMP_Text hintLabel;
    public Button closeButton;

    [Header("Puzzle Input")]
    public TMP_InputField inputField;
    public Button submitButton;

    [Header("Optional Feedback")]
    public TMP_Text feedbackLabel;

    private Action<string> _onSubmit;

    void Awake()
    {
        // Use panelRoot if assigned, otherwise this GameObject
        var root = panelRoot != null ? panelRoot : gameObject;

        // Auto-add CanvasGroup if missing
        if (canvasGroup == null)
        {
            canvasGroup = root.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = root.AddComponent<CanvasGroup>();
        }

        HideInstant();

        if (submitButton != null)
            submitButton.onClick.AddListener(Submit);

        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);

        if (inputField != null)
            inputField.onSubmit.AddListener(_ => Submit());
    }

    // AnchorsRealtime burayı çağırıyor
    public void Show(string hint, string title, Action<string> onSubmit)
    {
        _onSubmit = onSubmit;

        if (titleLabel != null)
            titleLabel.text = string.IsNullOrEmpty(title) ? "Puzzle" : title;

        if (hintLabel != null)
            hintLabel.text = string.IsNullOrEmpty(hint) ? "(No hint)" : hint;

        if (feedbackLabel != null)
            feedbackLabel.text = "";

        if (inputField != null)
        {
            inputField.text = "";
            inputField.ActivateInputField();
        }

        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    public void Hide()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
        _onSubmit = null;
    }

    private void HideInstant()
    {
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

    private void Submit()
    {
        string val = inputField != null ? inputField.text : "";
        _onSubmit?.Invoke(val);
    }

    public void SetFeedback(string msg)
    {
        if (feedbackLabel != null)
            feedbackLabel.text = msg ?? "";
    }
}