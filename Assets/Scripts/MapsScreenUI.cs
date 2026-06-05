using System;
using System.Collections;
using System.Collections.Generic;
using Firebase.Database;
using Firebase.Extensions;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Attached to Screen_Maps.
///
/// On open:
///   1. Acquires GPS position (or editor stub).
///   2. Loads all "approved" maps from Firebase.
///   3. Calculates distance + bearing from player to every map.
///   4. Positions the 5 nearest maps as dots inside RadarFrame.
///   5. Populates MapListScrollView sorted by distance, filtered by active category.
///
/// Inspector wiring:
///   Radar ─────────────────────────────────────────────────────────────
///   radarFrame      → RadarFrame   (RectTransform — used for radius)
///   playerDot       → PlayerCenterDot (RectTransform — stays at center)
///   mapDots[0..4]   → MapDot_01 … MapDot_05 (RectTransform array, size 5)
///
///   Category tabs ──────────────────────────────────────────────────────
///   btnAll / btnHistorical / btnSocial / btnCriminal → respective Buttons
///
///   List ───────────────────────────────────────────────────────────────
///   mapRowTemplate  → MapRow_Template (kept inactive, cloned per map)
///   mapRowContainer → Content (Transform)
///   txtNearbyHint   → Txt_NearbyHint  (optional status label)
///
///   Navigation ─────────────────────────────────────────────────────────
///   appUIManager    → AppUIManager
/// </summary>
public class MapsScreenUI : MonoBehaviour
{
    // ── Inspector ────────────────────────────────────────────────────────────

    [Header("Radar")]
    public RectTransform  radarFrame;
    public RectTransform  playerDot;
    public RectTransform[] mapDots = new RectTransform[5];  // MapDot_01..05

    [Header("Category Tabs")]
    public Button btnAll;
    public Button btnHistorical;
    public Button btnSocial;
    public Button btnCriminal;

    [Header("Map List")]
    public GameObject mapRowTemplate;
    public Transform  mapRowContainer;
    public TMP_Text   txtNearbyHint;

    [Header("Navigation")]
    public AppUIManager appUIManager;

    [Tooltip("MapRootProvider manages the location service — reuse it instead of starting our own.")]
    public MapRootProvider mapRootProvider;

    // ── Static context for map details screen ────────────────────────────────

    public static string SelectedMapDbKey  { get; private set; }
    public static string SelectedMapName   { get; private set; }

    // ── Internal data ────────────────────────────────────────────────────────

    private class MapEntry
    {
        public string dbKey;
        public string name;
        public string category;
        public double lat, lon;
        public double distanceMeters; // filled after GPS acquired
        public double bearingDeg;     // 0 = north, 90 = east
        public float  avgRating;
        public int    ratingCount;
        public int    requiredLevel;  // 0 = no restriction
    }

    private readonly List<MapEntry>    _allMaps      = new();
    private readonly List<MapEntry>    _displayMaps  = new();   // sorted + filtered
    private readonly List<GameObject>  _spawnedRows  = new();

    private string  _activeCategory = "All";
    private double  _playerLat, _playerLon;
    private bool    _hasLocation;
    private bool    _locationReady;   // GPS coroutine finished (success or fail)
    private int     _playerLevel = 1; // updated by real-time user listener

    // SC-2 timing
    private float _screenOpenTime;
    private bool  _firstRenderLogged;

    // Real-time listener state
    private Query                              _mapsQuery;
    private EventHandler<ValueChangedEventArgs> _mapsHandler;
    private DatabaseReference                  _userRef;
    private EventHandler<ValueChangedEventArgs> _userHandler;

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (mapRowTemplate != null) mapRowTemplate.SetActive(false);

        _allMaps.Clear();
        _hasLocation       = false;
        _locationReady     = false;
        _activeCategory    = "All";
        _firstRenderLogged = false;
        _screenOpenTime    = Time.realtimeSinceStartup;

        Debug.Log($"[SC2][DISCOVERY] Screen opened — t=0 ms | time={DateTime.UtcNow:HH:mm:ss.fff} UTC");

        SetHint("Locating…");
        ClearRows();
        HideAllDots();
        WireCategoryButtons();

