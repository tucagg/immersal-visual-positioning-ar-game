using UnityEngine;

/// <summary>
/// World Space Canvas'ı AR kamerasının önünde sabit mesafede tutar.
/// Canvas objesine ekle.
/// </summary>
public class WorldSpaceUIFollower : MonoBehaviour
{
    [Tooltip("AR Camera. Boş bırakılırsa Camera.main kullanılır.")]
    public Camera targetCamera;

    [Tooltip("Kameradan uzaklık (metre).")]
    public float distance = 1.2f;

    void LateUpdate()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null) return;
        }

        transform.position = targetCamera.transform.position
                           + targetCamera.transform.forward * distance;
        transform.rotation = targetCamera.transform.rotation;
    }
}
