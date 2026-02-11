using System;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MapAdminUI : MonoBehaviour
{
    [Header("Root")]
    public GameObject panelRoot;          // Tüm Map admin paneli (aç/kapa)

    [Header("Top Mode Buttons")]
    public Button btnEditMaps;            // "Edit maps"
    public Button btnAddLocation;         // "Add location"

    [Header("Common UI")]
    public TMP_Text statusLabel;          // Alt taraftaki bilgi / hata
    public ScrollRect mapListScroll;      // Map listesini göstereceğin ScrollView
    public Transform mapListContent;      // ScrollView içindeki Content
    public GameObject mapListItemPrefab;  // Tek satır prefab (Button + TMP_Text)

    [Header("Edit Map Panels")]
    public GameObject editMapRoot;        // Edit Maps modundaki ana panel
    public Button btnAddNewMap;           // "Add new map"

    [Header("Edit Selected Map")]
    public GameObject selectedMapOptionsRoot; // Seçili map için "Edit name / Edit coords" seçenekleri
    public Button btnEditName;
    public Button btnEditCoords;

    [Header("Edit Name Panel")]
    public GameObject editNamePanel;
    public TMP_Text currentNameLabel;     // "Current name: xxx"
    public TMP_InputField newNameInput;
    public Button btnSaveName;

    [Header("Edit Coords Panel")]
    public GameObject editCoordsPanel;
    public TMP_Text currentCoordsLabel;   // "Current: lat,lon,alt"
    public Button btnUseDeviceLocation;   // Cihazdan al
    public Button btnSaveCoords;          // (istersen manuel input da ekleyebilirsin)

    [Header("Add New Map Panel")]
    public GameObject addNewMapPanel;
    public TMP_InputField newMapIdInput;
    public TMP_InputField newMapNameInput;
    public Button btnSaveNewMap;

    [Header("Add Location Mode")]
    public GameObject addLocationRoot;    // "Add location" modunun ana paneli
    public Button btnSaveLocationForSelected; // Seçili map için konum kaydet

    private DatabaseReference DB => FirebaseInitializer.DB;

    // Hafızada tuttuğumuz map info
    [Serializable]
    public class MapInfo
    {
        public string id;
        public string name;
        public double lat;
        public double lon;
        public double alt;
    }

    private readonly Dictionary<string, MapInfo> _maps = new();
    private string _selectedMapId;
    private enum Mode { None, EditMaps, AddLocation, AddNewMap }
    private Mode _mode = Mode.None;

    private void SetMode(Mode mode)
    {
        _mode = mode;

        // Reset selection-dependent edit UI whenever mode changes.
        if (selectedMapOptionsRoot != null) selectedMapOptionsRoot.SetActive(false);
        if (editNamePanel != null) editNamePanel.SetActive(false);
        if (editCoordsPanel != null) editCoordsPanel.SetActive(false);

        // Clear selection when switching modes; user can re-select.
        _selectedMapId = null;
    }

    void Start()
    {
        // Paneli başta kapalı tutmak istersen:
        if (panelRoot != null) panelRoot.SetActive(false);

        WireButtons();
    }

    void WireButtons()
    {
        if (btnEditMaps != null)
            btnEditMaps.onClick.AddListener(() => EnterEditMapsMode());

        if (btnAddLocation != null)
            btnAddLocation.onClick.AddListener(() => EnterAddLocationMode());

        if (btnAddNewMap != null)
            btnAddNewMap.onClick.AddListener(() => EnterAddNewMapMode());

        if (btnEditName != null)
            btnEditName.onClick.AddListener(() => ShowEditNamePanel());

        if (btnEditCoords != null)
            btnEditCoords.onClick.AddListener(() => ShowEditCoordsPanel());

        if (btnSaveName != null)
            btnSaveName.onClick.AddListener(() => SaveEditedName());

        if (btnUseDeviceLocation != null)
            btnUseDeviceLocation.onClick.AddListener(() => StartCoroutine(SaveDeviceLocationForSelected(false)));

        if (btnSaveCoords != null)
            btnSaveCoords.onClick.AddListener(() => StartCoroutine(SaveDeviceLocationForSelected(true)));

        if (btnSaveNewMap != null)
            btnSaveNewMap.onClick.AddListener(() => SaveNewMap());

        if (btnSaveLocationForSelected != null)
            btnSaveLocationForSelected.onClick.AddListener(() => StartCoroutine(SaveDeviceLocationForSelected(true)));
    }

    // Admin menüden çağırılacak
    public void TogglePanel()
    {
        if (panelRoot == null) return;

        bool newState = !panelRoot.activeSelf;
        panelRoot.SetActive(newState);

        if (newState)
        {
            ClearStatus();

            // Do NOT default to Edit Maps. User must choose a mode.
            SetMode(Mode.None);

            // Keep the main buttons visible (Edit maps / Add location / Add new map),
            // but hide selected-map edit options until Edit Maps mode is chosen.
            ShowEditMapsRoot(true);
            ShowAddLocationRoot(false);
            if (selectedMapOptionsRoot != null) selectedMapOptionsRoot.SetActive(false);

            // Still load the map list so the user can browse, but keep edit options hidden.
            if (CheckFirebaseReady())
            {
                SetStatus("Choose a mode: Edit maps or Add location.");
                RefreshMapList();
            }
            else
            {
                SetStatus("Firebase not ready.");
            }
        }
    }

    #region Modes

    void EnterEditMapsMode()
    {
        if (!CheckFirebaseReady()) return;
        SetStatus("Loading maps...");
        SetMode(Mode.EditMaps);

        ShowEditMapsRoot(true);
        ShowAddLocationRoot(false);

        RefreshMapList(() =>
        {
            SetStatus("Select a map or add new one.");
        });
    }

    void EnterAddLocationMode()
    {
        if (!CheckFirebaseReady()) return;
        SetStatus("Choose a map to assign location.");
        SetMode(Mode.AddLocation);

        ShowEditMapsRoot(true);
        ShowAddLocationRoot(true);

        RefreshMapList(() =>
        {
            SetStatus("Tap a map, then 'Save location'.");
        });
    }

    void EnterAddNewMapMode()
    {
        if (!CheckFirebaseReady()) return;
        SetStatus("Create a new map.");
        SetMode(Mode.AddNewMap);

        // Keep the mode buttons visible.
        ShowEditMapsRoot(true);
        ShowAddLocationRoot(false);

        ShowAddNewMapPanel();
    }

    void ShowEditMapsRoot(bool show)
    {
        if (editMapRoot != null) editMapRoot.SetActive(show);
        if (selectedMapOptionsRoot != null) selectedMapOptionsRoot.SetActive(false);
        if (editNamePanel != null) editNamePanel.SetActive(false);
        if (editCoordsPanel != null) editCoordsPanel.SetActive(false);
        if (addNewMapPanel != null) addNewMapPanel.SetActive(false);
    }

    void ShowAddLocationRoot(bool show)
    {
        if (addLocationRoot != null) addLocationRoot.SetActive(show);
        if (editNamePanel != null) editNamePanel.SetActive(false);
        if (editCoordsPanel != null) editCoordsPanel.SetActive(false);
        if (addNewMapPanel != null) addNewMapPanel.SetActive(false);
        if (selectedMapOptionsRoot != null) selectedMapOptionsRoot.SetActive(false);
    }

    #endregion

    #region List maps

    void ClearMapList()
    {
        if (mapListContent == null) return;

        for (int i = mapListContent.childCount - 1; i >= 0; i--)
        {
            Destroy(mapListContent.GetChild(i).gameObject);
        }
    }

    void RefreshMapList(Action onDone = null)
    {
        if (!CheckFirebaseReady()) return;

        ClearMapList();
        _maps.Clear();
        _selectedMapId = null;
        if (selectedMapOptionsRoot != null) selectedMapOptionsRoot.SetActive(false);

        DB.Child("maps").GetValueAsync().ContinueWithOnMainThread(task =>
        {
            if (!task.IsCompletedSuccessfully || task.Result == null || !task.Result.Exists)
            {
                SetStatus("No maps found. You can add a new one.");
                onDone?.Invoke();
                return;
            }

            foreach (var child in task.Result.Children)
            {
                string id = child.Key;
                string name = child.Child("name").Value?.ToString() ?? id;

                double lat = 0, lon = 0, alt = 0;
                double.TryParse(child.Child("lat").Value?.ToString(), out lat);
                double.TryParse(child.Child("lon").Value?.ToString(), out lon);
                double.TryParse(child.Child("alt").Value?.ToString(), out alt);

                var info = new MapInfo
                {
                    id = id,
                    name = name,
                    lat = lat,
                    lon = lon,
                    alt = alt
                };
                _maps[id] = info;

                CreateMapListItem(info);
            }

            onDone?.Invoke();
        });
    }

    void CreateMapListItem(MapInfo info)
    {
        if (mapListItemPrefab == null || mapListContent == null) return;

        var go = Instantiate(mapListItemPrefab, mapListContent);
        var txt = go.GetComponentInChildren<TMP_Text>();
        if (txt != null)
        {
            txt.text = $"{info.name}  ({info.id})";
        }

        var btn = go.GetComponent<Button>();
        if (btn != null)
        {
            btn.onClick.AddListener(() => OnMapRowClicked(info.id));
        }
    }

    void OnMapRowClicked(string mapId)
    {
        if (!_maps.TryGetValue(mapId, out var info))
        {
            SetStatus("Map not found in local cache.");
            return;
        }

        _selectedMapId = mapId;

        if (selectedMapOptionsRoot != null)
            selectedMapOptionsRoot.SetActive(_mode == Mode.EditMaps);

        // In non-EditMaps modes, never show edit panels.
        if (_mode != Mode.EditMaps)
        {
            if (editNamePanel != null) editNamePanel.SetActive(false);
            if (editCoordsPanel != null) editCoordsPanel.SetActive(false);
        }

        UpdateSelectedMapInfoUI(info);

        SetStatus($"Selected map: {info.name} ({info.id})");
    }

    void UpdateSelectedMapInfoUI(MapInfo info)
    {
        if (currentNameLabel != null)
            currentNameLabel.text = $"Current name: {info.name}";

        if (currentCoordsLabel != null)
            currentCoordsLabel.text = $"Current: {info.lat:F6}, {info.lon:F6}, {info.alt:F1}";
    }

    #endregion

    #region Edit name

    void ShowEditNamePanel()
    {
        if (string.IsNullOrEmpty(_selectedMapId))
        {
            SetStatus("Please select a map first.");
            return;
        }

        if (!_maps.TryGetValue(_selectedMapId, out var info))
        {
            SetStatus("Map not found.");
            return;
        }

        UpdateSelectedMapInfoUI(info);
        if (newNameInput != null) newNameInput.text = info.name;

        if (editNamePanel != null) editNamePanel.SetActive(true);
        if (editCoordsPanel != null) editCoordsPanel.SetActive(false);
        if (addNewMapPanel != null) addNewMapPanel.SetActive(false);
    }

    void SaveEditedName()
    {
        if (string.IsNullOrEmpty(_selectedMapId))
        {
            SetStatus("No map selected.");
            return;
        }

        if (newNameInput == null)
        {
            SetStatus("Name input not assigned.");
            return;
        }

        string newName = newNameInput.text.Trim();
        if (string.IsNullOrEmpty(newName))
        {
            SetStatus("New name cannot be empty.");
            return;
        }

        SetStatus("Saving name...");
        DB.Child("maps").Child(_selectedMapId).Child("name").SetValueAsync(newName)
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    SetStatus("Failed to save name.");
                    Debug.LogError(task.Exception);
                    return;
                }

                if (_maps.TryGetValue(_selectedMapId, out var info))
                {
                    info.name = newName;
                    _maps[_selectedMapId] = info;
                    UpdateSelectedMapInfoUI(info);
                }

                RefreshMapList();
                SetStatus("Name updated.");
            });
    }

    #endregion

    #region Add new map

    void ShowAddNewMapPanel()
    {
        if (addNewMapPanel != null) addNewMapPanel.SetActive(true);
        if (editNamePanel != null) editNamePanel.SetActive(false);
        if (editCoordsPanel != null) editCoordsPanel.SetActive(false);

        if (newMapIdInput != null) newMapIdInput.text = "";
        if (newMapNameInput != null) newMapNameInput.text = "";
    }

    void SaveNewMap()
    {
        if (!CheckFirebaseReady()) return;

        string mapId = newMapIdInput != null ? newMapIdInput.text.Trim() : "";
        string name = newMapNameInput != null ? newMapNameInput.text.Trim() : "";

        if (string.IsNullOrEmpty(mapId) || string.IsNullOrEmpty(name))
        {
            SetStatus("Please enter Map ID and Name.");
            return;
        }

        SetStatus("Creating new map...");

        // Önce cihazdan konum almaya çalışalım
        StartCoroutine(SaveNewMapWithDeviceLocation(mapId, name));
    }

    System.Collections.IEnumerator SaveNewMapWithDeviceLocation(string mapId, string name)
    {
        // Konumu al
        yield return StartCoroutine(GetLocationOnce((success, lat, lon, alt, errorMsg) =>
        {
            if (!success)
            {
                SetStatus("Location failed, saving map without coords: " + errorMsg);
                SaveMapRecord(mapId, name, 0, 0, 0);
            }
            else
            {
                SaveMapRecord(mapId, name, lat, lon, alt);
            }
        }));
    }

    void SaveMapRecord(string mapId, string name, double lat, double lon, double alt)
    {
        var data = new Dictionary<string, object>
        {
            { "name", name },
            { "lat", lat },
            { "lon", lon },
            { "alt", alt }
        };

        DB.Child("maps").Child(mapId).SetValueAsync(data)
            .ContinueWithOnMainThread(task =>
            {
                if (!task.IsCompletedSuccessfully)
                {
                    SetStatus("Failed to create map.");
                    Debug.LogError(task.Exception);
                    return;
                }

                SetStatus("Map created.");
                if (addNewMapPanel != null) addNewMapPanel.SetActive(false);

                RefreshMapList();
            });
    }

    #endregion

    #region Edit / Add coords via device location

    void ShowEditCoordsPanel()
    {
        if (string.IsNullOrEmpty(_selectedMapId))
        {
            SetStatus("Please select a map first.");
            return;
        }

        if (!_maps.TryGetValue(_selectedMapId, out var info))
        {
            SetStatus("Map not found.");
            return;
        }

        UpdateSelectedMapInfoUI(info);

        if (editCoordsPanel != null) editCoordsPanel.SetActive(true);
        if (editNamePanel != null) editNamePanel.SetActive(false);
        if (addNewMapPanel != null) addNewMapPanel.SetActive(false);
    }

    System.Collections.IEnumerator SaveDeviceLocationForSelected(bool alsoClosePanels)
    {
        if (string.IsNullOrEmpty(_selectedMapId))
        {
            SetStatus("Please select a map first.");
            yield break;
        }

        SetStatus("Getting device location...");

        yield return StartCoroutine(GetLocationOnce((success, lat, lon, alt, errorMsg) =>
        {
            if (!success)
            {
                SetStatus("Location failed: " + errorMsg);
                return;
            }

            SetStatus("Saving coordinates...");
            var data = new Dictionary<string, object>
            {
                { "lat", lat },
                { "lon", lon },
                { "alt", alt }
            };

            DB.Child("maps").Child(_selectedMapId).UpdateChildrenAsync(data)
                .ContinueWithOnMainThread(task =>
                {
                    if (!task.IsCompletedSuccessfully)
                    {
                        SetStatus("Failed to save coordinates.");
                        Debug.LogError(task.Exception);
                        return;
                    }

                    if (_maps.TryGetValue(_selectedMapId, out var info))
                    {
                        info.lat = lat;
                        info.lon = lon;
                        info.alt = alt;
                        _maps[_selectedMapId] = info;
                        UpdateSelectedMapInfoUI(info);
                    }

                    SetStatus("Coordinates saved.");
                    if (alsoClosePanels)
                    {
                        if (editCoordsPanel != null) editCoordsPanel.SetActive(false);
                    }
                });
        }));
    }

    // Cihazdan bir kere GPS verisi al
    System.Collections.IEnumerator GetLocationOnce(Action<bool, double, double, double, string> callback)
    {
#if UNITY_EDITOR
        // Editor’da gerçek GPS yok; debug için sabit değer döndürelim
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

    #endregion

    #region Helpers

    bool CheckFirebaseReady()
    {
        if (!FirebaseInitializer.Ready || DB == null)
        {
            SetStatus("Firebase not ready.");
            return false;
        }
        return true;
    }

    void SetStatus(string msg)
    {
        Debug.Log("[MapAdmin] " + msg);
        if (statusLabel != null)
            statusLabel.text = msg;
    }

    void ClearStatus() => SetStatus("");

    #endregion
}