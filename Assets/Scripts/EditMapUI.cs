using System;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class EditMapUI : MonoBehaviour
{
    [Header("Refs")]
    public AppUIManager ui;

    [Header("Inputs")]
    public TMP_InputField inputMapId;
    public TMP_InputField inputMapName;
    public TMP_Dropdown dropdownCategory;
    public TMP_InputField inputDetails;
    public TMP_InputField inputLevel;

    [Header("Location")]
    public TMP_Text txtCurrentCoords;

    [Header("Rejection Feedback")]
    [Tooltip("Txt_RejectionFeedback — visible only when approvalStatus == rejected")]
    public TMP_Text txtRejectionFeedback;

    [Header("Submit Feedback")]
    [Tooltip("Txt_Feedback — shows green confirmation after submit")]
    public TMP_Text txtFeedback;

    [Header("Buttons")]
    public Button btnBack;
    public Button btnSave;
    public Button btnDelete;
    public Button btnClues;
    public Button btnSubmit;
    public Button btnGetCurrentLocation;

    public static string SelectedMapId { get; private set; }
    public static int SelectedImmersalMapId { get; private set; }
    public static string SelectedMapName { get; private set; }

    private DatabaseReference DB => FirebaseInitializer.DB;

    private bool _hasSelectedLocation;
    private double _selectedLat;
    private double _selectedLon;
    private double _selectedAlt;

    private DatabaseReference _mapRef;
    private EventHandler<ValueChangedEventArgs> _mapHandler;

    private void Start()
    {
        WireButtons();
    }

    private void OnDestroy()
    {
        UnwireButtons();
        UnsubscribeMap();
    }

    private void OnEnable()
    {
        if (txtRejectionFeedback != null)
            txtRejectionFeedback.gameObject.SetActive(false);
        if (txtFeedback != null)
            txtFeedback.gameObject.SetActive(false);

        if (!string.IsNullOrEmpty(SelectedMapId))
            SubscribeMap(SelectedMapId);
    }

    private void OnDisable()
    {
        UnsubscribeMap();
    }

    private void SubscribeMap(string mapId)
    {
        UnsubscribeMap();

        _mapRef = DB.Child("maps").Child(mapId);
        _mapHandler = (_, args) =>
        {
            if (args.DatabaseError != null) return;
            ApplySnapshot(args.Snapshot);
        };
        _mapRef.ValueChanged += _mapHandler;
    }

    private void UnsubscribeMap()
    {
        if (_mapRef != null && _mapHandler != null)
            _mapRef.ValueChanged -= _mapHandler;
        _mapRef = null;
        _mapHandler = null;
    }

    void WireButtons()
    {
        if (btnBack != null) btnBack.onClick.AddListener(OnClickBack);
        if (btnSave != null) btnSave.onClick.AddListener(OnClickSave);
        if (btnDelete != null) btnDelete.onClick.AddListener(OnClickDelete);
        if (btnClues != null) btnClues.onClick.AddListener(OnClickClues);
        if (btnSubmit != null) btnSubmit.onClick.AddListener(OnClickSubmit);
        if (btnGetCurrentLocation != null) btnGetCurrentLocation.onClick.AddListener(OnClickGetLocation);
    }

    void UnwireButtons()
    {
        if (btnBack != null) btnBack.onClick.RemoveListener(OnClickBack);
        if (btnSave != null) btnSave.onClick.RemoveListener(OnClickSave);
        if (btnDelete != null) btnDelete.onClick.RemoveListener(OnClickDelete);
        if (btnClues != null) btnClues.onClick.RemoveListener(OnClickClues);
        if (btnSubmit != null) btnSubmit.onClick.RemoveListener(OnClickSubmit);
        if (btnGetCurrentLocation != null) btnGetCurrentLocation.onClick.RemoveListener(OnClickGetLocation);
    }

    public static void SetSelectedMap(string mapId)
    {
        SelectedMapId = mapId;
        SelectedImmersalMapId = 0;
        SelectedMapName = "";
    }

    // Allow other UI code (e.g. AppUIManager) to populate the static cache after a fallback Firebase fetch.
    public static void UpdateSelectedMap(string mapId, int immersalMapId, string mapName)
    {
        SelectedMapId = mapId;
        SelectedImmersalMapId = immersalMapId;
        SelectedMapName = mapName ?? "";
    }

    public void LoadMap(string mapId)
    {
        if (string.IsNullOrEmpty(mapId)) return;
        SelectedMapId = mapId;
        SubscribeMap(mapId);
    }

    private void ApplySnapshot(DataSnapshot snap)
    {
        if (snap == null || !snap.Exists) return;

        string name          = snap.Child("name").Value?.ToString() ?? "";
        string immersalMapId = snap.Child("immersalMapId").Value?.ToString() ?? "";
        string category      = snap.Child("category").Value?.ToString() ?? "";
        string details       = snap.Child("details").Value?.ToString() ?? "";
        string level         = snap.Child("level").Value?.ToString() ?? "";

        SelectedMapName = name;
        int.TryParse(immersalMapId, out int parsedImmersalId);
        SelectedImmersalMapId = parsedImmersalId;

        double lat = ReadDouble(snap, "lat");
        double lon = ReadDouble(snap, "lon");
        double alt = ReadDouble(snap, "alt");

        _selectedLat = lat;
        _selectedLon = lon;
        _selectedAlt = alt;
        _hasSelectedLocation = true;

        if (inputMapId   != null) inputMapId.text   = immersalMapId;
        if (inputMapName != null) inputMapName.text  = name;
        if (inputDetails != null) inputDetails.text  = details;
        if (inputLevel   != null) inputLevel.text    = level;

        SetDropdown(dropdownCategory, category);
        SetCoordsText($"Lat: {lat:F6}, Lon: {lon:F6}, Alt: {alt:F1}");

        string approvalStatus    = snap.Child("approvalStatus").Value?.ToString() ?? "";
        string rejectionFeedback = snap.Child("rejectionFeedback").Value?.ToString() ?? "";

        if (txtRejectionFeedback != null)
        {
            bool isRejected = approvalStatus == "rejected" && !string.IsNullOrEmpty(rejectionFeedback);
            txtRejectionFeedback.gameObject.SetActive(isRejected);
            txtRejectionFeedback.text = isRejected ? $"Rejection reason: {rejectionFeedback}" : "";
        }

        // Clear submit feedback when status changes externally (e.g. admin acts).
        if (txtFeedback != null && approvalStatus != "pending")
            txtFeedback.gameObject.SetActive(false);
    }

    void OnClickBack()
    {
        if (ui != null) ui.GoBackCreate();
    }

    void OnClickGetLocation()
    {
        StartCoroutine(GetLocationOnce((ok, lat, lon, alt, err) =>
        {
            if (!ok)
            {
                SetCoordsText("Location failed");
                return;
            }

            _hasSelectedLocation = true;
            _selectedLat = lat;
            _selectedLon = lon;
            _selectedAlt = alt;

            SetCoordsText($"Lat: {lat:F6}, Lon: {lon:F6}, Alt: {alt:F1}");
        }));
    }

    void OnClickSave()
    {
        string mapRecordId = SelectedMapId;
        string immersalMapId = inputMapId.text;
        string mapName = inputMapName.text;

        SelectedMapName = mapName;
        int.TryParse(immersalMapId, out int parsedImmersalId);
        SelectedImmersalMapId = parsedImmersalId;

        if (string.IsNullOrEmpty(mapRecordId)) return;

        var updates = new Dictionary<string, object>
        {
            {"name", mapName},
            {"immersalMapId", immersalMapId},
            {"category", dropdownCategory.options[dropdownCategory.value].text},
            {"details", inputDetails.text},
            {"level", inputLevel.text},
            {"approvalStatus", "pending"}
        };

        if (_hasSelectedLocation)
        {
            updates["lat"] = _selectedLat;
            updates["lon"] = _selectedLon;
            updates["alt"] = _selectedAlt;
        }

        DB.Child("maps").Child(mapRecordId).UpdateChildrenAsync(updates)
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    Debug.LogError("[EditMapUI] Save failed: " + task.Exception);
                    return;
                }

                Debug.Log("[EditMapUI] Map updated: " + mapRecordId);

                var adminUI = FindFirstObjectByType<AdminMapsStatusUI>(FindObjectsInactive.Include);
                if (adminUI != null)
                    adminUI.ForceRefresh();

                if (ui != null)
                    ui.GoBackCreate();
            });
    }

    void OnClickDelete()
    {
        string mapRecordId = SelectedMapId;
        if (string.IsNullOrEmpty(mapRecordId)) return;

        DB.Child("maps").Child(mapRecordId).RemoveValueAsync();
        DB.Child("anchors").Child(mapRecordId).RemoveValueAsync();

        var adminUI = FindFirstObjectByType<AdminMapsStatusUI>(FindObjectsInactive.Include);
        if (adminUI != null)
            adminUI.ForceRefresh();

        ui.GoBackCreate();
    }

    void OnClickClues()
    {
        ApplySelectedMapToMapRootProvider();
        ui.ShowEditClues();
    }

    void ApplySelectedMapToMapRootProvider()
    {
        if (string.IsNullOrEmpty(SelectedMapId))
        {
            Debug.LogWarning("[EditMapUI] Cannot apply selected map: SelectedMapId is empty.");
            return;
        }

        if (SelectedImmersalMapId <= 0)
        {
            string immersalMapId = inputMapId != null ? inputMapId.text : "";
            int.TryParse(immersalMapId, out int parsedImmersalId);
            SelectedImmersalMapId = parsedImmersalId;
        }

        if (string.IsNullOrEmpty(SelectedMapName) && inputMapName != null)
            SelectedMapName = inputMapName.text;

        if (SelectedImmersalMapId <= 0)
        {
            Debug.LogWarning("[EditMapUI] Cannot apply selected map: Immersal id is empty or invalid.");
            return;
        }

        var provider = FindFirstObjectByType<MapRootProvider>(FindObjectsInactive.Include);
        if (provider == null)
        {
            Debug.LogWarning("[EditMapUI] Cannot apply selected map: MapRootProvider not found in scene.");
            return;
        }

        provider.SetMapByDbKey(SelectedMapId, SelectedImmersalMapId, SelectedMapName);
        Debug.Log($"[EditMapUI] Applied selected map to MapRootProvider. dbKey={SelectedMapId}, immersalId={SelectedImmersalMapId}, name='{SelectedMapName}'");
    }

    void OnClickSubmit()
    {
        string mapRecordId = SelectedMapId;
        if (string.IsNullOrEmpty(mapRecordId)) return;

        DB.Child("maps").Child(mapRecordId).Child("approvalStatus").SetValueAsync("pending")
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    if (txtFeedback != null)
                    {
                        txtFeedback.text = "Submitted for admin approval!";
                        txtFeedback.color = new Color(0.18f, 0.72f, 0.18f);
                        txtFeedback.gameObject.SetActive(true);
                    }

                    var adminUI = FindFirstObjectByType<AdminMapsStatusUI>(FindObjectsInactive.Include);
                    if (adminUI != null)
                        adminUI.ForceRefresh();
                }
                else
                {
                    if (txtFeedback != null)
                    {
                        txtFeedback.text = "Submit failed. Try again.";
                        txtFeedback.color = new Color(0.85f, 0.15f, 0.15f);
                        txtFeedback.gameObject.SetActive(true);
                    }
                }
            });
    }

    double ReadDouble(DataSnapshot snap, string key)
    {
        var val = snap.Child(key).Value;
        if (val == null) return 0.0;
        if (val is double d) return d;
        if (val is long   l) return (double)l;
        double.TryParse(
            System.Convert.ToString(val, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double v);
        return v;
    }

    void SetDropdown(TMP_Dropdown dd, string value)
    {
        if (dd == null) return;

        for (int i = 0; i < dd.options.Count; i++)
        {
            if (dd.options[i].text == value)
            {
                dd.value = i;
                break;
            }
        }
    }

    void SetCoordsText(string txt)
    {
        if (txtCurrentCoords != null)
            txtCurrentCoords.text = txt;
    }

    System.Collections.IEnumerator GetLocationOnce(Action<bool, double, double, double, string> cb)
    {
#if UNITY_EDITOR
        cb(true, 41.0, 29.0, 0, null);
        yield break;
#else
        if (!Input.location.isEnabledByUser)
        {
            cb(false,0,0,0,"disabled");
            yield break;
        }

        Input.location.Start();

        int wait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && wait-- > 0)
            yield return new WaitForSeconds(1);

        if (Input.location.status != LocationServiceStatus.Running)
        {
            cb(false,0,0,0,"fail");
            yield break;
        }

        var d = Input.location.lastData;
        Input.location.Stop();

        cb(true, d.latitude, d.longitude, d.altitude, null);
#endif
    }
}