using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Firebase.Extensions;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Kullanıcının konumuna göre en yakın 20 haritayı iOS geofence olarak kaydeder.
/// Uygulama kapalıyken kullanıcı bölgeye girince iOS yerel bildirim gösterir.
/// </summary>
public class GeofenceNotifier : MonoBehaviour
{
    public static GeofenceNotifier Instance { get; private set; }

    [Header("Geofence")]
    [Tooltip("Her harita etrafındaki bildirim yarıçapı (metre)")]
    public float radiusMeters = 300f;

    [Header("Güncelleme")]
    [Tooltip("Bu kadar hareket edilince geofence'ler yenilenir (metre)")]
    public float updateThresholdMeters = 500f;
    [Tooltip("Konum kontrol aralığı — uygulama açıkken (saniye)")]
    public float locationPollSeconds = 120f;

    private const string PrefsKey = "nearby_notif_enabled";
    private const int    MaxRegions = 20;

    private bool      _hasLastPos;
    private float     _lastLat, _lastLon;
    private Coroutine _monitor;

    // ── iOS native bridge ─────────────────────────────────────────────────

#if UNITY_IOS && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void _GeoRequestPermissions();
    [DllImport("__Internal")] static extern void _GeoSetRegions(string json);
    [DllImport("__Internal")] static extern void _GeoClearRegions();
#else
    static void _GeoRequestPermissions()  => Debug.Log("[Geo] RequestPermissions (stub)");
    static void _GeoSetRegions(string j)  => Debug.Log("[Geo] SetRegions:\n" + j);
    static void _GeoClearRegions()        => Debug.Log("[Geo] ClearRegions (stub)");
#endif

    // ── Tercih ────────────────────────────────────────────────────────────

    public static bool IsEnabled
    {
        get => PlayerPrefs.GetInt(PrefsKey, 0) == 1;
        set { PlayerPrefs.SetInt(PrefsKey, value ? 1 : 0); PlayerPrefs.Save(); }
    }

