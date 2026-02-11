using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class CluePopupUI : MonoBehaviour
{
    [Header("UI")]
    public GameObject panelRoot;     // UserCluePopupPanel (aktif kalacak)
    public CanvasGroup canvasGroup;  // EKLEDİK
    public TMP_Text titleLabel;
    public TMP_Text messageLabel;
    public Button closeButton;

    void Awake()
    {
        // Eğer CanvasGroup yoksa otomatik ekleyelim
        if (canvasGroup == null && panelRoot != null)
        {
            canvasGroup = panelRoot.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = panelRoot.AddComponent<CanvasGroup>();
        }

        // Panel başta görünmesin ama aktif olsun
        Hide();

        if (closeButton != null)
            closeButton.onClick.AddListener(Hide);
    }

    public void Show(string message, string title = "Clue")
    {
        if (titleLabel != null)
            titleLabel.text = title;

        if (messageLabel != null)
            messageLabel.text = string.IsNullOrEmpty(message) ? "(No message)" : message;

        // Smooth aç (fade)
        canvasGroup.alpha = 1f;
        canvasGroup.interactable = true;
        canvasGroup.blocksRaycasts = true;
    }

    // Convenience alias
    public void ShowMessage(string message, string title = "Clue")
    {
        Show(message, title);
    }

    public void Hide()
    {
        // Smooth kapat (fade)
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;
    }

}