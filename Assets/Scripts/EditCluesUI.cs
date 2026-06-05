using UnityEngine;
using UnityEngine.UI;

public class EditCluesUI : MonoBehaviour
{
    [Header("Refs")]
    public AnchorsRealtime anchors;
    public AppUIManager appUIManager;

    [Header("Buttons")]
    public Button btnBack;

    [Header("List")]
    public Transform contentRoot;
    public GameObject clueRowTemplate;

    private void OnEnable()
    {
        if (btnBack != null)
        {
            btnBack.onClick.RemoveListener(OnBack);
            btnBack.onClick.AddListener(OnBack);
        }

        // Load anchor metadata from Firebase if not already in memory (e.g. when navigating
        // directly from Screen_CreateMain without going through AR first).
        if (anchors != null)
            anchors.LoadAnchorsForEditing(RefreshList);
        else
            RefreshList();
    }

    public void RefreshList()
    {
        if (anchors == null || appUIManager == null || contentRoot == null || clueRowTemplate == null)
            return;

        ClearList();

        var ids = anchors.GetCurrentAnchorIdsSorted();

        foreach (var id in ids)
        {
            if (!anchors.TryGetClueEditData(id, out var data))
                continue;

            var row = Instantiate(clueRowTemplate, contentRoot);
            row.SetActive(true);

            var rowUI = row.GetComponent<ClueRowUI>();
            if (rowUI != null)
            {
                rowUI.Setup(id, anchors, appUIManager, data);
            }
        }
    }

    private void OnBack()
    {
        if (appUIManager != null)
            appUIManager.GoBackCreate();
    }

    private void ClearList()
    {
        for (int i = contentRoot.childCount - 1; i >= 0; i--)
        {
            var child = contentRoot.GetChild(i).gameObject;

            if (child == clueRowTemplate)
                continue;

            Destroy(child);
        }
    }
}