    // ── Unity lifecycle ───────────────────────────────────────────────────

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
    }

    void Start()
    {
        // Kullanıcı zaten giriş yapmışsa (stay signed in) ve toggle açıksa hemen sync yap.
        if (IsEnabled)
        {
            _GeoRequestPermissions();
            StartCoroutine(WaitForAuthAndSync());
            StartMonitor();
        }
    }

    private IEnumerator WaitForAuthAndSync()
    {
        // AuthManager login tamamlanana kadar bekle (en fazla 10s)
        float t = 0f;
        while (t < 10f)
        {
            if (AuthManager.Instance != null && AuthManager.Instance.CurrentUser != null)
                break;
            yield return new WaitForSeconds(0.5f);
            t += 0.5f;
        }
        yield return SyncCoroutine();
    }

    void OnApplicationPause(bool paused)
    {
        if (paused && IsEnabled)
        {
            Debug.Log("[Geo] Arka plana geçildi — geofence güncelleniyor.");
            StartCoroutine(SyncCoroutine());
        }
    }

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>Toggle.onValueChanged'a bağla.</summary>
    public void OnToggleChanged(bool enabled)
    {
        IsEnabled = enabled;
        if (enabled)
        {
            _GeoRequestPermissions();
            StartCoroutine(SyncCoroutine());
            StartMonitor();
        }
        else
        {
            StopMonitor();
            _GeoClearRegions();
        }
    }

    /// <summary>Settings ekranı açılınca çağır.</summary>
    public void InitToggle(Toggle toggle)
    {
        if (toggle != null) toggle.SetIsOnWithoutNotify(IsEnabled);
    }

    /// <summary>AuthManager login başarısında çağırır.</summary>
    public void SyncGeofences()
    {
        if (!IsEnabled) return;
        _GeoRequestPermissions();
        StartCoroutine(SyncCoroutine());
        StartMonitor();
    }

    // ── Konum izleme döngüsü ──────────────────────────────────────────────

    private void StartMonitor()
    {
        StopMonitor();
        _monitor = StartCoroutine(LocationMonitorLoop());
    }

    private void StopMonitor()
    {
        if (_monitor != null) { StopCoroutine(_monitor); _monitor = null; }
    }

    private IEnumerator LocationMonitorLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(locationPollSeconds);
            if (!IsEnabled) yield break;

            bool gotPos  = false;
            float curLat = 0f, curLon = 0f;
            yield return GetGPS(
                (lat, lon) => { gotPos = true; curLat = lat; curLon = lon; },
                ()         => { gotPos = false; });

            if (!gotPos) continue;

            float moved = _hasLastPos
                ? HaversineMeters(_lastLat, _lastLon, curLat, curLon)
                : float.MaxValue;

            if (moved >= updateThresholdMeters)
            {
                Debug.Log($"[Geo] {moved:F0}m hareket — yenileniyor.");
                yield return SyncCoroutine();
            }
        }
    }

    // ── Geofence senkronizasyonu ──────────────────────────────────────────

    private IEnumerator SyncCoroutine()
    {
        while (!FirebaseInitializer.Ready) yield return null;

        // 1. GPS al
        bool  gotPos = false;
        float uLat   = 0f, uLon = 0f;
        yield return GetGPS(
            (lat, lon) => { gotPos = true; uLat = lat; uLon = lon; },
            ()         => { gotPos = false; });

        if (!gotPos) { Debug.LogWarning("[Geo] GPS alınamadı, sync atlandı."); yield break; }

        // 2. Firebase'den onaylı haritaları çek
        bool done = false;
        var maps = new List<MapEntry>();

        FirebaseInitializer.DB
            .Child("maps")
            .OrderByChild("approvalStatus")
            .EqualTo("approved")
            .GetValueAsync()
            .ContinueWithOnMainThread(task =>
            {
                if (task.IsCompletedSuccessfully && task.Result != null)
                {
                    foreach (var snap in task.Result.Children)
                    {
                        if (snap.Child("lat").Value == null || snap.Child("lon").Value == null) continue;
                        maps.Add(new MapEntry
                        {
                            id   = snap.Key,
                            name = snap.Child("name").Value?.ToString() ?? snap.Key,
                            lat  = ToDouble(snap.Child("lat").Value),
                            lon  = ToDouble(snap.Child("lon").Value),
                        });
                    }
                }
                done = true;
            });

        yield return new WaitUntil(() => done);
        if (maps.Count == 0) yield break;

        // 3. Mesafeye göre sırala — en yakın MaxRegions'ı al
        maps.Sort((a, b) =>
            HaversineMeters(uLat, uLon, (float)a.lat, (float)a.lon)
            .CompareTo(HaversineMeters(uLat, uLon, (float)b.lat, (float)b.lon)));

        if (maps.Count > MaxRegions)
            maps.RemoveRange(MaxRegions, maps.Count - MaxRegions);

        // 4. JSON → iOS
        var parts = new List<string>(maps.Count);
        var ic    = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var m in maps)
            parts.Add(
                "{\"id\":\""     + Esc(m.id)   + "\"," +
                "\"name\":\""    + Esc(m.name) + "\"," +
                "\"lat\":"       + m.lat.ToString("F6", ic) + "," +
                "\"lon\":"       + m.lon.ToString("F6", ic) + "," +
                "\"radius\":"    + radiusMeters.ToString("F1", ic) + "}");

        _GeoSetRegions("[" + string.Join(",", parts) + "]");

        _hasLastPos = true;
        _lastLat = uLat;
        _lastLon = uLon;

        Debug.Log($"[Geo] {maps.Count} bölge kaydedildi. En yakın: {maps[0].name}");
    }

    // ── GPS yardımcısı (platform bağımsız) ───────────────────────────────

    private IEnumerator GetGPS(System.Action<float, float> onSuccess, System.Action onFail)
    {
#if UNITY_EDITOR
        onSuccess(41.0082f, 28.9784f); // Editor stub — İstanbul
        yield break;
#else
        if (!Input.location.isEnabledByUser) { onFail(); yield break; }

        Input.location.Start(50f, 50f);

        int wait = 20;
        while (Input.location.status == LocationServiceStatus.Initializing && wait-- > 0)
            yield return new WaitForSeconds(0.5f);

        if (Input.location.status == LocationServiceStatus.Running)
        {
            var d = Input.location.lastData;
            onSuccess((float)d.latitude, (float)d.longitude);
        }
        else
        {
            onFail();
        }

        Input.location.Stop();
#endif
    }

    // ── Yardımcılar ───────────────────────────────────────────────────────

    private static float HaversineMeters(float lat1, float lon1, float lat2, float lon2)
    {
        const float R = 6_371_000f;
        float dLat = Mathf.Deg2Rad * (lat2 - lat1);
        float dLon = Mathf.Deg2Rad * (lon2 - lon1);
        float a = Mathf.Sin(dLat / 2f) * Mathf.Sin(dLat / 2f)
                + Mathf.Cos(Mathf.Deg2Rad * lat1) * Mathf.Cos(Mathf.Deg2Rad * lat2)
                * Mathf.Sin(dLon / 2f) * Mathf.Sin(dLon / 2f);
        return R * 2f * Mathf.Atan2(Mathf.Sqrt(a), Mathf.Sqrt(1f - a));
    }

    private static double ToDouble(object v)
    {
        double.TryParse(v?.ToString(),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out double r);
        return r;
    }

    private static string Esc(string s) =>
        s?.Replace("\\", "\\\\").Replace("\"", "\\\"") ?? "";

    private struct MapEntry
    {
        public string id, name;
        public double lat, lon;
    }
}
