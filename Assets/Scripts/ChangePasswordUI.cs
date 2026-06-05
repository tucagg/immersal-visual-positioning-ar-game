using Firebase.Auth;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ChangePasswordUI : MonoBehaviour
{
    public TMP_InputField inputCurrentPassword;
    public TMP_InputField inputNewPassword;
    public TMP_InputField inputConfirmPassword;
    public TMP_Text txtStatus;
    public Button btnSavePassword;

    private void OnEnable()
    {
        Clear();

        if (btnSavePassword != null)
        {
            btnSavePassword.onClick.RemoveListener(OnClickSavePassword);
            btnSavePassword.onClick.AddListener(OnClickSavePassword);
        }
    }

    private void OnDisable()
    {
        if (btnSavePassword != null)
            btnSavePassword.onClick.RemoveListener(OnClickSavePassword);
    }

    private void OnClickSavePassword()
    {
        var user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (user == null)
        {
            SetStatus("No user.");
            return;
        }

        string currentPassword = inputCurrentPassword != null ? inputCurrentPassword.text : "";
        string newPassword = inputNewPassword != null ? inputNewPassword.text : "";
        string confirmPassword = inputConfirmPassword != null ? inputConfirmPassword.text : "";

        if (string.IsNullOrEmpty(currentPassword) ||
            string.IsNullOrEmpty(newPassword) ||
            string.IsNullOrEmpty(confirmPassword))
        {
            SetStatus("Please fill all fields.");
            return;
        }

        if (newPassword.Length < 6)
        {
            SetStatus("New password must be at least 6 characters.");
            return;
        }

        if (newPassword != confirmPassword)
        {
            SetStatus("New passwords do not match.");
            return;
        }

        SetStatus("Checking current password...");

        string email = user.Email;
        var credential = EmailAuthProvider.GetCredential(email, currentPassword);

        user.ReauthenticateAsync(credential).ContinueWithOnMainThread(authTask =>
        {
            if (!authTask.IsCompletedSuccessfully)
            {
                SetStatus("Current password is incorrect.");
                return;
            }

            SetStatus("Updating password...");

            user.UpdatePasswordAsync(newPassword).ContinueWithOnMainThread(updateTask =>
            {
                if (!updateTask.IsCompletedSuccessfully)
                {
                    SetStatus("Password update failed.");
                    return;
                }

                SetStatus("Password updated.");

                var appUI = FindFirstObjectByType<AppUIManager>();
                if (appUI != null)
                    appUI.GoBack();
            });
        });
    }

    private void Clear()
    {
        if (inputCurrentPassword != null) inputCurrentPassword.text = "";
        if (inputNewPassword != null) inputNewPassword.text = "";
        if (inputConfirmPassword != null) inputConfirmPassword.text = "";
        SetStatus("");
    }

    private void SetStatus(string msg)
    {
        if (txtStatus != null)
            txtStatus.text = msg;

        Debug.Log("[ChangePasswordUI] " + msg);
    }
}