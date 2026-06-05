using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attached to UserRow_Template.
/// Populates username, XP and profile photo for a leaderboard row.
/// Inspector refs are optional — Awake auto-finds them by hierarchy name if null.
///
/// Expected hierarchy:
///   UserRow_Template
///     Img_ProfilePhoto   ← Image
///     CenterBlock
///       Txt_Username     ← TMP_Text
///     Txt_XP             ← TMP_Text
/// </summary>
public class UserRowItem : MonoBehaviour
{
    [Header("Row UI (auto-found if left empty)")]
    public Image    imgProfilePhoto;
    public TMP_Text txtUsername;
    public TMP_Text txtXP;

    // ── Auto-find ────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (imgProfilePhoto == null)
        {
            var t = transform.Find("Img_ProfilePhoto");
            if (t != null) imgProfilePhoto = t.GetComponent<Image>();
        }

        if (txtUsername == null)
        {
            var t = transform.Find("CenterBlock/Txt_Username");
            if (t != null) txtUsername = t.GetComponent<TMP_Text>();
        }

        if (txtXP == null)
        {
            var t = transform.Find("Txt_XP");
            if (t != null) txtXP = t.GetComponent<TMP_Text>();
        }
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public void Populate(string username, int xp, string photoUrl)
    {
        if (txtUsername != null) txtUsername.text = username;
        if (txtXP      != null) txtXP.text        = $"{xp} XP";

        if (!string.IsNullOrEmpty(photoUrl) && imgProfilePhoto != null)
            ImageCache.Load(photoUrl, sprite =>
            {
                if (imgProfilePhoto == null) return;
                imgProfilePhoto.sprite = sprite;
                imgProfilePhoto.preserveAspect = true;
            });
    }
}
