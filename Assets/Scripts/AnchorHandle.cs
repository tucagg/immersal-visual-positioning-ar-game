using UnityEngine;
using UnityEngine.EventSystems;

public class AnchorHandle : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Tooltip("This anchor's unique id from Firebase")]
    public string anchorId;

    private AnchorsRealtime anchors;
    private AdminMenuUI adminMenu;

    private Camera _cam;
    private Plane _dragPlane;
    private Vector3 _dragOffset;
    private bool _dragging;

    void Awake()
    {
        anchors = Object.FindFirstObjectByType<AnchorsRealtime>();
        adminMenu = Object.FindFirstObjectByType<AdminMenuUI>();
        _cam = Camera.main;
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (anchors == null || string.IsNullOrEmpty(anchorId))
            return;

        if (anchors.adminMode)
        {
            anchors.OnAnchorClickedAsAdmin(anchorId);
        }
        else
        {
            anchors.OnAnchorClickedAsUser(anchorId);
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Sadece admin ve EditClues modundayken sürüklenebilsin
        if (anchors == null || !anchors.adminMode || !anchors.IsInEditCluesMode()) return;
        if (_cam == null) _cam = Camera.main;

        // Objeden geçen, kameraya bakan bir düzlem oluştur
        _dragPlane = new Plane(-_cam.transform.forward, transform.position);

        Ray ray = _cam.ScreenPointToRay(eventData.position);
        float enter;
        if (_dragPlane.Raycast(ray, out enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            _dragOffset = transform.position - hitPoint;
            _dragging = true;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (!_dragging || _cam == null) return;

        Ray ray = _cam.ScreenPointToRay(eventData.position);
        float enter;
        if (_dragPlane.Raycast(ray, out enter))
        {
            Vector3 hitPoint = ray.GetPoint(enter);
            transform.position = hitPoint + _dragOffset;
        }
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        _dragging = false;
    }
}