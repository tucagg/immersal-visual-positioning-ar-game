using UnityEngine.XR.ARFoundation;
using UnityEngine;

[RequireComponent(typeof(ARSession))]
public class ARSessionFocusHandler : MonoBehaviour
{
    private ARSession _arSession;

    private void Awake() => _arSession = GetComponent<ARSession>();

    private void OnApplicationFocus(bool hasFocus) => _arSession.enabled = hasFocus;
}