        // GPS once per enable; Firebase real-time runs in parallel
        StartCoroutine(AcquireLocationThenRefresh());
        SubscribeUserRealtime();
        SubscribeMapsRealtime();
    }

    private void OnDisable()
    {
        UnsubscribeMapsRealtime();
        UnsubscribeUserRealtime();
    }

    private void OnDestroy()
    {
        UnsubscribeMapsRealtime();
        UnsubscribeUserRealtime();
    }

    // ── GPS acquisition ───────────────────────────────────────────────────────

    private IEnumerator AcquireLocationThenRefresh()
    {
        yield return StartCoroutine(AcquireLocation());
        _locationReady = true;

        // If Firebase already delivered map data before GPS finished, render now.
        if (_allMaps.Count > 0)
            RefreshUI();
    }

    // ── Firebase real-time subscription ──────────────────────────────────────

    private void SubscribeMapsRealtime()
    {
        UnsubscribeMapsRealtime();

        _mapsQuery = FirebaseInitializer.DB.Child("maps");

        _mapsHandler = (_, args) =>
        {
            if (args.DatabaseError != null)
            {
                SetHint("Could not load maps.");
                return;
            }

            _allMaps.Clear();

            if (args.Snapshot != null && args.Snapshot.Exists)
            {
                foreach (DataSnapshot snap in args.Snapshot.Children)
                {
                    string status = ChildStr(snap, "approvalStatus", "");
                    if (status != "approved") continue;

                    int   rSum   = ChildInt(snap, "ratingSum");
                    int   rCount = ChildInt(snap, "ratingCount");
                    float avg    = rCount > 0 ? (float)rSum / rCount : 0f;

                    // Map's required level (stored as string in Firebase, 0 = no restriction)
                    int.TryParse(ChildStr(snap, "level", "0"), out int reqLevel);

                    _allMaps.Add(new MapEntry
                    {
                        dbKey         = snap.Key,
                        name          = ChildStr(snap, "name", "Unnamed"),
                        category      = ChildStr(snap, "category", ""),
                        lat           = ChildDouble(snap, "lat"),
                        lon           = ChildDouble(snap, "lon"),
                        avgRating     = avg,
                        ratingCount   = rCount,
                        requiredLevel = reqLevel
                    });
                }
            }

            float firebaseElapsed = (Time.realtimeSinceStartup - _screenOpenTime) * 1000f;
            Debug.Log(
                $"[SC2][DISCOVERY] Firebase data received" +
                $" | approved_maps={_allMaps.Count}" +
                $" | elapsed={firebaseElapsed:F0}ms" +
                $" | load_status={(firebaseElapsed <= 5000f ? "PASS ✓ (≤5 s)" : "FAIL ✗ (>5 s)")}"
            );

            // Only render if GPS is ready; otherwise the GPS coroutine will trigger RefreshUI.
            if (_locationReady)
                RefreshUI();
        };

        _mapsQuery.ValueChanged += _mapsHandler;
    }

    private void UnsubscribeMapsRealtime()
    {
        if (_mapsQuery != null && _mapsHandler != null)
            _mapsQuery.ValueChanged -= _mapsHandler;

        _mapsQuery   = null;
        _mapsHandler = null;
    }

    // ── Player level real-time listener ──────────────────────────────────────

    private void SubscribeUserRealtime()
    {
        UnsubscribeUserRealtime();

        string uid = "";
        if (AuthManager.Instance != null && AuthManager.Instance.CurrentUser != null)
            uid = AuthManager.Instance.CurrentUser.UserId;
        else
        {
            try { uid = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser?.UserId ?? ""; }
            catch { }
        }

        if (string.IsNullOrEmpty(uid)) return;

        _userRef = FirebaseInitializer.DB
            .Child("users").Child(uid);

        _userHandler = (_, args) =>
        {
            if (args.DatabaseError != null || args.Snapshot == null || !args.Snapshot.Exists)
                return;

            int newLevel = ChildInt(args.Snapshot, "level");
            if (newLevel < 1) newLevel = 1;

            if (newLevel != _playerLevel)
            {
                _playerLevel = newLevel;
                if (_locationReady) RefreshUI(); // level changed → re-filter maps
            }
            else
            {
                _playerLevel = newLevel;
            }
        };

        _userRef.ValueChanged += _userHandler;
    }

    private void UnsubscribeUserRealtime()
    {
        if (_userRef != null && _userHandler != null)
            _userRef.ValueChanged -= _userHandler;

        _userRef    = null;
        _userHandler = null;
    }

    // ── GPS ──────────────────────────────────────────────────────────────────

    private IEnumerator AcquireLocation()
    {
#if UNITY_EDITOR
        _playerLat   = 41.0082;
        _playerLon   = 28.9784;
        _hasLocation = true;
        yield break;
#else
        // Delegate entirely to MapRootProvider so we don't fight over
        // Input.location.Start/Stop — that breaks MapRootProvider's AR flow.
        if (mapRootProvider == null)
            mapRootProvider = FindFirstObjectByType<MapRootProvider>(FindObjectsInactive.Include);

        if (mapRootProvider != null)
            yield return StartCoroutine(mapRootProvider.EnsureLocationRunning());

        if (mapRootProvider != null && mapRootProvider.TryGetLastLocation(out double lat, out double lon))
        {
            _playerLat   = lat;
            _playerLon   = lon;
            _hasLocation = true;
        }
        else
        {
            SetHint("Could not get location.");
        }
#endif
    }

    // ── UI refresh ────────────────────────────────────────────────────────────

    private void RefreshUI()
    {
        // Calculate distances (requires location)
        foreach (var m in _allMaps)
        {
            if (_hasLocation)
            {
                m.distanceMeters = Haversine(_playerLat, _playerLon, m.lat, m.lon);
                m.bearingDeg     = Bearing(_playerLat, _playerLon, m.lat, m.lon);
            }
            else
            {
                m.distanceMeters = double.MaxValue;
                m.bearingDeg     = 0;
            }
        }

        _allMaps.Sort((a, b) => a.distanceMeters.CompareTo(b.distanceMeters));

        // SC-2: log 500m discovery radius results on first render after screen opens
        if (!_firstRenderLogged && _hasLocation)
        {
            _firstRenderLogged = true;
            float renderElapsed = (Time.realtimeSinceStartup - _screenOpenTime) * 1000f;

            int within500 = 0;
            string nearestInfo = "none";
            foreach (var m in _allMaps)
            {
                if (m.distanceMeters <= 500.0) within500++;
            }
            if (_allMaps.Count > 0)
                nearestInfo = $"{_allMaps[0].name} @ {_allMaps[0].distanceMeters:F0} m";

            Debug.Log(
                $"[SC2][DISCOVERY] List rendered" +
                $" | total_approved={_allMaps.Count}" +
                $" | within_500m={within500}" +
                $" | nearest={nearestInfo}" +
                $" | player=({_playerLat:F5}, {_playerLon:F5})" +
                $" | total_elapsed={renderElapsed:F0}ms" +
                $" | load_status={(renderElapsed <= 5000f ? "PASS ✓ (≤5 s)" : "FAIL ✗ (>5 s)")}"
            );
        }

        // Apply category filter (level-locked maps still appear but are styled differently)
        _displayMaps.Clear();
        foreach (var m in _allMaps)
        {
            if (_activeCategory == "All" || m.category == _activeCategory)
                _displayMaps.Add(m);
        }

        UpdateRadar();
        UpdateList();
        SetHint(_displayMaps.Count == 0 ? "No maps found nearby." : "");
    }

    // ── Radar ─────────────────────────────────────────────────────────────────

    private void UpdateRadar()
    {
        HideAllDots();

        if (!_hasLocation || _allMaps.Count == 0 || radarFrame == null) return;

        int    dotCount = Mathf.Min(mapDots.Length, _allMaps.Count);
        float  radius   = Mathf.Min(radarFrame.rect.width, radarFrame.rect.height) * 0.5f;

        // +5 m margin so the farthest dot never sits exactly on the edge.
        double maxDist = _allMaps[dotCount - 1].distanceMeters + 5.0;

        for (int i = 0; i < dotCount; i++)
        {
            var dot   = mapDots[i];
            var entry = _allMaps[i];
            if (dot == null) continue;

            // Dot kendi boyutunun yarısı kadar içeride kalacak şekilde yerleşir.
            float dotHalf        = dot.rect.width * 0.5f;
            float placementRadius = Mathf.Max(0f, radius - dotHalf);

            float t     = Mathf.Clamp01((float)(entry.distanceMeters / maxDist));
            float angle = (float)(entry.bearingDeg * Mathf.Deg2Rad);

            float px = Mathf.Sin(angle) * t * placementRadius;
            float py = Mathf.Cos(angle) * t * placementRadius;

            dot.anchoredPosition = new Vector2(px, py);
            dot.gameObject.SetActive(true);

            // Colour the dot by distance rank — matches the list row colour exactly.
            bool  dotLocked = entry.requiredLevel > 0 && _playerLevel < entry.requiredLevel;
            Color dotColor  = MapColor(i, dotLocked); // i == rank in _allMaps

            var dotImg = dot.GetComponent<Image>();
            if (dotImg != null) dotImg.color = dotColor;

            var lbl = dot.GetComponentInChildren<TMP_Text>(true);
            if (lbl != null)
            {
                lbl.text  = entry.name;
                lbl.color = dotColor;
            }
        }
    }

    private void HideAllDots()
    {
        foreach (var d in mapDots)
            if (d != null) d.gameObject.SetActive(false);
    }

    // ── Per-map colours (indexed by distance rank in _allMaps) ───────────────

    // Each map gets a unique colour based on its position in the distance-sorted
    // _allMaps list. The same index is used for both the list row and the radar
    // dot, so users can visually match them. Locked maps get a darkened variant.

    private static readonly Color[] MapColors =
    {
        new Color(0.94f, 0.64f, 0.16f, 1f), // 0 amber
        new Color(0.30f, 0.80f, 0.77f, 1f), // 1 teal
        new Color(1.00f, 0.42f, 0.42f, 1f), // 2 coral
        new Color(0.60f, 0.40f, 0.94f, 1f), // 3 violet
        new Color(0.29f, 0.82f, 0.46f, 1f), // 4 green
        new Color(0.98f, 0.45f, 0.72f, 1f), // 5 pink
        new Color(0.20f, 0.69f, 0.94f, 1f), // 6 sky blue
        new Color(0.95f, 0.77f, 0.06f, 1f), // 7 yellow
    };

    private static readonly Color ColorLocked = new Color(0.35f, 0.35f, 0.35f, 1f);

    // Returns the colour for the map at position `rank` in _allMaps (0 = nearest).
    // Locked maps are always flat grey regardless of rank.
    private static Color MapColor(int rank, bool locked)
    {
        return locked ? ColorLocked : MapColors[rank % MapColors.Length];
    }

    // ── List ──────────────────────────────────────────────────────────────────

    private void UpdateList()
    {
        ClearRows();

        if (mapRowTemplate == null || mapRowContainer == null) return;

        foreach (var entry in _displayMaps)
        {
            bool  isLocked = entry.requiredLevel > 0 && _playerLevel < entry.requiredLevel;
            int   rank     = _allMaps.IndexOf(entry); // distance rank → consistent with radar
            Color color    = MapColor(rank, isLocked);

            if (entry.requiredLevel > 0)
            {
                Debug.Log(
                    $"[SC1][LEVEL_GATE] map=\"{entry.name}\"" +
                    $" | required_level={entry.requiredLevel}" +
                    $" | player_level={_playerLevel}" +
                    $" | accessible={!isLocked}" +
                    $" | gate_status={(isLocked ? "LOCKED 🔒" : "OPEN ✓")}"
                );
            }

            GameObject row = Instantiate(mapRowTemplate, mapRowContainer);
            row.SetActive(true);
            _spawnedRows.Add(row);

            // Populate text fields
            SetChildText(row, "LeftBlock/Txt_MapName",   entry.name);
            SetChildText(row, "LeftBlock/Txt_Category",  entry.category);
            SetChildText(row, "RightBlock/Txt_Rating",   FormatRating(entry.avgRating, entry.ratingCount));
            SetChildText(row, "RightBlock/Txt_Distance", FormatDistance(entry.distanceMeters));
            SetChildText(row, "MiddleBlock/Txt_Level", $"Lv. {entry.requiredLevel}");


            // Apply category colour to images only; texts stay white.
            foreach (var img in row.GetComponentsInChildren<Image>(true))
                img.color = color;

            // Wire click — locked maps are still tappable (user sees details but can't play).
            string capturedKey  = entry.dbKey;
            string capturedName = entry.name;
            var btn = row.GetComponent<Button>() ?? row.GetComponentInChildren<Button>(true);
            if (btn != null)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() => OnRowTapped(capturedKey, capturedName));
            }
        }
    }

    private void ClearRows()
    {
        foreach (var go in _spawnedRows)
            if (go != null) DestroyImmediate(go);
        _spawnedRows.Clear();
    }

    // ── Category tabs ─────────────────────────────────────────────────────────

    private static readonly Color ColorActive   = new Color(0xF0/255f, 0x93/255f, 0x2B/255f, 1f); // #F0932B
    private static readonly Color ColorDefault  = new Color(0xFF/255f, 0xBE/255f, 0x76/255f, 1f); // #FFBE76

    private void WireCategoryButtons()
    {
        Wire(btnAll,        "All");
        Wire(btnHistorical, "Historical");
        Wire(btnSocial,     "Social");
        Wire(btnCriminal,   "Criminal");

        RefreshTabColors();
    }

    private void Wire(Button btn, string category)
    {
        if (btn == null) return;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(() =>
        {
            _activeCategory = category;
            RefreshTabColors();
            RefreshUI();
        });
    }

    private void RefreshTabColors()
    {
        SetTabColor(btnAll,        "All");
        SetTabColor(btnHistorical, "Historical");
        SetTabColor(btnSocial,     "Social");
        SetTabColor(btnCriminal,   "Criminal");
    }

    private void SetTabColor(Button btn, string category)
    {
        if (btn == null) return;
        var img = btn.GetComponent<Image>();
        if (img != null)
            img.color = category == _activeCategory ? ColorActive : ColorDefault;
    }

    // ── Row tap ───────────────────────────────────────────────────────────────

    private void OnRowTapped(string dbKey, string name)
    {
        SelectedMapDbKey = dbKey;
        SelectedMapName  = name;

        if (appUIManager == null)
            appUIManager = FindFirstObjectByType<AppUIManager>();

        if (appUIManager != null)
            appUIManager.ShowMapDetails();
        else
            Debug.LogWarning("[MapsScreenUI] AppUIManager not found.");
    }

    // ── Geo helpers ───────────────────────────────────────────────────────────

    /// <summary>Distance in metres between two lat/lon pairs (Haversine) — matches MapRootProvider.</summary>
    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        const double R = 6371000.0;
        double dLat = (lat2 - lat1) * Math.PI / 180.0;
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                 + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
                 * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return R * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
    }

    /// <summary>Initial bearing in degrees (0 = north, 90 = east) from point 1 to point 2.</summary>
    private static double Bearing(double lat1, double lon1, double lat2, double lon2)
    {
        double dLon = (lon2 - lon1) * Math.PI / 180.0;
        lat1 *= Math.PI / 180.0;
        lat2 *= Math.PI / 180.0;
        double y = Math.Sin(dLon) * Math.Cos(lat2);
        double x = Math.Cos(lat1) * Math.Sin(lat2) - Math.Sin(lat1) * Math.Cos(lat2) * Math.Cos(dLon);
        return ((Math.Atan2(y, x) * 180.0 / Math.PI) + 360.0) % 360.0;
    }

    // ── Format helpers ────────────────────────────────────────────────────────

    private static string FormatDistance(double meters)
    {
        if (meters >= double.MaxValue / 2) return "–";
        if (meters < 1000.0) return $"{meters:F0} m";
        return $"{meters / 1000.0:F1} km";
    }

    private static string FormatRating(float avg, int count)
    {
        if (count == 0) return "–";
        return $"{avg:F1} / 5";
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    private void SetHint(string msg)
    {
        if (txtNearbyHint != null) txtNearbyHint.text = msg;
    }

    /// <summary>Sets text on a descendant found by a slash-separated path.</summary>
    private static void SetChildText(GameObject root, string path, string value)
    {
        Transform t = root.transform.Find(path);
        if (t == null) return;
        var txt = t.GetComponent<TMP_Text>();
        if (txt != null) txt.text = value;
    }

    // ── Firebase snapshot helpers ─────────────────────────────────────────────

    private static string ChildStr(DataSnapshot s, string key, string fallback)
    {
        var c = s.Child(key);
        return (c.Exists && c.Value != null) ? c.Value.ToString() : fallback;
    }

    private static int ChildInt(DataSnapshot s, string key)
    {
        var c = s.Child(key);
        if (!c.Exists || c.Value == null) return 0;
        int.TryParse(c.Value.ToString(), out int v);
        return v;
    }

    private static double ChildDouble(DataSnapshot s, string key)
    {
        var c = s.Child(key);
        if (!c.Exists || c.Value == null) return 0.0;
        if (c.Value is double d) return d;
        if (c.Value is long   l) return (double)l;
        double.TryParse(
            System.Convert.ToString(c.Value, System.Globalization.CultureInfo.InvariantCulture),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out double v);
        return v;
    }
}
