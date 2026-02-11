using UnityEngine;
using TMPro;

public class PopupMessageEditorUI : MonoBehaviour
{
    [Header("Refs")]
    public AnchorsRealtime anchors;

    [Header("UI")]
    [Tooltip("Seçili anchor ID'sini gösteren label.")]
    public TMP_Text anchorIdLabel;

    [Tooltip("Database'te kayıtlı mevcut popup mesajını gösteren label.")]
    public TMP_Text existingMessageLabel;

    [Tooltip("Yeni/düzenlenmiş popup mesajını yazdığımız input alanı.")]
    public TMP_InputField messageInput;

    void OnEnable()
    {
        RefreshSelectedAnchor();
    }

    // Admin tarafında bir anchor seçildiğinde veya panel açıldığında çağrılacak
    public void RefreshSelectedAnchor()
    {
        if (anchors == null)
        {
            if (anchorIdLabel != null)
                anchorIdLabel.text = "No AnchorsRealtime reference";

            if (existingMessageLabel != null)
                existingMessageLabel.text = string.Empty;

            if (messageInput != null)
                messageInput.text = string.Empty;

            return;
        }

        var id = anchors.GetCurrentPopupAnchorId();

        if (string.IsNullOrEmpty(id))
        {
            if (anchorIdLabel != null)
                anchorIdLabel.text = "No anchor selected";

            if (existingMessageLabel != null)
                existingMessageLabel.text = string.Empty;

            if (messageInput != null)
                messageInput.text = string.Empty;

            return;
        }

        // Anchor ID'yi göster
        if (anchorIdLabel != null)
            anchorIdLabel.text = $"Anchor ID: {id}";

        // Mevcut mesajı yüklerken geçici bir text göster
        if (existingMessageLabel != null)
            existingMessageLabel.text = "Loading current message...";

        // Input'u şimdilik temizle
        if (messageInput != null)
            messageInput.text = string.Empty;

        // AnchorsRealtime'dan popupMessage'ı yükle
        anchors.GetPopupMessageForAnchor(id, msg =>
        {
            // Callback main thread'de çalışıyor (ContinueWithOnMainThread sayesinde)
            if (existingMessageLabel != null)
            {
                if (string.IsNullOrEmpty(msg))
                    existingMessageLabel.text = "(No popup message yet)";
                else
                    existingMessageLabel.text = msg;
            }

            if (messageInput != null)
            {
                // Input'u mevcut mesajla doldur (veya boş bırak)
                messageInput.text = msg ?? string.Empty;
            }
        });
    }

    // "Save popup" butonuna bağla
    public void OnSaveClicked()
    {
        if (anchors == null || messageInput == null)
        {
            Debug.LogWarning("[PopupEditor] Missing anchors or messageInput.");
            return;
        }

        // Firebase'e yaz
        anchors.SetPopupMessage(messageInput.text);
        Debug.Log("[PopupEditor] Popup message saved.");

        // Paneli kapatmak istiyorsan şu satırı bırak, yoksa yorum satırı yapabilirsin
        gameObject.SetActive(false);
    }
}