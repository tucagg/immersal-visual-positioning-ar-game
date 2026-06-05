using UnityEngine;

/// <summary>
/// Deprecated wrapper kept only to avoid breaking old button references.
/// Prefer wiring bottom bar buttons directly to AppUIManager.
/// </summary>
public class BottomBarController : MonoBehaviour
{
    [Header("Navigation")]
    public AppUIManager appUIManager;

    private AppUIManager UI
    {
        get
        {
            if (appUIManager == null)
                appUIManager = FindFirstObjectByType<AppUIManager>();

            return appUIManager;
        }
    }

    public void ShowAR()
    {
        if (UI != null) UI.ShowAR();
    }

    public void ShowMap()
    {
        if (UI != null) UI.ShowMap();
    }

    public void ShowProfile()
    {
        if (UI != null) UI.ShowProfile();
    }

    public void ShowSocial()
    {
        if (UI != null) UI.ShowSocial();
    }

    public void ShowCreate()
    {
        if (UI != null) UI.ShowCreate();
    }

    public void ShowAdmin()
    {
        if (UI != null) UI.ShowAdmin();
    }
}