using UnityEngine;
using UnityEngine.UI;

public class EditClueLocARUI : MonoBehaviour
{
    [Header("Refs")]
    public AnchorsRealtime anchors;

    [Header("Buttons")]
    public Button btnAddClue;
    public Button btnEditLocation;
    public Button btnEdit;
    public Button btnDeleteClue;
    public Button btnSave;

    [Header("Active Mode Visual")]
    [Tooltip("Normal color applied to a button when its mode is NOT active.")]
    public Color defaultButtonColor = Color.white;
    [Tooltip("Color applied to a button when its mode IS active.")]
    public Color activeModeColor = new Color(0.35f, 0.85f, 0.35f, 1f);

    // ---- internal mode state ----
    private enum EditMode { None, Location, Clue, Delete }
    private EditMode _activeMode = EditMode.None;

    // ---- Unity lifecycle ----

    void Start()
    {
        // OnEnable already runs before Start on first activation — nothing extra needed here.
    }

    void OnEnable()
    {
        Wire(); // wire every time so listeners are always fresh
        ApplyMode(EditMode.None);
        if (anchors != null)
        {
            anchors.OnDeleteModeExited += OnDeleteModeExitedExternally;
            anchors.creatorEditSession = true;
            anchors.RefreshAllVisibility();
        }
    }

    void OnDisable()
    {
        if (anchors != null)
        {
            anchors.creatorEditSession = false;
            anchors.ExitEditModes();
            anchors.ExitDeleteMode();
            anchors.OnDeleteModeExited -= OnDeleteModeExitedExternally;
            anchors.RefreshAllVisibility();
        }
    }

    // Called by AnchorsRealtime after a single-shot delete so the UI reflects the mode exit.
    void OnDeleteModeExitedExternally()
    {
        if (_activeMode == EditMode.Delete)
        {
            _activeMode = EditMode.None;
            RefreshButtonVisuals();
        }
    }

    // ---- Button wiring ----

    void Wire()
    {
        if (btnAddClue != null)
        {
            btnAddClue.onClick.RemoveListener(OnAddClue);
            btnAddClue.onClick.AddListener(OnAddClue);
        }

        if (btnEditLocation != null)
        {
            btnEditLocation.onClick.RemoveListener(OnToggleEditLocation);
            btnEditLocation.onClick.AddListener(OnToggleEditLocation);
        }

        if (btnEdit != null)
        {
            btnEdit.onClick.RemoveListener(OnToggleClueEdit);
            btnEdit.onClick.AddListener(OnToggleClueEdit);
        }

        if (btnDeleteClue != null)
        {
            btnDeleteClue.onClick.RemoveListener(OnToggleDelete);
            btnDeleteClue.onClick.AddListener(OnToggleDelete);
        }

        if (btnSave != null)
        {
            btnSave.onClick.RemoveListener(OnSave);
            btnSave.onClick.AddListener(OnSave);
        }
    }

    // ---- Button handlers ----

    void OnAddClue()
    {
        if (anchors == null) return;

        // Exit any active mode before placing a new clue so a tap on the new
        // anchor doesn't immediately open ShowEditClue or move it by drag.
        ApplyMode(EditMode.None);
        anchors.PlaceHere();
    }

    void OnToggleEditLocation()
    {
        if (anchors == null) return;

        // Toggle: pressing the active button again turns it off.
        ApplyMode(_activeMode == EditMode.Location ? EditMode.None : EditMode.Location);
    }

    void OnToggleClueEdit()
    {
        if (anchors == null) return;

        ApplyMode(_activeMode == EditMode.Clue ? EditMode.None : EditMode.Clue);
    }

    public void OnToggleDelete()
    {
        if (anchors == null) return;

        ApplyMode(_activeMode == EditMode.Delete ? EditMode.None : EditMode.Delete);
    }

    void OnSave()
    {
        if (anchors == null) return;

        // Exit all modes before saving so the state is clean.
        ApplyMode(EditMode.None);
        anchors.SaveAllAnchorTransforms();

        // Konum değişikliği map içeriğini değiştirir → yeniden onay gerekir.
        anchors.ResetMapApprovalToPending();
    }

    // ---- Mode management ----

    /// Single source of truth for mode transitions.
    /// Tells AnchorsRealtime which mode to enter, then refreshes button visuals.
    private void ApplyMode(EditMode mode)
    {
        _activeMode = mode;

        if (anchors != null)
        {
            switch (mode)
            {
                case EditMode.Location:
                    anchors.ExitDeleteMode();
                    anchors.EnterLocationEditMode();
                    break;
                case EditMode.Clue:
                    anchors.ExitDeleteMode();
                    anchors.EnterClueEditMode();
                    break;
                case EditMode.Delete:
                    anchors.ExitEditModes();
                    anchors.OnClickDeleteClue();
                    break;
                default:
                    anchors.ExitEditModes();
                    anchors.ExitDeleteMode();
                    break;
            }
        }

        RefreshButtonVisuals();
    }

    private void RefreshButtonVisuals()
    {
        SetButtonActive(btnEditLocation, _activeMode == EditMode.Location);
        SetButtonActive(btnEdit,         _activeMode == EditMode.Clue);
        SetButtonActive(btnDeleteClue,   _activeMode == EditMode.Delete);
    }

    private void SetButtonActive(Button btn, bool isActive)
    {
        if (btn == null) return;

        var colors = btn.colors;
        colors.normalColor = isActive ? activeModeColor : defaultButtonColor;
        btn.colors = colors;
    }
}
