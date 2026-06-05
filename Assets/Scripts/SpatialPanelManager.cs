using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Panelleri AR dünyasında yay (arc) şeklinde konumlandırır.
/// Her panel kendi World Space Canvas'ına sahip olmalı.
///
/// Kullanım:
///   1. Sahneye boş bir GameObject ekle, bu scripti bağla.
///   2. Her panel Canvas'ını Panels listesine ekle + açısını ayarla.
///   3. AR Canvas'ını Ar Canvas alanına bağla.
///   4. AR Camera'yı bağla.
/// </summary>
public class SpatialPanelManager : MonoBehaviour
{
    [System.Serializable]
    public class SpatialPanel
    {
        public string label;
        public Canvas canvas;
        [Tooltip("Yatay açı (derece). 0 = ileri, - = sol, + = sağ")]
        public float angleDeg;
    }

    [Header("Spatial Panels (Arc)")]
    public List<SpatialPanel> panels = new();

    [Header("AR Panel (özel — arc dışı)")]
    public Canvas arCanvas;

    [Header("Layout")]
    [Tooltip("Kameradan uzaklık (metre)")]
    public float radius = 2f;
    [Tooltip("Panel yüksekliği (Y offset, metre)")]
    public float panelHeight = 0f;
    [Tooltip("Panel scale — 1080px canvas için ~0.0005 önerilir")]
    public float panelScale = 0.0005f;

    [Header("Camera")]
    public Camera arCamera;

    [Header("App UI Manager")]
    [Tooltip("AR moduna girince/çıkınca kamerayı açıp kapamak için.")]
    public AppUIManager appUIManager;

    [Tooltip("Sadece AR modunda görünmesi gereken 3D içerik kökü (clue objeleri). gameRoot değil, onun altındaki ayrı bir child olmalı.")]
    public GameObject arContentRoot;

    [Header("Swipe — AR'dan çıkış")]
    [Tooltip("Parmak kaç px kaydırınca swipe sayılır")]
    public float swipeThreshold = 120f;

    // ── iç durum ──────────────────────────────────────────────────────────────

    public bool IsInARMode => _arMode;
    private bool   _arMode;
    private Vector2 _touchStart;
    private bool    _touchTracking;

    // Arc'ın "0 açısı"na karşılık gelen world-space yön (derece).
    // SetARMode(false) çağrıldığında kameranın o anki yatay yönüne snap'lenir;
    // böylece spatial UI her zaman kullanıcının önünde açılır.
    private float _arcBaseYDeg = 0f;

    // ── Unity lifecycle ───────────────────────────────────────────────────────

    void Start()
    {
        if (arCamera == null) arCamera = Camera.main;
        InitCanvases();
        SetARMode(false);
    }

    void LateUpdate()
    {
        UpdateArcPositions();
        HandleSwipe();
    }

    // ── Canvas ilk kurulumu (scale + renderMode bir kez) ─────────────────────

    void InitCanvases()
    {
        foreach (var p in panels)
        {
            if (p.canvas == null) continue;
            p.canvas.renderMode  = RenderMode.WorldSpace;
            if (arCamera != null) p.canvas.worldCamera = arCamera;
            p.canvas.transform.localScale = Vector3.one * panelScale;
        }
    }

    // ── Arc yerleşimi — her frame kamera pozisyonuna göre güncellenir ─────────

    /// <summary>
    /// Arc'ın referans yönünü kameranın o anki yatay yönüne snap'ler.
    /// AR modundan spatial UI'ya geçişte çağrılır; böylece paneller her zaman
    /// kullanıcının baktığı yönün önünde açılır (oda değiştirince kaybolmaz).
    /// </summary>
    private void SnapArcToCamera()
    {
        if (arCamera == null) return;
        Vector3 fwd = arCamera.transform.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude > 0.001f)
            _arcBaseYDeg = Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg;
    }

    void UpdateArcPositions()
    {
        if (arCamera == null) return;
        Vector3 camPos = arCamera.transform.position;

        foreach (var p in panels)
        {
            if (p.canvas == null || !p.canvas.gameObject.activeSelf) continue;

            // Panel açısı: arc referans yönü + her panelin kendi offset açısı.
            // _arcBaseYDeg sayesinde "0°" her zaman son snap anındaki kamera yönüdür.
            float worldDeg = _arcBaseYDeg + p.angleDeg;
            float rad = worldDeg * Mathf.Deg2Rad;
            float x   = Mathf.Sin(rad) * radius;
            float z   = Mathf.Cos(rad) * radius;

            var t = p.canvas.transform;
            t.position = new Vector3(camPos.x + x, camPos.y + panelHeight, camPos.z + z);
            t.rotation = Quaternion.LookRotation(new Vector3(x, 0f, z).normalized);
        }
    }

    // ── AR modu ───────────────────────────────────────────────────────────────

    public void SetARMode(bool arMode)
    {
        _arMode = arMode;

        // Spatial UI'ya dönerken arc'ı kameranın güncel yönüne sıfırla.
        // Böylece farklı bir odadan çıkıldığında paneller kullanıcının önünde açılır.
        if (!arMode) SnapArcToCamera();

        foreach (var p in panels)
            if (p.canvas != null)
                p.canvas.gameObject.SetActive(!arMode);

        if (arCanvas != null)
            arCanvas.gameObject.SetActive(arMode);

        // Sadece 3D AR içeriği (clue objeleri) AR modunda görünür — kamera dokunulmaz
        if (arContentRoot != null)
            arContentRoot.SetActive(arMode);

        // Kamera yönetimi: AppUIManager'a haber ver
        if (appUIManager != null)
        {
            if (arMode) appUIManager.OnARModeEntered();
            else        appUIManager.OnARModeExited();
        }
    }

    public void EnterARMode() => SetARMode(true);
    public void ExitARMode()  => SetARMode(false);

    // ── Swipe: yukarı → AR, aşağı → spatial ─────────────────────────────────

    void HandleSwipe()
    {
#if UNITY_EDITOR
        // Editor'de mouse ile test
        if (Input.GetMouseButtonDown(0))
        {
            _touchStart    = Input.mousePosition;
            _touchTracking = true;
        }
        else if (Input.GetMouseButtonUp(0) && _touchTracking)
        {
            _touchTracking = false;
            float dy = ((Vector2)Input.mousePosition - _touchStart).y;
            EvaluateSwipe(dy);
        }
#else
        if (Input.touchCount == 1)
        {
            Touch t = Input.GetTouch(0);
            if (t.phase == TouchPhase.Began)
            {
                _touchStart    = t.position;
                _touchTracking = true;
            }
            else if ((t.phase == TouchPhase.Ended || t.phase == TouchPhase.Canceled)
                     && _touchTracking)
            {
                _touchTracking = false;
                float dy = t.position.y - _touchStart.y;
                EvaluateSwipe(dy);
            }
        }
        else if (Input.touchCount == 0)
        {
            _touchTracking = false;
        }
#endif
    }

    void EvaluateSwipe(float dy)
    {
        if (Mathf.Abs(dy) < swipeThreshold) return;

        if (dy < 0 && !_arMode) // yukarı kaydır → AR
            SetARMode(true);
        else if (dy > 0 && _arMode) // aşağı kaydır → spatial
            SetARMode(false);
    }
}
