using Firebase.Auth;
using Firebase.Database;
using Firebase.Extensions;
using Firebase.Storage;
using System.IO;
#if UNITY_EDITOR
using UnityEditor;
#endif
#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EditProfileUI : MonoBehaviour
{
    public TMP_InputField inputFullName;
    public TMP_InputField inputUsername;
    public TMP_InputField inputBio;
    public TMP_Text txtStatus;
    public Button btnSaveProfile;
    public Button btnEditPhotoIcon;
    public Image imgProfilePhoto;

    private FirebaseUser _user;
    private DatabaseReference _db;

    // ---- Native iOS DllImport ----
#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")]
    private static extern void Who8_PickImageFromGallery(string goName, string methodName);
#endif

    // ---- Unity lifecycle ----

    private void OnEnable()
    {
        LoadProfile();

        if (btnSaveProfile != null)
        {
            btnSaveProfile.onClick.RemoveListener(OnClickSave);
            btnSaveProfile.onClick.AddListener(OnClickSave);
        }

        if (btnEditPhotoIcon != null)
        {
            btnEditPhotoIcon.onClick.RemoveListener(OnClickEditPhotoIcon);
            btnEditPhotoIcon.onClick.AddListener(OnClickEditPhotoIcon);
        }
    }

    private void OnDisable()
    {
        if (btnSaveProfile != null)
            btnSaveProfile.onClick.RemoveListener(OnClickSave);

        if (btnEditPhotoIcon != null)
            btnEditPhotoIcon.onClick.RemoveListener(OnClickEditPhotoIcon);
    }

    // ---- Profile load ----

    private void LoadProfile()
    {
        _user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (_user == null) return;

        _db = FirebaseInitializer.DB;

        _db.Child("users").Child(_user.UserId).GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsCompletedSuccessfully || task.Result == null || !task.Result.Exists)
                    return;

                var data = task.Result;

                if (inputFullName != null)
                    inputFullName.text = GetValue(data, "fullName", GetValue(data, "userName", ""));

                if (inputUsername != null)
                    inputUsername.text = GetValue(data, "userName", "");

                if (inputBio != null)
                    inputBio.text = GetValue(data, "bio", "");

                string photoUrl = GetValue(data, "photoUrl", "");
                if (!string.IsNullOrEmpty(photoUrl) && imgProfilePhoto != null)
                    ImageCache.Load(photoUrl, SetProfilePhoto);
            });
    }

    // ---- Save profile ----

    private void OnClickSave()
    {
        _user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (_user == null) return;

        string fullName = inputFullName != null ? inputFullName.text.Trim() : "";
        string username = inputUsername != null ? inputUsername.text.Trim() : "";
        string bio      = inputBio      != null ? inputBio.text.Trim()      : "";

        if (string.IsNullOrEmpty(username))
        {
            SetStatus("Username cannot be empty.");
            return;
        }

        var updates = new System.Collections.Generic.Dictionary<string, object>
        {
            { "fullName", fullName },
            { "userName", username },
            { "bio",      bio      }
        };

        FirebaseInitializer.DB
            .Child("users")
            .Child(_user.UserId)
            .UpdateChildrenAsync(updates)
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    SetStatus("Profile update failed.");
                    return;
                }

                SetStatus("Profile updated.");

                var appUI = FindFirstObjectByType<AppUIManager>();
                if (appUI != null)
                    appUI.GoBack();
            });
    }

    // ---- Photo pick entry point ----

    private void OnClickEditPhotoIcon()
    {
        _user = FirebaseAuth.DefaultInstance.CurrentUser;
        if (_user == null)
        {
            SetStatus("No user.");
            return;
        }

#if UNITY_EDITOR
        // In Editor: use the built-in file panel (synchronous).
        string path = EditorUtility.OpenFilePanel("Select profile photo", "", "png,jpg,jpeg");
        if (!string.IsNullOrEmpty(path))
            UploadProfilePhoto(path);
        else
            SetStatus("No image selected.");

#elif UNITY_IOS
        // On iOS: open the native photo gallery and wait for the async callback
        // (OnImagePickedFromGallery) delivered via UnitySendMessage.
        SetStatus("Opening gallery...");
        Who8_PickImageFromGallery(gameObject.name, nameof(OnImagePickedFromGallery));

#else
        SetStatus("Image picking is not supported on this platform.");
#endif
    }

    /// <summary>
    /// Called by the native iOS plugin via UnitySendMessage once the user picks
    /// (or cancels) the image.  The argument is the absolute path to a temporary
    /// JPEG file, or an empty string when the user cancelled.
    /// </summary>
    public void OnImagePickedFromGallery(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            SetStatus("No image selected.");
            return;
        }

        UploadProfilePhoto(path);
    }

    // ---- Upload ----

    private void UploadProfilePhoto(string imagePath)
    {
        if (!File.Exists(imagePath))
        {
            SetStatus("Selected image not found.");
            Debug.LogWarning("[EditProfileUI] File not found: " + imagePath);
            return;
        }

        string extension = Path.GetExtension(imagePath).ToLowerInvariant();
        if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
        {
            SetStatus("Please select a PNG or JPG image.");
            return;
        }

        SetStatus("Uploading photo...");

        // Read bytes here so the temp file can be released after upload starts.
        byte[] imageBytes;
        try
        {
            imageBytes = File.ReadAllBytes(imagePath);
        }
        catch (System.Exception e)
        {
            SetStatus("Could not read image file.");
            Debug.LogError("[EditProfileUI] File read error: " + e);
            return;
        }

        // Always store as JPEG in Firebase Storage.
        string storagePath = $"profilePhotos/{_user.UserId}.jpg";
        StorageReference photoRef = FirebaseStorage.DefaultInstance.GetReference(storagePath);

        var metadata = new MetadataChange { ContentType = "image/jpeg" };

        photoRef.PutBytesAsync(imageBytes, metadata)
            .ContinueWithOnMainThread(uploadTask =>
            {
                if (!uploadTask.IsCompletedSuccessfully)
                {
                    SetStatus("Photo upload failed.");
                    Debug.LogError("[EditProfileUI] Photo upload failed: " + uploadTask.Exception);
                    return;
                }

                photoRef.GetDownloadUrlAsync()
                    .ContinueWithOnMainThread(urlTask =>
                    {
                        if (!urlTask.IsCompletedSuccessfully)
                        {
                            SetStatus("Could not get photo URL.");
                            Debug.LogError("[EditProfileUI] GetDownloadUrl failed: " + urlTask.Exception);
                            return;
                        }

                        string photoUrl = urlTask.Result.ToString();

                        FirebaseInitializer.DB
                            .Child("users")
                            .Child(_user.UserId)
                            .Child("photoUrl")
                            .SetValueAsync(photoUrl)
                            .ContinueWithOnMainThread(dbTask =>
                            {
                                if (!dbTask.IsCompletedSuccessfully)
                                {
                                    SetStatus("Photo saved, profile update failed.");
                                    Debug.LogError("[EditProfileUI] Saving photoUrl failed: " + dbTask.Exception);
                                    return;
                                }

                                SetStatus("Profile photo updated.");
                                // Invalidate old cached version then load fresh.
                                ImageCache.Invalidate(photoUrl);
                                ImageCache.Load(photoUrl, SetProfilePhoto);
                            });
                    });
            });
    }

    // ---- Photo display ----

    private void SetProfilePhoto(Sprite sprite)
    {
        if (imgProfilePhoto == null || sprite == null) return;
        imgProfilePhoto.sprite = sprite;
        imgProfilePhoto.preserveAspect = true;
    }

    // ---- Helpers ----

    private string GetValue(DataSnapshot data, string key, string fallback)
    {
        return data.Child(key).Exists && data.Child(key).Value != null
            ? data.Child(key).Value.ToString()
            : fallback;
    }

    private void SetStatus(string msg)
    {
        if (txtStatus != null)
            txtStatus.text = msg;

        Debug.Log("[EditProfileUI] " + msg);
    }
}
