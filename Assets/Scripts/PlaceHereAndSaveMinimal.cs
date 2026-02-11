using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

#if UNITY_IOS
using UnityEngine.XR.ARKit;
#endif

public class PlaceHereAndSaveMinimal : MonoBehaviour
{
    [Header("Visual")]
    public GameObject markerPrefab;
    public float markerForwardMeters = 2f;

    [Header("Location")]
    public float desiredAccuracyMeters = 5f;
    public float updateDistanceMeters = 1f;
    public float locationTimeoutSeconds = 20f;

    [Header("Logs")]
    public bool logToConsole = true;

    bool _isPlacing;   // aynı anda iki akışı engelle

#if UNITY_IOS && !UNITY_EDITOR
    // ARKitSessionSubsystem.nativePtr içeriğini okumak için
    struct NativePtrData { public int version; public IntPtr sessionPtr; }

    // CLLocationCoordinate2D eşleniği
    struct CLLocationCoordinate2D { public double latitude; public double longitude; }

    [DllImport("__Internal", EntryPoint = "ARSession_addGeoAnchor")]
    static extern void AddGeoAnchor(IntPtr session, CLLocationCoordinate2D coordinate, double altitude);
#endif

    // Butona bağlanacak metod
    public void PlaceHere()
    {
        if (_isPlacing) return;
        StartCoroutine(PlaceHereFlow());
    }

    IEnumerator PlaceHereFlow()
    {
        _isPlacing = true;

        // 1) Konumu anlık başlat / hazırla
        Input.compass.enabled = true;

        // Eğer hiç başlatılmamış ya da durdurulmuşsa şimdi başlat
        if (Input.location.status == LocationServiceStatus.Stopped)
            Input.location.Start(desiredAccuracyMeters, updateDistanceMeters);

        // İlk kez çağrılıyorsa iOS burada izin penceresini gösterir
        float t = 0f;
        while (Input.location.status == LocationServiceStatus.Initializing && t < locationTimeoutSeconds)
        {
            t += Time.deltaTime;
            yield return null;
        }

        if (Input.location.status != LocationServiceStatus.Running)
        {
            if (logToConsole)
                Debug.LogWarning($"Location NOT running (Status: {Input.location.status}). " +
                                 "Eğer daha önce reddettiysen Ayarlar > Uygulama > Konum'dan izin ver.");
            // İstersen kullanıcıyı ayarlara gönderebilirsin:
            // Application.OpenURL("app-settings:");
            _isPlacing = false;
            yield break;
        }

        // 2) Son bilinen konum
        var last = Input.location.lastData;
        double lat = last.latitude;
        double lon = last.longitude;
        double alt = double.IsNaN(last.altitude) ? 0.0 : last.altitude;

#if UNITY_IOS && !UNITY_EDITOR
        // 3) ARKit session pointer'ı al
        var arSession = UnityEngine.Object.FindFirstObjectByType<ARSession>();
        if (arSession == null || !(arSession.subsystem is ARKitSessionSubsystem s))
        {
            if (logToConsole) Debug.LogError("ARKitSessionSubsystem not found.");
            _isPlacing = false;
            yield break;
        }
        if (s.nativePtr == IntPtr.Zero)
        {
            if (logToConsole) Debug.LogError("ARKit native session ptr is zero.");
            _isPlacing = false;
            yield break;
        }
        var session = Marshal.PtrToStructure<NativePtrData>(s.nativePtr).sessionPtr;
        if (session == IntPtr.Zero)
        {
            if (logToConsole) Debug.LogError("ARKit sessionPtr is zero.");
            _isPlacing = false;
            yield break;
        }

        // 4) GeoAnchor isteği (native köprü)
        AddGeoAnchor(session, new CLLocationCoordinate2D { latitude = lat, longitude = lon }, alt);
        if (logToConsole) Debug.Log($"GeoAnchor requested at lat:{lat:F6}, lon:{lon:F6}, alt:{alt:F1}");
#else
        Debug.Log("GeoAnchor: iOS cihazda çalıştır. (Editor/diğer platformda yerleştirme atlandı.)");
#endif

        // 5) Görsel geri bildirim (küçük marker)
        if (markerPrefab != null && Camera.main != null)
        {
            var cam = Camera.main.transform;
            var pos = cam.position + cam.forward * markerForwardMeters;
            Instantiate(markerPrefab, pos, Quaternion.identity);
        }

        _isPlacing = false;
    }
}