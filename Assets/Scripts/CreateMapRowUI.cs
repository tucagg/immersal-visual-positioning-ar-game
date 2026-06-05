using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CreateMapRowUI : MonoBehaviour
{
    public TMP_Text txtMapName;
    public TMP_Text txtMapType;
    public TMP_Text txtMapStatus;
    public Button btnEdit;

    private string _mapId;
    private AppUIManager _ui;

    public void Setup(string mapId, string mapName, string category, string status, AppUIManager ui)
    {
        _mapId = mapId;
        _ui = ui;

        if (txtMapName != null) txtMapName.text = mapName;
        if (txtMapType != null) txtMapType.text = category;
        if (txtMapStatus != null) txtMapStatus.text = status;

        if (btnEdit != null)
        {
            btnEdit.onClick.RemoveAllListeners();
            btnEdit.onClick.AddListener(OnClickEdit);
        }
    }

    private void OnClickEdit()
    {
        EditMapUI.SetSelectedMap(_mapId);

        if (_ui != null)
            _ui.ShowEditMap();
    }
}