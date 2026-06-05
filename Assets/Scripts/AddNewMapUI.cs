using System;
using System.Collections.Generic;
using Firebase.Extensions;
using Firebase.Database;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class AddNewMapUI : MonoBehaviour
{
    [Header("Refs")]
    public AppUIManager ui;

    [Header("Inputs")]
    public TMP_InputField inputMapId;      // Immersal Map ID, not our DB record id
    public TMP_InputField inputMapName;
    public TMP_Dropdown dropdownCategory;
    public TMP_InputField inputDetails;
    public TMP_InputField inputLevel;

    [Header("Location")]
    public TMP_Text txtCurrentCoords;

    [Header("Buttons")]
    public Button btnSave;
    public Button btnGetCurrentLocation;

    private bool _hasSelectedLocation;
    private double _selectedLat;
    private double _selectedLon;
    private double _selectedAlt;

    private void Start()
    {
        if (btnSave != null)
            btnSave.onClick.AddListener(OnClickSave);

        if (btnGetCurrentLocation != null)
            btnGetCurrentLocation.onClick.AddListener(OnClickGetCurrentLocation);
    }

    private void OnDestroy()
    {
        if (btnSave != null)
            btnSave.onClick.RemoveListener(OnClickSave);

        if (btnGetCurrentLocation != null)
            btnGetCurrentLocation.onClick.RemoveListener(OnClickGetCurrentLocation);
    }

    private void OnEnable()
    {
        ClearForm();
    }

    private void ClearForm()
    {
        if (inputMapId != null) inputMapId.text = "";
        if (inputMapName != null) inputMapName.text = "";
        if (inputDetails != null) inputDetails.text = "";
        if (inputLevel != null) inputLevel.text = "";

        if (dropdownCategory != null)
        {
            dropdownCategory.value = 0;
            dropdownCategory.RefreshShownValue();
        }

        _hasSelectedLocation = false;
        _selectedLat = 0;
        _selectedLon = 0;
        _selectedAlt = 0;

        SetCoordsText("");
    }

    private void OnClickGetCurrentLocation()
    {
        StartCoroutine(GetLocationOnce((success, lat, lon, alt, errorMsg) =>
        {
            if (!success)
            {
                Debug.LogWarning("[AddNewMapUI] Location failed: " + errorMsg);
                SetCoordsText("Location failed: " + errorMsg);
                return;
            }

            _hasSelectedLocation = true;
            _selectedLat = lat;
            _selectedLon = lon;
            _selectedAlt = alt;

            SetCoordsText($"Lat: {_selectedLat:F6}, Lon: {_selectedLon:F6}, Alt: {_selectedAlt:F1}");
            Debug.Log($"[AddNewMapUI] Current location selected: {_selectedLat:F6}, {_selectedLon:F6}, {_selectedAlt:F1}");
        }));
    }

    private void OnClickSave()
    {
        if (!CheckFirebaseReady())
            return;

        string immersalMapId = inputMapId != null ? inputMapId.text.Trim() : "";
        string mapName = inputMapName != null ? inputMapName.text.Trim() : "";

        if (string.IsNullOrEmpty(immersalMapId) || string.IsNullOrEmpty(mapName))
        {
            Debug.LogWarning("[AddNewMapUI] Please enter Immersal Map ID and Name.");
            return;
        }

        if (_hasSelectedLocation)
        {
            SaveMapRecord(immersalMapId, mapName, _selectedLat, _selectedLon, _selectedAlt);
            return;
        }

        // Eski MapAdminUI mantığı: save sırasında da cihaz konumu almaya çalış.
        StartCoroutine(GetLocationOnce((success, lat, lon, alt, errorMsg) =>
        {
            if (!success)
            {
                Debug.LogWarning("[AddNewMapUI] Location failed, saving map without coords: " + errorMsg);
                SaveMapRecord(immersalMapId, mapName, 0, 0, 0);
                return;
            }

            SaveMapRecord(immersalMapId, mapName, lat, lon, alt);
        }));
    }

    private void SaveMapRecord(string immersalMapId, string mapName, double lat, double lon, double alt)
    {
        string category = dropdownCategory != null && dropdownCategory.options.Count > 0
            ? dropdownCategory.options[dropdownCategory.value].text
            : "";

        string details = inputDetails != null ? inputDetails.text.Trim() : "";
        string level = inputLevel != null ? inputLevel.text.Trim() : "";
        string creatorUid = CurrentUid;

        string mapRecordId = FirebaseInitializer.DB.Child("maps").Push().Key;
        if (string.IsNullOrEmpty(mapRecordId))
            mapRecordId = Guid.NewGuid().ToString("N");

        // Eski MapAdminUI ile aynı ana alanlar korunuyor: name, lat, lon, alt.
        // Yeni fark: inputMapId artık DB key değil, Immersal ID olarak saklanıyor.
        var data = new Dictionary<string, object>
        {
            { "name", mapName },
            { "immersalMapId", immersalMapId },
            { "category", category },
            { "details", details },
            { "level", level },
            { "lat", lat },
            { "lon", lon },
            { "alt", alt },
            { "approvalStatus", "draft" },
            { "creatorUid", creatorUid },
            { "createdAt", ServerValue.Timestamp }
        };

        FirebaseInitializer.DB.Child("maps").Child(mapRecordId).SetValueAsync(data)
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    Debug.LogError("[AddNewMapUI] Failed to create map: " + task.Exception);
                    return;
                }

                Debug.Log($"[AddNewMapUI] Map created: recordId={mapRecordId}, immersalMapId={immersalMapId}");

                var listUI = FindFirstObjectByType<CreateMapsListUI>();
                if (listUI != null)
                    listUI.RefreshList();

                if (ui != null)
                    ui.GoBack();

                ClearForm();
            });
    }

    private System.Collections.IEnumerator GetLocationOnce(Action<bool, double, double, double, string> callback)
    {
#if UNITY_EDITOR
        // Editor’da gerçek GPS yok; eski MapAdminUI ile aynı debug fallback.
        callback(true, 41.0, 29.0, 0, null);
        yield break;
#else
        if (!Input.location.isEnabledByUser)
        {
            callback(false, 0, 0, 0, "Location service disabled.");
            yield break;
        }

        Input.location.Start(1f, 1f);

        int maxWait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && maxWait > 0)
        {
            yield return new WaitForSeconds(1);
            maxWait--;
        }

        if (maxWait <= 0)
        {
            Input.location.Stop();
            callback(false, 0, 0, 0, "Location timeout.");
            yield break;
        }

        if (Input.location.status != LocationServiceStatus.Running)
        {
            Input.location.Stop();
            callback(false, 0, 0, 0, "Location not running.");
            yield break;
        }

        var data = Input.location.lastData;
        Input.location.Stop();

        callback(true, data.latitude, data.longitude, data.altitude, null);
#endif
    }

    private string CurrentUid
    {
        get
        {
            if (AuthManager.Instance != null && AuthManager.Instance.CurrentUser != null)
                return AuthManager.Instance.CurrentUser.UserId;

            try
            {
                var user = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser;
                return user != null ? user.UserId : "";
            }
            catch
            {
                return "";
            }
        }
    }

    private bool CheckFirebaseReady()
    {
        if (!FirebaseInitializer.Ready || FirebaseInitializer.DB == null)
        {
            Debug.LogWarning("[AddNewMapUI] Firebase not ready.");
            return false;
        }

        return true;
    }

    private void SetCoordsText(string text)
    {
        if (txtCurrentCoords != null)
            txtCurrentCoords.text = text;
    }
}