using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PrefabSelectorUI : MonoBehaviour
{
    [Header("Refs")]
    public AnchorsRealtime anchors;

    [Header("UI")]
    public TMP_Text selectedAnchorLabel;
    public Transform listContent;        // PrefabScroll/Viewport/Content
    public GameObject listItemPrefab;    // MapListItemPrefab veya kopyası

    private string _selectedAnchorId;

    void Start()
    {
        BuildList();
    }

    void BuildList()
    {
        if (anchors == null || listContent == null || listItemPrefab == null)
        {
            Debug.LogWarning("[PrefabSelectorUI] Missing refs.");
            return;
        }

        // Eski çocukları temizle
        for (int i = listContent.childCount - 1; i >= 0; i--)
        {
            Destroy(listContent.GetChild(i).gameObject);
        }

        foreach (var opt in anchors.prefabOptions)
        {
            if (opt == null || opt.prefab == null)
                continue;

            var go = Instantiate(listItemPrefab, listContent);
            var txt = go.GetComponentInChildren<TMP_Text>();
            if (txt != null)
            {
                txt.text = string.IsNullOrEmpty(opt.displayName) ? opt.key : opt.displayName;
            }

            var btn = go.GetComponent<Button>();
            string key = opt.key; // closure
            if (btn != null)
            {
                btn.onClick.AddListener(() => OnPrefabClicked(key));
            }
        }
    }

    // AnchorsRealtime tarafından çağrılacak
    public void OnAnchorSelectedForPrefab(string anchorId)
    {
        _selectedAnchorId = anchorId;
        if (selectedAnchorLabel != null)
        {
            selectedAnchorLabel.text = "Selected anchor: " + anchorId;
        }
    }

    private void OnPrefabClicked(string key)
    {
        if (string.IsNullOrEmpty(_selectedAnchorId))
        {
            Debug.LogWarning("[PrefabSelectorUI] No anchor selected.");
            return;
        }

        if (anchors == null)
        {
            Debug.LogWarning("[PrefabSelectorUI] AnchorsRealtime missing.");
            return;
        }

        anchors.ChangePrefabForAnchor(_selectedAnchorId, key);
    }
    public void ClearSelection()
    {
        _selectedAnchorId = null;
        if (selectedAnchorLabel != null)
        {
            selectedAnchorLabel.text = "Selected anchor: (none)";
        }
    }
}