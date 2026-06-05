using UnityEngine;
using UnityEngine.EventSystems;

public class AnchorHandle : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [Tooltip("This anchor's unique id from Firebase")]
    public string anchorId;

    private AnchorsRealtime anchors;

    private Camera _cam;
    private Plane _dragPlane;
    private Vector3 _dragOffset;
    private bool _dragging;

    void Awake()
    {
        anchors = Object.FindFirstObjectByType<AnchorsRealtime>();
        _cam = Camera.main;
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        if (anchors == null || string.IsNullOrEmpty(anchorId))
            return;

        // Route through creator/admin handler when admin mode is active OR
        // when any edit mode is on (so map creators can interact with their anchors).
        if (anchors.adminMode || anchors.IsInAnyEditMode())
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
        // EditClues modundayken sürüklenebilsin (admin veya creator fark etmez)
        if (anchors == null || !anchors.IsInEditCluesMode()) return;
        if (_cam == null) _cam = Camera.main;

        // Notify AnchorsRealtime which anchor is active so two-finger rotation knows what to rotate.
        anchors.SetActiveDragAnchor(anchorId);

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