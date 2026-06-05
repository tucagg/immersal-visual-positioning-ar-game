using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Firebase.Database;
using Firebase.Extensions;
using Immersal.XR;
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
    [Tooltip("Immersal scan ID used for AR localization. Multiple maps (different SelectedMapDbKey values) can share this ID when they are in the same physical room.")]
    public int immersalMapId = 0;

    [Tooltip("Seçili haritanın adı (DB'den gelir)")]
    public string mapName = "";

    [Tooltip("Selected Firebase map record id. This is the app map id used for anchors/clues path, not the Immersal id.")]
    public string SelectedMapDbKey { get; private set; } = "";

    [Tooltip("Localize olunduğunda true yapın (Immersal otomatik yapıyorsa Inspector'dan işaretlemeye gerek yok)")]
    public bool localized = false;


    public bool IsLocalized => mapRoot && localized;

    [Header("Embedded XR Map Objects")]
    [Tooltip("If true, only the XR Map GameObject matching the selected Immersal mapId stays active. Other XR Map objects under mapRoot are disabled.")]
    public bool manageEmbeddedXRMaps = true;

    [Tooltip("XR Map GameObjects usually start with this name, e.g. 'XR Map 138050-okul2'.")]
    public string xrMapObjectNamePrefix = "XR Map";

    [Header("Map Selection Distance Gate")]
    [Tooltip("A map can only be selected if the user is within this distance (meters).")]
    public float maxSelectableDistanceMeters = 30f;

    [Header("Auto-select nearest map")]
    [Tooltip("Konuma göre en yakın haritayı otomatik seç")]
    public bool autoSelectNearestMap = true;

    [Tooltip("Firebase DB'de map listesinin path'i (örn: maps)")]
    public string mapsDbPath = "maps";

    [Header("Public AR Map Filter")]
    [Tooltip("When true, public AR map list / nearest-map auto select only uses maps with approvalStatus == approved.")]
    public bool onlyApprovedMapsForPublicAR = true;

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

    [Tooltip("Second selected map label for another MapBar, e.g. Screen_EditClueLocAR.")]
    public Text selectedMapNameText2;

    [Tooltip("Second selected map TMP label for another MapBar, e.g. Screen_EditClueLocAR.")]
    public TMP_Text selectedMapNameTMP2;

    [Header("UI - Localization Indicator")]
    [Tooltip("Map bar yanında gösterilen 'O' localization indicator (TMP_Text)")]
    public TMP_Text localizedIndicatorTMP;

    [Tooltip("Second localization indicator TMP for another MapBar, e.g. Screen_EditClueLocAR.")]
    public TMP_Text localizedIndicatorTMP2;

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
        public string dbKey;        // Firebase /maps/{dbKey}; unique record id per map
        public int immersalMapId;   // Immersal scan ID — shared by all maps in the same room
        public string name;
        public double latitude;
        public double longitude;
        public double alt;
        public int requiredLevel;   // Minimum player level required to play this map (0 = no restriction)

        public override string ToString() => $"{name} (immersalId={immersalMapId}, dbKey={dbKey}) @ {latitude},{longitude}";
    }

    [Header("Pose Smoothing")]
    [Tooltip("Smooth out jitter when Immersal re-localizes. Prevents objects from snapping on fast camera moves.")]
    public bool smoothPose = true;
    [Tooltip("How fast the AR space catches up to each new localization result. Higher = snappier, lower = smoother.")]
    [Range(1f, 30f)]
    public float poseLerpSpeed = 8f;

    public event Action<int, string> OnMapSelected;

    private bool _autoSelectedOnce = false;
    private bool _dropdownOpen = false;
    private bool _editClueLocARMapLocked = false;

    // Admin bypass — when true, level gate and completed gate are skipped.
    public bool isAdminMode = false;

    // Pose smoothing state
    private Pose   _targetPose;
    private bool   _hasTargetPose    = false;
    private bool   _firstLocalization = true;

    // Localization timeout — if no successful localization event arrives within this
    // many seconds, the indicator reverts to red. 0 = disabled.
    [Header("Localization Timeout")]
    [Tooltip("Seconds without a successful localization before the indicator turns red. 0 = never timeout.")]
    public float localizationTimeoutSeconds = 5f;
    private float _lastLocalizationTime = -1f;

    // Player level cache — loaded from Firebase on Start, used for map level gate.
    private int _playerLevel = 1;

    // Completed map keys for the current user — refreshed each time the dropdown opens.
    private readonly HashSet<string> _completedMapKeys = new();

    // Immersal Localizer subscription for continuous re-localization support.
    private Localizer _immersalLocalizer;

    private readonly List<MapInfo> _maps = new();

    private bool TryGetMapInfo(int id, out MapInfo info)
    {
        info = null;
        for (int i = 0; i < _maps.Count; i++)
        {
            var m = _maps[i];
            if (m != null && m.immersalMapId == id)
            {
                info = m;
                return true;
            }
        }
        return false;
    }

    private string ResolveMapDbKey(int targetMapId, string targetMapName)
    {
        MapInfo matched = null;

        if (_maps != null && _maps.Count > 0)
        {
            // 1) Exact name + immersalMapId match — most specific, use this when available.
            if (!string.IsNullOrEmpty(targetMapName))
            {
                matched = _maps.FirstOrDefault(m =>
                    m != null &&
                    m.immersalMapId == targetMapId &&
                    string.Equals(m.name, targetMapName, StringComparison.OrdinalIgnoreCase));
            }

            // 2) Prefer keeping the current SelectedMapDbKey if it belongs to a map with this
            //    immersalMapId. This prevents accidentally switching between two maps that share
            //    the same immersalMapId (same physical room / "parallel worlds" scenario).
            if (matched == null && !string.IsNullOrEmpty(SelectedMapDbKey))
            {
                matched = _maps.FirstOrDefault(m =>
                    m != null &&
                    m.immersalMapId == targetMapId &&
                    string.Equals(m.dbKey, SelectedMapDbKey, StringComparison.OrdinalIgnoreCase));
            }

            // 3) Last resort: first map with this immersalMapId.
            //    WARNING: If multiple maps share the same immersalMapId, this is ambiguous.
            //    Prefer calling SetMapByDbKey (which bypasses this resolution) whenever the
            //    caller already has the dbKey (e.g. auto-select, dropdown).
            if (matched == null)
                matched = _maps.FirstOrDefault(m => m != null && m.immersalMapId == targetMapId);
        }

        if (matched != null && !string.IsNullOrEmpty(matched.dbKey))
            return matched.dbKey;

        if (!string.IsNullOrEmpty(SelectedMapDbKey))
            return SelectedMapDbKey;

        return targetMapId.ToString(); // legacy fallback only
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

    void Update()
    {
        if (smoothPose && _hasTargetPose && mapRoot != null)
        {
            mapRoot.position = Vector3.Lerp(
                mapRoot.position, _targetPose.position, Time.deltaTime * poseLerpSpeed);
            mapRoot.rotation = Quaternion.Slerp(
                mapRoot.rotation, _targetPose.rotation, Time.deltaTime * poseLerpSpeed);
        }

        // Localization timeout: if no successful localization arrived recently, go red.
        if (localized && localizationTimeoutSeconds > 0f && _lastLocalizationTime >= 0f)
        {
            if (Time.time - _lastLocalizationTime > localizationTimeoutSeconds)
            {
                localized = false;
                RefreshLocalizationIndicator();
                Debug.Log("[MapRootProvider] Localization timed out — indicator set to red.");
            }
        }
    }

    void Start()
    {
        EnsureDropdownObjects();
        SubscribeToImmersalLocalizer();

        // If we reloaded due to a map change, force the selected map exactly once.
        // Important: apply it after maps are loaded so SelectedMapDbKey resolves correctly.
        bool hasForcedMap = PlayerPrefs.GetInt(PREFS_FORCE_MAP_ONCE, 0) == 1;
        if (hasForcedMap)
        {
            autoSelectNearestMap = false;
            StartCoroutine(ApplyForcedMapAfterLoad());
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
        ApplySelectedXRMapVisibility();

        LoadPlayerLevel();

        if (autoSelectNearestMap)
        {
            StartCoroutine(LoadMapsThenAutoSelect(initialRun: true));
        }
    }

    private void OnDestroy()
    {
        UnsubscribeFromImmersalLocalizer();
    }

    // Subscribe to Immersal's continuous localization event so localized=true is maintained
    // across screen changes and correctly re-set after map switches (without toggling arRuntimeRoot).
    private void SubscribeToImmersalLocalizer()
    {
        // Include inactive — arRuntimeRoot may be inactive on first Start().
        _immersalLocalizer = FindFirstObjectByType<Localizer>(FindObjectsInactive.Include);
        if (_immersalLocalizer != null)
        {
            _immersalLocalizer.OnSuccessfulLocalizations.RemoveListener(OnImmersalSuccessfulLocalizations);
            _immersalLocalizer.OnSuccessfulLocalizations.AddListener(OnImmersalSuccessfulLocalizations);
            Debug.Log("[MapRootProvider] Subscribed to Immersal Localizer.OnSuccessfulLocalizations.");
        }
        else
        {
            Debug.LogWarning("[MapRootProvider] Immersal Localizer not found. Localization state won't auto-update after first localize.");
        }
    }

    private void UnsubscribeFromImmersalLocalizer()
    {
        if (_immersalLocalizer != null)
            _immersalLocalizer.OnSuccessfulLocalizations.RemoveListener(OnImmersalSuccessfulLocalizations);
    }

    // Called by Immersal every time ANY map is successfully localized (not just the first time).
    // This keeps localized=true alive across screen changes and after map switches.
    private void OnImmersalSuccessfulLocalizations(int[] mapIds)
    {
        if (mapIds == null || mapIds.Length == 0) return;

        // No map selected — do not accept any localization.
        if (immersalMapId <= 0) return;

        bool matches = Array.IndexOf(mapIds, immersalMapId) >= 0;
        if (!matches) return;

        _lastLocalizationTime = Time.time; // reset timeout on every successful localization
        if (!localized)
        {
            localized = true;
            RefreshLocalizationIndicator();
            Debug.Log($"[MapRootProvider] Re-localized (continuous event). immersalMapId={immersalMapId}");
        }
    }

    // (Opsiyonel) Immersal callback'inden çağırırsan otomatik günceller:

    private IEnumerator ApplyForcedMapAfterLoad()
    {
        if (PlayerPrefs.GetInt(PREFS_FORCE_MAP_ONCE, 0) != 1)
            yield break;

        int forcedId = PlayerPrefs.GetInt(PREFS_SELECTED_MAP_ID, immersalMapId);
        string forcedName = PlayerPrefs.GetString(PREFS_SELECTED_MAP_NAME, "");

        // Load maps first so ResolveMapDbKey can find the Firebase record id.
        if (_maps.Count == 0)
            yield return LoadMapsOnly();

        SetMap(forcedId, forcedName);

        PlayerPrefs.SetInt(PREFS_FORCE_MAP_ONCE, 0);
        PlayerPrefs.Save();

        Debug.Log($"[MapRootProvider] Forced map applied after load: immersalId={forcedId}, name='{forcedName}', dbKey='{SelectedMapDbKey}'");
    }
    public void OnLocalized(Pose worldPose, int localizedMapId)
    {
        // No map selected — reject all localization.
        if (immersalMapId <= 0) return;

        // If a different map was localized, ignore it — don't kill our localization state.
        if (localizedMapId != immersalMapId)
        {
            Debug.LogWarning($"[MapRootProvider] OnLocalized: localizedMapId={localizedMapId} doesn't match immersalMapId={immersalMapId}. Ignoring (not resetting localized).");
            return;
        }

        if (mapRoot != null)
        {
            if (smoothPose && !_firstLocalization)
            {
                // Store as target — Update() will lerp towards it smoothly.
                _targetPose    = worldPose;
                _hasTargetPose = true;
            }
            else
            {
                // First localization: snap immediately so objects appear in the right place.
                mapRoot.SetPositionAndRotation(worldPose.position, worldPose.rotation);
                _targetPose      = worldPose;
                _hasTargetPose   = true;
                _firstLocalization = false;
            }
        }

        localized = true;
        _lastLocalizationTime = Time.time;
        RefreshLocalizationIndicator();
        Debug.Log($"[MapRootProvider] OnLocalized -> immersalMapId={immersalMapId}, pos={worldPose.position}");
    }

    // NOTE: Do NOT assign this to any Inspector UnityEvent — it has no map ID check.
    // Localization is managed exclusively through OnImmersalSuccessfulLocalizations (code subscription).
    [System.Obsolete("Do not use from Inspector events. Use OnImmersalSuccessfulLocalizations via code subscription instead.")]
    public void MarkLocalized()
    {
        Debug.LogWarning("[MapRootProvider] MarkLocalized() called — this method should not be wired to Inspector events. Remove it from Immersal Localizer's event listeners.");
    }

    public void ResetLocalization()
    {
        localized             = false;
        _hasTargetPose        = false;
        _firstLocalization    = true;
        _lastLocalizationTime = -1f;
        RefreshLocalizationIndicator();
        Debug.Log("[MapRootProvider] ResetLocalization()");
    }
    public void SetEditClueLocARMapLock(bool locked)
    {
        _editClueLocARMapLocked = locked;

        if (locked)
        {
            _autoSelectedOnce = true;
            RefreshSelectedMapUI();
            RefreshLocalizationIndicator();
            ApplySelectedXRMapVisibility();

            Debug.Log($"[MapRootProvider] EditClueLocAR map lock ON. Locked immersalMapId={immersalMapId}, dbKey='{SelectedMapDbKey}', name='{mapName}'");
        }
        else
        {
            _autoSelectedOnce = false;
            Debug.Log("[MapRootProvider] EditClueLocAR map lock OFF.");
        }
    }

    /// <summary>
    /// Selects a map using its unique Firebase record key (dbKey). Prefer this over SetMap()
    /// whenever the dbKey is available — it bypasses ResolveMapDbKey entirely, so two maps
    /// that share the same immersalMapId ("parallel worlds" in the same room) are handled
    /// correctly without ambiguity.
    /// </summary>
    public void SetMapByDbKey(string mapDbKey, int newImmersalMapId, string newMapName = "")
    {
        if (string.IsNullOrEmpty(mapDbKey))
        {
            Debug.LogWarning("[MapRootProvider] SetMapByDbKey: mapDbKey is empty, ignoring.");
            return;
        }

        if (_editClueLocARMapLocked && newImmersalMapId != immersalMapId)
        {
            Debug.LogWarning($"[MapRootProvider] SetMapByDbKey blocked: EditClueLocAR map lock is active. Current={immersalMapId}, Requested={newImmersalMapId}");
            RefreshSelectedMapUI();
            ApplySelectedXRMapVisibility();
            return;
        }

        // Same map — nothing to do except refresh UI labels.
        bool sameMap = string.Equals(SelectedMapDbKey, mapDbKey, StringComparison.OrdinalIgnoreCase)
                    && newImmersalMapId == immersalMapId;
        if (sameMap)
        {
            if (!string.IsNullOrEmpty(newMapName))
                mapName = newMapName;
            RefreshSelectedMapUI();
            ApplySelectedXRMapVisibility();
            Debug.Log($"[MapRootProvider] SetMapByDbKey ignored (same map): dbKey='{SelectedMapDbKey}', immersalMapId={immersalMapId}");
            return;
        }

        // Level gate: block if map requires a higher level than the player has (admin bypasses).
        int reqLevel = GetMapRequiredLevel(mapDbKey);
        if (!isAdminMode && reqLevel > _playerLevel)
        {
            Debug.LogWarning($"[MapRootProvider] SetMapByDbKey blocked: map '{newMapName}' requires level {reqLevel}, player is level {_playerLevel}.");
            RefreshSelectedMapUI();
            return;
        }

        // Completed gate: block if the player has already finished this map (admin bypasses).
        if (!isAdminMode && _completedMapKeys.Contains(mapDbKey))
        {
            Debug.LogWarning($"[MapRootProvider] SetMapByDbKey blocked: map '{newMapName}' already completed by this player.");
            RefreshSelectedMapUI();
            return;
        }

        immersalMapId = newImmersalMapId;
        SelectedMapDbKey = mapDbKey;
        if (!string.IsNullOrEmpty(newMapName))
            mapName = newMapName;

        // Map changed — reset localization so TrySubscribeOnce re-runs for the new map.
        localized          = false;
        _hasTargetPose     = false;
        _firstLocalization = true;
        RefreshLocalizationIndicator();
        RefreshSelectedMapUI();
        ApplySelectedXRMapVisibility();
        OnMapSelected?.Invoke(immersalMapId, mapName);

        Debug.Log($"[MapRootProvider] SetMapByDbKey -> immersalMapId={immersalMapId}, dbKey='{SelectedMapDbKey}', name='{mapName}'");
    }

    /// <summary>
    /// Elle map seçmek istersen UI'dan çağır.
    /// </summary>
    public void SetMap(int newMapId, string newMapName = "")
    {
        if (_editClueLocARMapLocked && newMapId != immersalMapId)
        {
            Debug.LogWarning($"[MapRootProvider] SetMap blocked while EditClueLocAR map lock is active. Current immersalMapId={immersalMapId}, Requested={newMapId}");
            RefreshSelectedMapUI();
            ApplySelectedXRMapVisibility();
            return;
        }

        string resolvedDbKey = ResolveMapDbKey(newMapId, newMapName);
        bool sameDbKey = string.Equals(SelectedMapDbKey, resolvedDbKey, StringComparison.OrdinalIgnoreCase);

        // If selecting the same map again, don't reset localization or re-fire events.
        if (newMapId == immersalMapId && sameDbKey)
        {
            if (!string.IsNullOrEmpty(newMapName))
                mapName = newMapName;

            SelectedMapDbKey = resolvedDbKey;
            RefreshSelectedMapUI();
            ApplySelectedXRMapVisibility();
            Debug.Log($"[MapRootProvider] SetMap ignored (same map): immersalMapId={immersalMapId}, dbKey='{SelectedMapDbKey}', name='{mapName}'");
            return;
        }

        // Distance gate: if we can compute distance and it's too far, do not allow selecting this map.
        if (TryGetDistanceToMap(newMapId, out double distMeters))
        {
            if (distMeters > maxSelectableDistanceMeters)
            {
                Debug.LogWarning($"[MapRootProvider] SetMap blocked: immersalMapId={newMapId} is {distMeters:F1}m away (> {maxSelectableDistanceMeters:F0}m). Selection ignored.");
                RefreshSelectedMapUI();
                return;
            }
        }

        // Level gate: block if map requires a higher level than the player has (admin bypasses).
        int reqLvl = GetMapRequiredLevelByImmersalId(newMapId, resolvedDbKey);
        if (!isAdminMode && reqLvl > _playerLevel)
        {
            Debug.LogWarning($"[MapRootProvider] SetMap blocked: map '{newMapName}' (immersalId={newMapId}) requires level {reqLvl}, player is level {_playerLevel}.");
            RefreshSelectedMapUI();
            return;
        }

        immersalMapId = newMapId;
        SelectedMapDbKey = resolvedDbKey;
        if (!string.IsNullOrEmpty(newMapName))
            mapName = newMapName;

        // Map değişince localization state sıfırlansın
        localized = false;
        RefreshLocalizationIndicator();

        RefreshSelectedMapUI();
        ApplySelectedXRMapVisibility();
        OnMapSelected?.Invoke(immersalMapId, mapName);

        Debug.Log($"[MapRootProvider] SetMap -> immersalMapId={immersalMapId}, dbKey='{SelectedMapDbKey}', name='{mapName}'");
    }


    // ── Player level ─────────────────────────────────────────────────────────

    private void LoadPlayerLevel()
    {
        try
        {
            var user = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser;
            if (user == null) return;

            FirebaseInitializer.DB
                .Child("users").Child(user.UserId).Child("level")
                .GetValueAsync()
                .ContinueWithOnMainThread(t =>
                {
                    if (t.IsCompletedSuccessfully && t.Result != null && t.Result.Exists && t.Result.Value != null)
                    {
                        if (int.TryParse(t.Result.Value.ToString(), out int lvl) && lvl > 0)
                            _playerLevel = lvl;
                    }
                });
        }
        catch (Exception e)
        {
            Debug.LogWarning("[MapRootProvider] LoadPlayerLevel error: " + e.Message);
        }
    }

    // Returns the requiredLevel for a given dbKey by looking it up in the cached _maps list.
    private int GetMapRequiredLevel(string dbKey)
    {
        if (string.IsNullOrEmpty(dbKey)) return 0;
        foreach (var m in _maps)
        {
            if (m != null && string.Equals(m.dbKey, dbKey, StringComparison.OrdinalIgnoreCase))
                return m.requiredLevel;
        }
        return 0; // Unknown map — no restriction
    }

    private int GetMapRequiredLevelByImmersalId(int immersalId, string resolvedDbKey)
    {
        // Prefer exact dbKey match when available (avoids "parallel worlds" ambiguity)
        int lvl = GetMapRequiredLevel(resolvedDbKey);
        if (lvl > 0) return lvl;

        // Fall back to first matching immersalId
        foreach (var m in _maps)
        {
            if (m != null && m.immersalMapId == immersalId)
                return m.requiredLevel;
        }
        return 0;
    }

    private void ApplySelectedXRMapVisibility()
    {
        if (!manageEmbeddedXRMaps || mapRoot == null)
            return;

        // No map selected — deactivate all XR Map objects so Immersal doesn't scan anything.
        if (immersalMapId <= 0)
        {
            for (int i = 0; i < mapRoot.childCount; i++)
            {
                Transform child = mapRoot.GetChild(i);
                if (child == null) continue;
                string childName = child.name ?? "";
                if (childName.StartsWith(xrMapObjectNamePrefix, StringComparison.OrdinalIgnoreCase))
                    child.gameObject.SetActive(false);
            }
            return;
        }

        string selectedIdText = immersalMapId.ToString();
        bool foundMatch = false;

        for (int i = 0; i < mapRoot.childCount; i++)
        {
            Transform child = mapRoot.GetChild(i);
            if (child == null)
                continue;

            string childName = child.name ?? "";

            // Only manage actual XR Map objects. Do not touch PoseFilter, PoseSmoother, etc.
            if (!childName.StartsWith(xrMapObjectNamePrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            bool isSelectedMapObject = childName.Contains(selectedIdText);
            child.gameObject.SetActive(isSelectedMapObject);

            if (isSelectedMapObject)
                foundMatch = true;
        }

        if (!foundMatch)
        {
            Debug.LogWarning($"[MapRootProvider] No embedded XR Map GameObject found for immersalMapId={immersalMapId}. Expected a child name like '{xrMapObjectNamePrefix} {immersalMapId}-...'.");
        }
        else
        {
            Debug.Log($"[MapRootProvider] Embedded XR Map visibility applied. Active immersalMapId={immersalMapId}");
        }
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
        ToggleDropdownFor(ref _dropdownOpen, dropdownPanel, dropdownContent);
    }


    private void ToggleDropdownFor(ref bool isOpen, RectTransform targetPanel, RectTransform targetContent)
    {
        if (targetPanel == null)
            return;

        isOpen = !isOpen;
        targetPanel.gameObject.SetActive(isOpen);

        if (isOpen)
            StartCoroutine(RebuildDropdownList(targetPanel, targetContent));
    }

    private IEnumerator RebuildDropdownList(RectTransform targetPanel, RectTransform targetContent)
    {
        // Maps yoksa önce yükle
        if (_maps.Count == 0)
            yield return LoadMapsOnly();

        // Completion verisi yükle (her açılışta taze)
        yield return LoadCompletedMapKeys();

        // Konumu al
        if (!TryGetLastLocation(out double lat, out double lon))
        {
            // Eğer konum yoksa başlatıp almayı dene
            yield return EnsureLocationRunning();
            if (!TryGetLastLocation(out lat, out lon))
            {
                BuildDropdownFallback("Konum alınamadı", targetContent);
                yield break;
            }
        }

        BuildDropdownNearest(lat, lon, targetPanel, targetContent);
    }

    private IEnumerator LoadCompletedMapKeys()
    {
        _completedMapKeys.Clear();

        string uid = "";
        try
        {
            var user = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser;
            if (user != null) uid = user.UserId;
        }
        catch { }

        if (string.IsNullOrEmpty(uid) || FirebaseInitializer.DB == null)
            yield break;

        bool done = false;

        FirebaseInitializer.DB
            .Child("users").Child(uid).Child("progress")
            .GetValueAsync()
            .ContinueWithOnMainThread(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result != null && t.Result.Exists)
                {
                    foreach (var child in t.Result.Children)
                    {
                        var flag = child.Child("mapCompleted");
                        if (!flag.Exists || flag.Value == null) continue;

                        bool isCompleted = false;
                        if (!bool.TryParse(flag.Value.ToString(), out isCompleted))
                            isCompleted = flag.Value.ToString() == "True";

                        if (isCompleted)
                            _completedMapKeys.Add(child.Key);
                    }
                }
                done = true;
            });

        while (!done) yield return null;
    }

    private void ClearDropdownContent(RectTransform targetContent)
    {
        if (targetContent == null) return;
        for (int i = targetContent.childCount - 1; i >= 0; i--)
        {
            Destroy(targetContent.GetChild(i).gameObject);
        }
    }

    private void BuildDropdownFallback(string message, RectTransform targetContent)
    {
        ClearDropdownContent(targetContent);

        var row = CreateRowBase("", "", targetContent);
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

    private void BuildDropdownNearest(double lat, double lon, RectTransform targetPanel, RectTransform targetContent)
    {
        ClearDropdownContent(targetContent);

        var ranked = _maps
            .Select(m => new { map = m, dist = HaversineMeters(lat, lon, m.latitude, m.longitude) })
            .OrderBy(x => x.dist)
            .ToList();

        if (ranked.Count == 0)
        {
            BuildDropdownFallback("Harita bulunamadı", targetContent);
            return;
        }

        // Pin the currently selected map at the top — compare by unique dbKey, not immersalMapId,
        // so two maps that share the same immersalMapId are treated as distinct entries.
        var selected = ranked.FirstOrDefault(x =>
            string.Equals(x.map.dbKey, SelectedMapDbKey, StringComparison.OrdinalIgnoreCase));
        if (selected == null)
            selected = ranked[0];

        // Top N list: selected first, then nearest others.
        // Deduplicate by dbKey so each Firebase map record appears exactly once.
        var list = new List<(MapInfo map, double dist)>();
        list.Add((selected.map, selected.dist));

        foreach (var x in ranked)
        {
            if (list.Count >= Mathf.Max(1, dropdownTopN)) break;
            if (string.Equals(x.map.dbKey, selected.map.dbKey, StringComparison.OrdinalIgnoreCase)) continue;
            list.Add((x.map, x.dist));
        }

        // UI rows
        for (int i = 0; i < list.Count; i++)
        {
            // Compare by dbKey — two maps with the same immersalMapId are different entries.
            bool isSelected = string.Equals(list[i].map.dbKey, SelectedMapDbKey, StringComparison.OrdinalIgnoreCase);
            string nameLine = isSelected ? $"* {list[i].map.name}" : list[i].map.name;
            string distLine = FormatDistance(list[i].dist);

            bool allowedByDistance = list[i].dist <= maxSelectableDistanceMeters;
            bool allowedByLevel    = isAdminMode || list[i].map.requiredLevel <= _playerLevel;
            bool notCompleted      = isAdminMode || !_completedMapKeys.Contains(list[i].map.dbKey);

            if (!allowedByDistance) distLine += " (Too far)";
            if (!allowedByLevel)    distLine += $" (Lv. {list[i].map.requiredLevel} required)";
            if (!notCompleted)      distLine += " (Completed)";

            var row = CreateRowBase(nameLine, distLine, targetContent);

            // Click: map seç
            var btn = row.GetComponent<Button>();
            if (btn != null)
            {
                // Capture all three fields — use SetMapByDbKey so the exact dbKey is applied
                // without going through ResolveMapDbKey (which could pick the wrong map when
                // two maps share the same immersalMapId).
                string capturedDbKey    = list[i].map.dbKey;
                int capturedImmersalId  = list[i].map.immersalMapId;
                string capturedName     = list[i].map.name;

                bool allowed = allowedByDistance && allowedByLevel && notCompleted;
                btn.interactable = allowed;

                if (allowed)
                {
                    btn.onClick.AddListener(() =>
                    {
                        SetMapByDbKey(capturedDbKey, capturedImmersalId, capturedName);
                        // Dropdown kapat
                        if (targetPanel != null) targetPanel.gameObject.SetActive(false);
                        if (targetPanel == dropdownPanel) _dropdownOpen = false;
                    });
                }
            }
        }
    }

    private GameObject CreateRowBase(string leftText, string rightText, RectTransform targetContent)
    {
        if (targetContent == null)
            targetContent = dropdownContent != null ? dropdownContent : dropdownPanel;

        var rowGO = new GameObject("MapRow", typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup));
        rowGO.transform.SetParent(targetContent, worldPositionStays: false);

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
        if (_editClueLocARMapLocked)
        {
            Debug.Log("[MapRootProvider] Refresh nearest ignored while EditClueLocAR map lock is active.");
            return;
        }
        StartCoroutine(LoadMapsThenAutoSelect(initialRun: false));
    }

    private IEnumerator LoadMapsThenAutoSelect(bool initialRun)
    {
        if (_editClueLocARMapLocked)
            yield break;
        // Initial run'da lockAfterAutoSelect true ise bir kere çalışsın; Refresh'de her zaman çalışsın
        if (initialRun && _autoSelectedOnce && lockAfterAutoSelect)
            yield break;

        // 1) Maps yükle — refresh'de her zaman yeniden çek, initial run'da sadece boşsa çek.
        if (!initialRun) _maps.Clear();
        if (_maps.Count == 0)
            yield return LoadMapsOnly();

        if (_maps.Count == 0)
            yield break;

        // 1b) Completed keys yükle — FindNearest'in doğru filtreleyebilmesi için gerekli.
        yield return LoadCompletedMapKeys();

        // 2) Lokasyon al
        yield return EnsureLocationRunning();

        if (!TryGetLastLocation(out double lat, out double lon))
        {
            Debug.LogWarning("[MapRootProvider] Refresh/AutoSelect: location unavailable.");
            yield break;
        }

        // 3) En yakını seç
        var best = FindNearest(_maps, lat, lon);
        if (best == null)
        {
            // No selectable map — clear any stale localization state and deactivate all XR Maps.
            immersalMapId = 0;
            SelectedMapDbKey = "";
            localized = false;
            RefreshLocalizationIndicator();
            ApplySelectedXRMapVisibility();
            RefreshSelectedMapUI();
            yield break;
        }
        if (best != null)
        {
            double bestDist = HaversineMeters(lat, lon, best.latitude, best.longitude);
            if (bestDist <= maxSelectableDistanceMeters)
            {
                _autoSelectedOnce = true;
                // Use SetMapByDbKey — not SetMap — so that when two maps share the same
                // immersalMapId (same physical room, different experiences), the unique dbKey
                // is used directly without going through the ambiguous ResolveMapDbKey.
                SetMapByDbKey(best.dbKey, best.immersalMapId, best.name);
                Debug.Log($"[MapRootProvider] Auto-selected nearest map: {best} ({bestDist:F1}m) for location lat={lat}, lon={lon}");
            }
            else
            {
                Debug.LogWarning($"[MapRootProvider] Auto-select skipped: nearest map '{best.name}' (immersalId={best.immersalMapId}) is {bestDist:F1}m away (> {maxSelectableDistanceMeters:F0}m).");
            }

            // Dropdown açıksa listeyi de güncelle
            if (_dropdownOpen)
                BuildDropdownNearest(lat, lon, dropdownPanel, dropdownContent);
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
                    // Public AR uses only approved maps, but forced map selection is used by owner/admin edit flows.
                    // Forced map must be loadable even if it is pending.
                    if (onlyApprovedMapsForPublicAR && PlayerPrefs.GetInt(PREFS_FORCE_MAP_ONCE, 0) != 1 && !IsApprovedMap(child))
                        continue;

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

    public IEnumerator EnsureLocationRunning()
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

    public bool TryGetLastLocation(out double lat, out double lon)
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

    // Current DB schema:
    // /maps/{FIREBASE_RECORD_ID}/
    //    immersalMapId: number/string   // Immersal map id used by AR localization
    //    alt: number
    //    lat: number
    //    lon: number
    //    name: string                   // UI display name
    //
    // Legacy schema fallback:
    // /maps/{IMMERSAL_MAP_ID}/ ...
    private static bool IsApprovedMap(DataSnapshot s)
    {
        string status = TryGetString(s, "approvalStatus") ?? "";
        return string.Equals(status, "approved", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryParseMap(DataSnapshot s, out MapInfo info)
    {
        info = null;

        // Map id is now stored as immersalMapId. The Firebase child key is only our DB record id.
        // Fallback to s.Key keeps older map records working.
        int id;
        string immersalIdRaw = TryGetString(s, "immersalMapId");
        if (!int.TryParse(immersalIdRaw, out id))
        {
            if (!int.TryParse(s.Key, out id))
                return false;
        }

        // Required fields: lat + lon
        if (!TryGetDouble(s, "lat", out double lat))
            return false;
        if (!TryGetDouble(s, "lon", out double lon))
            return false;

        // Optional
        _ = TryGetDouble(s, "alt", out double alt);
        string name = TryGetString(s, "name") ?? $"Map {id}";

        int requiredLevel = 0;
        string reqLvlRaw = TryGetString(s, "level") ?? TryGetString(s, "requiredLevel");
        if (!string.IsNullOrEmpty(reqLvlRaw))
            int.TryParse(reqLvlRaw, out requiredLevel);

        info = new MapInfo
        {
            dbKey = s.Key,
            immersalMapId = id,
            name = name,
            latitude = lat,
            longitude = lon,
            alt = alt,
            requiredLevel = requiredLevel
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

            // Skip completed maps (unless admin).
            if (!isAdminMode && _completedMapKeys.Contains(m.dbKey)) continue;

            // Skip level-locked maps (unless admin).
            if (!isAdminMode && m.requiredLevel > _playerLevel) continue;

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
                   Math.Sin(dLon / 2) * Math.Sin(dLon / 2); double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double Deg2Rad(double deg) => deg * (Math.PI / 180.0);

    // -------------------- UI --------------------

    public void RefreshSelectedMapUI()
    {
        string label = string.IsNullOrEmpty(mapName) ? $"{uiPrefix}{immersalMapId}" : $"{uiPrefix}{mapName}";

        if (selectedMapNameText != null)
            selectedMapNameText.text = label;

        if (selectedMapNameTMP != null)
            selectedMapNameTMP.text = label;

        if (selectedMapNameText2 != null)
            selectedMapNameText2.text = label;

        if (selectedMapNameTMP2 != null)
            selectedMapNameTMP2.text = label;
    }

    private void RefreshLocalizationIndicator()
    {
        RefreshLocalizationIndicatorText(localizedIndicatorTMP);
        RefreshLocalizationIndicatorText(localizedIndicatorTMP2);
    }

    private void RefreshLocalizationIndicatorText(TMP_Text target)
    {
        if (target == null)
            return;

        // Always show 'O'
        target.text = "O";

        // Green if localized, red otherwise
        target.color = IsLocalized ? localizedColor : notLocalizedColor;
    }
}