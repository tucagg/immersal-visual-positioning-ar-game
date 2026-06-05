using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ClueRowUI : MonoBehaviour
{
    public TMP_Text txtClueName;
    public TMP_Text txtClueType;
    public TMP_Text txtClueIndex;
    public Button btnEdit;

    private string _anchorId;
    private AnchorsRealtime _anchors;
    private AppUIManager _ui;

    public void Setup(string anchorId, AnchorsRealtime anchors, AppUIManager ui, AnchorsRealtime.ClueEditData data)
    {
        _anchorId = anchorId;
        _anchors = anchors;
        _ui = ui;

        if (txtClueName != null) txtClueName.text = data.clueName;
        if (txtClueType != null) txtClueType.text = data.clueType;
        if (txtClueIndex != null) txtClueIndex.text = data.clueIndex.ToString();

        if (btnEdit != null)
        {
            btnEdit.onClick.RemoveAllListeners();
            btnEdit.onClick.AddListener(OnClickEdit);
        }
    }

    private void OnClickEdit()
    {
        if (_anchors == null || _ui == null) return;

        _anchors.SetSelectedAnchorForEdit(_anchorId);
        _ui.ShowEditClue();
    }
}