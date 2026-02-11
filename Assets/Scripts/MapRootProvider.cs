using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class MapRootProvider : MonoBehaviour
{
    // PlayerPrefs keys (force a specific map selection once after a scene reload)
    public const string PREFS_FORCE_MAP_ONCE = "who8_force_map_once";
    public const string PREFS_SELECTED_MAP_ID = "who8_selected_map_id";
    public const string PREFS_SELECTED_MAP_NAME = "who8_selected_map_name";

    [Header("Immersal / AR Space")]
    [Tooltip("Immersal XRSpace / AR Space (hierarchy'de XRSpace)")]
    public Transform mapRoot;

    [Header("Current Map")]
    [Tooltip("Seçili Immersal Map ID")]
    public int mapId = 0;

    [Tooltip("Seçili haritanın adı (DB'den gelir)")]
    public string mapName = "";

    [Tooltip("Localize olunduğunda true yapın (Immersal otomatik yapıyorsa Inspector'dan işaretlemeye gerek yok)")]
    public bool localized = false;

    public bool IsLocalized => mapRoot && localized;

    [Header("Map Selection Distance Gate")]
    [Tooltip("A map can only be selected if the user is within this distance (meters).")]
    public float maxSelectableDistanceMeters = 30f;

    [Header("Auto-select nearest map")]
    [Tooltip("Konuma göre en yakın haritayı otomatik seç")]
    public bool autoSelectNearestMap = true;

    [Tooltip("Firebase DB'de map listesinin path'i (örn: maps)")]
    public string mapsDbPath = "maps";

    [Tooltip("Lokasyon servisini başlatırken en fazla bekleme (sn)")]
    public float locationInitTimeoutSeconds = 15f;

    [Tooltip("Lokasyon doğruluğu (metre). Daha düşük = daha iyi doğruluk ama daha yavaş olabilir.")]
    public float desiredAccuracyMeters = 25f;

    [Tooltip("Lokasyon güncelleme mesafesi (metre).")]
    public float updateDistanceMeters = 10f;

    [Tooltip("Bir kez otomatik seçince tekrar seçmesin (Refresh butonu yine çalışır)")]
    public bool lockAfterAutoSelect = true;

    [Header("UI - Selected Map Label")]
    [Tooltip("Üstte seçili harita adını göstermek için (Unity UI Text) - opsiyonel")]
    public Text selectedMapNameText;

    [Tooltip("Üstte seçili harita adını göstermek için (TMP_Text)")]
    public TMP_Text selectedMapNameTMP;

    [Header("UI - Localization Indicator")]
    [Tooltip("Map bar yanında gösterilen 'O' localization indicator (TMP_Text)")]
    public TMP_Text localizedIndicatorTMP;

    [Tooltip("Localized durumunda kullanılacak renk")]
    public Color localizedColor = Color.green;

    [Tooltip("Not localized durumunda kullanılacak renk")]
    public Color notLocalizedColor = Color.red;

    [Tooltip("Seçili harita yazısının olduğu bölgeye tıklanınca dropdown açılsın. (Button ekleyip buraya verin)")]
    public Button selectedMapToggleButton;

    [Tooltip("Refresh butonu: basınca güncel konumla en yakını tekrar seçer")]
    public Button refreshButton;

    [Header("UI - Dropdown")]
    [Tooltip("Dropdown listesinin parent panel'i. Yoksa runtime'da oluşturulur.")]
    public RectTransform dropdownPanel;

    [Tooltip("Dropdown panel içindeki content parent (Vertical Layout). Boşsa dropdownPanel'in kendisi kullanılır.")]
    public RectTransform dropdownContent;

    [Tooltip("Dropdown'da gösterilecek en yakın map sayısı")]
    public int dropdownTopN = 3;

    [Tooltip("UI prefix")]
    public string uiPrefix = "Map: ";

    [Serializable]
    public class MapInfo
    {
        public int mapId;
        public string name;
        public double latitude;
        public double longitude;
        public double alt;

        public override string ToString() => $"{name} (id={mapId}) @ {latitude},{longitude}";
    }

    public event Action<int, string> OnMapSelected;

    private bool _autoSelectedOnce = false;
    private bool _dropdownOpen = false;

    private readonly List<MapInfo> _maps = new();

    private bool TryGetMapInfo(int id, out MapInfo info)
    {
        info = null;
        for (int i = 0; i < _maps.Count; i++)
        {
            var m = _maps[i];
            if (m != null && m.mapId == id)
            {
                info = m;
                return true;
            }
        }
        return false;
    }

    private bool TryGetDistanceToMap(int targetMapId, out double meters)
    {
        meters = double.MaxValue;

        if (_maps.Count == 0)
            return false;

        if (!TryGetLastLocation(out double lat, out double lon))
            return false;

        if (!TryGetMapInfo(targetMapId, out var m) || m == null)
            return false;

        meters = HaversineMeters(lat, lon, m.latitude, m.longitude);
        return true;
    }

    void Start()
    {
        EnsureDropdownObjects();

        // If we reloaded due to a map change, force the selected map exactly once.
        if (PlayerPrefs.GetInt(PREFS_FORCE_MAP_ONCE, 0) == 1)
        {
            int forcedId = PlayerPrefs.GetInt(PREFS_SELECTED_MAP_ID, mapId);
            string forcedName = PlayerPrefs.GetString(PREFS_SELECTED_MAP_NAME, "");

            // Prevent auto-select from overriding the forced choice in this run
            autoSelectNearestMap = false;

            // Apply forced selection (will fire OnMapSelected and reset localization)
            SetMap(forcedId, forcedName);

            // Clear one-shot flag
            PlayerPrefs.SetInt(PREFS_FORCE_MAP_ONCE, 0);
            PlayerPrefs.Save();
        }

        // Button wiring
        if (selectedMapToggleButton != null)
        {
            selectedMapToggleButton.onClick.RemoveListener(ToggleDropdown);
            selectedMapToggleButton.onClick.AddListener(ToggleDropdown);
        }

        if (refreshButton != null)
        {
            refreshButton.onClick.RemoveListener(OnClickRefreshNearest);
            refreshButton.onClick.AddListener(OnClickRefreshNearest);
        }

        RefreshSelectedMapUI();
        RefreshLocalizationIndicator();

        if (autoSelectNearestMap)
        {
            StartCoroutine(LoadMapsThenAutoSelect(initialRun: true));
        }
    }

    // (Opsiyonel) Immersal callback'inden çağırırsan otomatik günceller:
    public void OnLocalized(Pose worldPose, int localizedMapId)
    {
        mapId = localizedMapId;

        if (mapRoot != null)
        {
            mapRoot.SetPositionAndRotation(worldPose.position, worldPose.rotation);
        }

        localized = true;
        RefreshLocalizationIndicator();
        Debug.Log($"[MapRootProvider] OnLocalized -> mapId={mapId}, pos={worldPose.position}");
    }

    public void MarkLocalized()
    {
        localized = true;
        RefreshLocalizationIndicator();
        Debug.Log($"[MapRootProvider] MarkLocalized() -> mapId={mapId}");
    }

    public void ResetLocalization()
    {
        localized = false;
        RefreshLocalizationIndicator();
        Debug.Log("[MapRootProvider] ResetLocalization()");
    }

    /// <summary>
    /// Elle map seçmek istersen UI'dan çağır.
    /// </summary>
    public void SetMap(int newMapId, string newMapName = "")
    {
        // If selecting the same map again, don't reset localization or re-fire events.
        if (newMapId == mapId)
        {
            if (!string.IsNullOrEmpty(newMapName))
                mapName = newMapName;

            RefreshSelectedMapUI();
            Debug.Log($"[MapRootProvider] SetMap ignored (same map): mapId={mapId}, name='{mapName}'");
            return;
        }

        // Distance gate: if we can compute distance and it's too far, do not allow selecting this map.
        if (TryGetDistanceToMap(newMapId, out double distMeters))
        {
            if (distMeters > maxSelectableDistanceMeters)
            {
                Debug.LogWarning($"[MapRootProvider] SetMap blocked: mapId={newMapId} is {distMeters:F1}m away (> {maxSelectableDistanceMeters:F0}m). Selection ignored.");
                RefreshSelectedMapUI();
                return;
            }
        }

        mapId = newMapId;
        if (!string.IsNullOrEmpty(newMapName))
            mapName = newMapName;

        // Map değişince localization state sıfırlansın
        localized = false;
        RefreshLocalizationIndicator();

        RefreshSelectedMapUI();
        OnMapSelected?.Invoke(mapId, mapName);

        Debug.Log($"[MapRootProvider] SetMap -> mapId={mapId}, name='{mapName}'");
    }

    // -------------------- Dropdown UI --------------------

    private void EnsureDropdownObjects()
    {
        // Eğer kullanıcı atamadıysa, label'ın parent'ına basit bir panel oluşturalım.
        if (dropdownPanel == null)
        {
            // Paneli toggle butonun altına koymak en mantıklısı
            Transform parent = selectedMapToggleButton != null ? selectedMapToggleButton.transform.parent : this.transform;

            var panelGO = new GameObject("MapDropdownPanel", typeof(RectTransform), typeof(Image));
            dropdownPanel = panelGO.GetComponent<RectTransform>();
            dropdownPanel.SetParent(parent, worldPositionStays: false);

            var img = panelGO.GetComponent<Image>();
            img.raycastTarget = true;
            img.color = new Color(0, 0, 0, 0.55f);

            // Basit bir layout
            var vlg = panelGO.AddComponent<VerticalLayoutGroup>();
            vlg.padding = new RectOffset(12, 12, 12, 12);
            vlg.spacing = 8;
            vlg.childControlHeight = true;
            vlg.childControlWidth = true;
            vlg.childForceExpandHeight = false;
            vlg.childForceExpandWidth = true;

            var fitter = panelGO.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            // Varsayılan: kapalı
            panelGO.SetActive(false);
        }

        if (dropdownContent == null)
            dropdownContent = dropdownPanel;

        if (dropdownPanel != null)
            dropdownPanel.gameObject.SetActive(false);
    }

    public void ToggleDropdown()
    {
        if (dropdownPanel == null)
            return;

        _dropdownOpen = !_dropdownOpen;
        dropdownPanel.gameObject.SetActive(_dropdownOpen);

        if (_dropdownOpen)
        {
            StartCoroutine(RebuildDropdownList());
        }
    }

    private IEnumerator RebuildDropdownList()
    {
        // Maps yoksa önce yükle
        if (_maps.Count == 0)
            yield return LoadMapsOnly();

        // Konumu al
        if (!TryGetLastLocation(out double lat, out double lon))
        {
            // Eğer konum yoksa başlatıp almayı dene
            yield return EnsureLocationRunning();
            if (!TryGetLastLocation(out lat, out lon))
            {
                BuildDropdownFallback("Konum alınamadı");
                yield break;
            }
        }

        BuildDropdownNearest(lat, lon);
    }

    private void ClearDropdownContent()
    {
        if (dropdownContent == null) return;
        for (int i = dropdownContent.childCount - 1; i >= 0; i--)
        {
            Destroy(dropdownContent.GetChild(i).gameObject);
        }
    }

    private void BuildDropdownFallback(string message)
    {
        ClearDropdownContent();

        var row = CreateRowBase("", "");
        var label = row.transform.Find("Name")?.GetComponent<TextMeshProUGUI>();
        if (label != null)
        {
            label.text = message;
            label.alignment = TextAlignmentOptions.Center;
        }

        // Click yok
        var btn = row.GetComponent<Button>();
        if (btn != null) btn.interactable = false;
    }

    private void BuildDropdownNearest(double lat, double lon)
    {
        ClearDropdownContent();

        var ranked = _maps
            .Select(m => new { map = m, dist = HaversineMeters(lat, lon, m.latitude, m.longitude) })
            .OrderBy(x => x.dist)
            .ToList();

        if (ranked.Count == 0)
        {
            BuildDropdownFallback("Harita bulunamadı");
            return;
        }

        // Seçili map'i ilk sıraya sabitle
        var selected = ranked.FirstOrDefault(x => x.map.mapId == mapId);
        if (selected == null)
        {
            // Eğer mevcut mapId listede yoksa, en yakını seçili gibi göster
            selected = ranked[0];
        }

        // Top N listesi: önce selected, sonra en yakınlardan kalan
        var list = new List<(MapInfo map, double dist)>();
        list.Add((selected.map, selected.dist));

        foreach (var x in ranked)
        {
            if (list.Count >= Mathf.Max(1, dropdownTopN)) break;
            if (x.map.mapId == selected.map.mapId) continue;
            list.Add((x.map, x.dist));
        }

        // UI rows
        for (int i = 0; i < list.Count; i++)
        {
            bool isSelected = list[i].map.mapId == mapId;
            string nameLine = isSelected ? $"* {list[i].map.name}" : list[i].map.name;
            string distLine = FormatDistance(list[i].dist);
            if (list[i].dist > maxSelectableDistanceMeters)
                distLine += " (Too far)";

            var row = CreateRowBase(nameLine, distLine);

            // Click: map seç
            var btn = row.GetComponent<Button>();
            if (btn != null)
            {
                int id = list[i].map.mapId;
                string nm = list[i].map.name;
                double dist = list[i].dist;

                bool allowedByDistance = dist <= maxSelectableDistanceMeters;

                // If too far, prevent selection
                btn.interactable = allowedByDistance;

                if (allowedByDistance)
                {
                    btn.onClick.AddListener(() =>
                    {
                        SetMap(id, nm);
                        // Dropdown kapat
                        _dropdownOpen = false;
                        if (dropdownPanel != null) dropdownPanel.gameObject.SetActive(false);
                    });
                }
            }
        }
    }

    private GameObject CreateRowBase(string leftText, string rightText)
    {
        if (dropdownContent == null)
            dropdownContent = dropdownPanel;

        var rowGO = new GameObject("MapRow", typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup));
        rowGO.transform.SetParent(dropdownContent, worldPositionStays: false);

        var img = rowGO.GetComponent<Image>();
        img.color = new Color(1, 1, 1, 0.08f);
        img.raycastTarget = true;

        var hlg = rowGO.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(12, 12, 10, 10);
        hlg.spacing = 12;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlHeight = true;
        hlg.childControlWidth = true;
        hlg.childForceExpandHeight = false;
        hlg.childForceExpandWidth = true;

        var leftGO = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
        leftGO.transform.SetParent(rowGO.transform, worldPositionStays: false);
        var left = leftGO.GetComponent<TextMeshProUGUI>();
        CopyTmpStyle(left);
        left.text = leftText;
        left.alignment = TextAlignmentOptions.MidlineLeft;
        // Tek satır + "..." (ellipsis)
        left.overflowMode = TextOverflowModes.Ellipsis;

        // Name alanı kalan genişliği alsın
        var leftLE = leftGO.AddComponent<LayoutElement>();
        leftLE.flexibleWidth = 1;
        leftLE.minWidth = 0;

        var rightGO = new GameObject("Distance", typeof(RectTransform), typeof(TextMeshProUGUI));
        rightGO.transform.SetParent(rowGO.transform, worldPositionStays: false);
        var right = rightGO.GetComponent<TextMeshProUGUI>();
        CopyTmpStyle(right);
        right.text = rightText;
        right.alignment = TextAlignmentOptions.MidlineRight;

        // Sağ text daha dar olsun
        var rightLE = rightGO.AddComponent<LayoutElement>();
        rightLE.preferredWidth = 140;
        rightLE.flexibleWidth = 0;

        return rowGO;
    }

    private void CopyTmpStyle(TextMeshProUGUI t)
    {
        // Referans olarak üstteki label'ı kullan
        var refTmp = selectedMapNameTMP;
        if (refTmp != null)
        {
            t.font = refTmp.font;
            t.fontSize = refTmp.fontSize;
            t.color = refTmp.color;
        }
        else
        {
            t.fontSize = 32;
            t.color = Color.white;
        }

        // No-wrap (enableWordWrapping obsolete)
        t.textWrappingMode = TextWrappingModes.NoWrap;
    }

    // -------------------- Refresh / Auto-select --------------------

    private void OnClickRefreshNearest()
    {
        StartCoroutine(LoadMapsThenAutoSelect(initialRun: false));
    }

    private IEnumerator LoadMapsThenAutoSelect(bool initialRun)
    {
        // Initial run'da lockAfterAutoSelect true ise bir kere çalışsın; Refresh'de her zaman çalışsın
        if (initialRun && _autoSelectedOnce && lockAfterAutoSelect)
            yield break;

        // 1) Maps yükle
        if (_maps.Count == 0)
            yield return LoadMapsOnly();

        if (_maps.Count == 0)
            yield break;

        // 2) Lokasyon al
        yield return EnsureLocationRunning();

        if (!TryGetLastLocation(out double lat, out double lon))
        {
            Debug.LogWarning("[MapRootProvider] Refresh/AutoSelect: location unavailable.");
            yield break;
        }

        // 3) En yakını seç
        var best = FindNearest(_maps, lat, lon);
        if (best != null)
        {
            double bestDist = HaversineMeters(lat, lon, best.latitude, best.longitude);
            if (bestDist <= maxSelectableDistanceMeters)
            {
                _autoSelectedOnce = true;
                SetMap(best.mapId, best.name);
                Debug.Log($"[MapRootProvider] Auto-selected nearest map: {best} ({bestDist:F1}m) for location lat={lat}, lon={lon}");
            }
            else
            {
                Debug.LogWarning($"[MapRootProvider] Auto-select skipped: nearest map '{best.name}' (id={best.mapId}) is {bestDist:F1}m away (> {maxSelectableDistanceMeters:F0}m).");
            }

            // Dropdown açıksa listeyi de güncelle
            if (_dropdownOpen)
                BuildDropdownNearest(lat, lon);
        }

        if (lockAfterAutoSelect)
        {
            // Pil için kapat
            Input.location.Stop();
        }
    }

    private IEnumerator LoadMapsOnly()
    {
        // Firebase hazır olana kadar bekle
        float wait = 0f;
        while (!FirebaseInitializer.Ready && wait < 10f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!FirebaseInitializer.Ready || FirebaseInitializer.DB == null)
        {
            Debug.LogWarning("[MapRootProvider] Firebase not ready.");
            yield break;
        }

        bool loaded = false;
        Exception loadErr = null;

        FirebaseInitializer.DB.Child(mapsDbPath).GetValueAsync().ContinueWithOnMainThread(t =>
        {
            try
            {
                _maps.Clear();

                if (!t.IsCompletedSuccessfully || t.Result == null || !t.Result.Exists)
                {
                    loaded = true;
                    return;
                }

                foreach (var child in t.Result.Children)
                {
                    if (TryParseMap(child, out var info))
                        _maps.Add(info);
                }

                loaded = true;
            }
            catch (Exception e)
            {
                loadErr = e;
                loaded = true;
            }
        });

        while (!loaded)
            yield return null;

        if (loadErr != null)
        {
            Debug.LogWarning("[MapRootProvider] Map list load error: " + loadErr);
            yield break;
        }

        if (_maps.Count == 0)
        {
            Debug.LogWarning($"[MapRootProvider] No maps found at path '{mapsDbPath}'.");
            yield break;
        }

        Debug.Log($"[MapRootProvider] Loaded {_maps.Count} maps from DB '{mapsDbPath}'.");
    }

    private IEnumerator EnsureLocationRunning()
    {
        // Platform izinleri
        if (!Input.location.isEnabledByUser)
        {
            Debug.LogWarning("[MapRootProvider] Location not enabled by user (phone settings).");
            yield break;
        }

        // Zaten çalışıyorsa bekleme yapma
        if (Input.location.status == LocationServiceStatus.Running)
            yield break;

        Input.location.Start(desiredAccuracyMeters, updateDistanceMeters);

        float elapsed = 0f;
        while (Input.location.status == LocationServiceStatus.Initializing && elapsed < locationInitTimeoutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        if (Input.location.status != LocationServiceStatus.Running)
        {
            Debug.LogWarning($"[MapRootProvider] Location service not running (status={Input.location.status}).");
        }
    }

    private bool TryGetLastLocation(out double lat, out double lon)
    {
        lat = 0;
        lon = 0;

        if (Input.location.status != LocationServiceStatus.Running)
            return false;

        var d = Input.location.lastData;
        // lastData bazen 0 dönebiliyor, ama yine de kabul ediyoruz
        lat = d.latitude;
        lon = d.longitude;
        return true;
    }

    // -------------------- DB parsing helpers --------------------

    // Your DB schema:
    // /maps/{MAP_ID_KEY}/
    //    alt: number
    //    lat: number
    //    lon: number
    //    name: string
    private static bool TryParseMap(DataSnapshot s, out MapInfo info)
    {
        info = null;

        // Map id is the CHILD KEY (e.g. "136761")
        if (!int.TryParse(s.Key, out int id))
            return false;

        // Required fields: lat + lon
        if (!TryGetDouble(s, "lat", out double lat))
            return false;
        if (!TryGetDouble(s, "lon", out double lon))
            return false;

        // Optional
        _ = TryGetDouble(s, "alt", out double alt);
        string name = TryGetString(s, "name") ?? $"Map {id}";

        info = new MapInfo
        {
            mapId = id,
            name = name,
            latitude = lat,
            longitude = lon,
            alt = alt
        };

        return true;
    }

    private static bool TryGetDouble(DataSnapshot s, string key, out double value)
    {
        value = 0;
        if (!s.Child(key).Exists || s.Child(key).Value == null) return false;
        try
        {
            // Realtime DB bazen long/double/string dönebiliyor
            if (s.Child(key).Value is double d) { value = d; return true; }
            if (s.Child(key).Value is float f) { value = f; return true; }
            if (s.Child(key).Value is long l) { value = l; return true; }
            if (s.Child(key).Value is int i) { value = i; return true; }
            value = Convert.ToDouble(s.Child(key).Value);
            return true;
        }
        catch { return false; }
    }

    private static string TryGetString(DataSnapshot s, string key)
    {
        if (!s.Child(key).Exists || s.Child(key).Value == null) return null;
        return s.Child(key).Value.ToString();
    }

    // -------------------- Nearest-map math --------------------

    private MapInfo FindNearest(List<MapInfo> maps, double lat, double lon)
    {
        MapInfo best = null;
        double bestDist = double.MaxValue;

        foreach (var m in maps)
        {
            if (m == null) continue;
            double d = HaversineMeters(lat, lon, m.latitude, m.longitude);
            if (d < bestDist)
            {
                bestDist = d;
                best = m;
            }
        }

        if (best == null)
            return null;

        // Respect the distance gate
        if (bestDist > maxSelectableDistanceMeters)
            return null;

        return best;
    }

    private static string FormatDistance(double meters)
    {
        if (meters < 1000)
            return $"{Mathf.RoundToInt((float)meters)} m";

        return $"{meters / 1000.0:F1} km";
    }

    // Haversine distance in meters
    private static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0; // meters
        double dLat = Deg2Rad(lat2 - lat1);
        double dLon = Deg2Rad(lon2 - lon1);

        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                   Math.Cos(Deg2Rad(lat1)) * Math.Cos(Deg2Rad(lat2)) *
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double Deg2Rad(double deg) => deg * (Math.PI / 180.0);

    // -------------------- UI --------------------

    public void RefreshSelectedMapUI()
    {
        string label = string.IsNullOrEmpty(mapName) ? $"{uiPrefix}{mapId}" : $"{uiPrefix}{mapName}";

        if (selectedMapNameText != null)
            selectedMapNameText.text = label;

        if (selectedMapNameTMP != null)
            selectedMapNameTMP.text = label;
    }

    private void RefreshLocalizationIndicator()
    {
        if (localizedIndicatorTMP == null)
            return;

        // Always show 'O'
        localizedIndicatorTMP.text = "O";

        // Green if localized, red otherwise
        localizedIndicatorTMP.color = IsLocalized ? localizedColor : notLocalizedColor;
    }
}