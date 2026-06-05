using System;
using System.Collections.Generic;
using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attached to Screen_MapRejectFeedback.
///
/// Admin writes a rejection reason before confirming reject.
/// Feedback is required — Btn_ConfirmReject is disabled until text is entered.
///
/// Firebase writes on confirm:
///   maps/{mapDbKey}/approvalStatus        = "rejected"
///   maps/{mapDbKey}/rejectedAt            = server timestamp
///   maps/{mapDbKey}/rejectedByUid         = admin uid
///   maps/{mapDbKey}/rejectionFeedback     = feedback text
///
/// When the owner later resubmits and an admin approves:
///   AdminMapDetailsApprovalUI.OnClickApproveMap clears rejectionFeedback (handled there).
///
/// Hierarchy:
///   Txt_Info              → brief instruction label (optional)
///   Input_RejectFeedback  → TMP_InputField
///   Txt_Status            → error / loading feedback
///   Btn_ConfirmReject     → Button
/// </summary>
public class MapRejectFeedbackUI : MonoBehaviour
{
    [Header("UI")]
    public TMP_Text       txtInfo;
    public TMP_InputField inputFeedback;
    public TMP_Text       txtStatus;
    public Button         btnConfirmReject;

    private DatabaseReference _db;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        _db = FirebaseInitializer.DB;

        // Reset state
        if (inputFeedback != null) inputFeedback.text = "";
        SetStatus("");
        RefreshConfirmButton();

        // Show map name in info label
        string mapName = AdminMapsStatusUI.SelectedMap?.mapName ?? "";
        if (txtInfo != null)
            txtInfo.text = string.IsNullOrEmpty(mapName)
                ? "Please provide rejection feedback."
                : $"Rejection reason for \"{mapName}\":";

        // Wire input → live validation
        if (inputFeedback != null)
        {
            inputFeedback.onValueChanged.RemoveListener(OnFeedbackChanged);
            inputFeedback.onValueChanged.AddListener(OnFeedbackChanged);
        }

        // Wire confirm button
        if (btnConfirmReject != null)
        {
            btnConfirmReject.onClick.RemoveListener(OnClickConfirmReject);
            btnConfirmReject.onClick.AddListener(OnClickConfirmReject);
        }
    }

    private void OnDisable()
    {
        if (inputFeedback    != null) inputFeedback.onValueChanged.RemoveListener(OnFeedbackChanged);
        if (btnConfirmReject != null) btnConfirmReject.onClick.RemoveListener(OnClickConfirmReject);
    }

    // ── Live validation ──────────────────────────────────────────────────────

    private void OnFeedbackChanged(string _) => RefreshConfirmButton();

    private void RefreshConfirmButton()
    {
        if (btnConfirmReject == null) return;
        bool hasText = inputFeedback != null && inputFeedback.text.Trim().Length > 0;
        btnConfirmReject.interactable = hasText;
    }

    // ── Confirm reject ───────────────────────────────────────────────────────

    private void OnClickConfirmReject()
    {
        string feedback = inputFeedback != null ? inputFeedback.text.Trim() : "";
        if (string.IsNullOrEmpty(feedback))
        {
            SetStatus("Please enter rejection feedback.");
            return;
        }

        string mapKey = AdminMapsStatusUI.SelectedMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            SetStatus("Map not found.");
            return;
        }

        string adminUid = FirebaseAuth.DefaultInstance.CurrentUser?.UserId ?? "";

        SetStatus("Submitting…");
        if (btnConfirmReject != null) btnConfirmReject.interactable = false;

        var updates = new Dictionary<string, object>
        {
            { "approvalStatus",    "rejected"                                      },
            { "rejectedAt",        DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()  },
            { "rejectedByUid",     adminUid                                        },
            { "rejectionFeedback", feedback                                        }
        };

        _db.Child("maps").Child(mapKey).UpdateChildrenAsync(updates)
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    SetStatus("Reject failed. Try again.");
                    if (btnConfirmReject != null) btnConfirmReject.interactable = true;
                    Debug.LogError("[MapRejectFeedbackUI] Reject write failed: " + task.Exception);
                    return;
                }

                Debug.Log($"[MapRejectFeedbackUI] Map rejected. key={mapKey}, feedback={feedback}");

                // Return to maps status list (clear back stack so admin doesn't
                // go back into the approval screen for a now-rejected map).
                var appUI = FindFirstObjectByType<AppUIManager>();
                if (appUI != null)
                    appUI.ShowAdmin();
                else
                    gameObject.SetActive(false);
            });
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void SetStatus(string msg)
    {
        if (txtStatus != null) txtStatus.text = msg;
    }
}
