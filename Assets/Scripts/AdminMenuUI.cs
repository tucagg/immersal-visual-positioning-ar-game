using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Firebase.Database;
using Firebase.Extensions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

public class AdminMenuUI : MonoBehaviour
{
    [Header("Refs")]
    public AnchorsRealtime anchors;
    public GameObject panel;  // AdminPanel

    [Header("Hierarchy (Edit) UI")]
    [Tooltip("Seçili map'i almak için. AnchorsRealtime içinden de gelir ama burada açıkça referans alıyoruz.")]
    public MapRootProvider mapRootProvider;

    [Tooltip("Clue listesi için panel root (ScrollView paneli gibi).")]
    public GameObject hierarchyPanel;

    [Tooltip("ScrollView/Content (VerticalLayoutGroup olan) - butonlar buraya eklenecek.")]
    public RectTransform hierarchyContent;

    [Tooltip("(Eski şema) /maps/{mapId}/clues içindi. Sende clue'lar anchors/{mapId}/{clueId} altında olduğu için kullanılmıyor.")]
    public string cluesPathKey = "clues";

    [Tooltip("Anchors root path (DB). Sende: anchors/{mapId}/{clueId}/...")]
    public string anchorsRootKey = "anchors";

    [Tooltip("Clue index alan adı: 'index' veya 'clueIndex' olabilir. Okumada ikisini de dener.")]
    public string clueIndexFieldPrimary = "clueIndex";

    [Tooltip("Alternatif index alan adı (opsiyonel)")]
    public string clueIndexFieldAlt = "index";

    [Tooltip("Clue ad alanı (opsiyonel)")]
    public string clueNameField = "clueName";

    [Tooltip("Bir butonun minimum yüksekliği (UI)")]
    public float hierarchyRowMinHeight = 72f;

    private bool _hierarchyLoading;

    [Header("UI Buttons")]
    public GameObject plusButton; // the + button itself

    [Header("Map Admin")]
    public MapAdminUI mapAdminUI; // MapAdminUI referansı

    [Header("Overlay")]
    public GameObject closeOverlay;

    [Header("Sub Panels")]
    public GameObject clueEditPanel;      // Panel for editing clues (drag + save)
    public GameObject interactionPanel;   // Panel for interaction options
    public GameObject popupEditorPanel;   // Panel for popup message editor
    public GameObject puzzleEditorPanel;  // Panel for puzzle editor
    public GameObject prefabSelectorPanel; // Panel for changing object type (ScrollView)
    public GameObject clueNamePanel; // Panel for changing clue names

    [Header("Clue Edit UI")]
    public GameObject clueEditModeButtons;   // Change Location / Change Object Type butonlarının bulunduğu container
    public GameObject clueEditLocationHint;  // "Drag clues to new positions..." Text objesi (veya container'ı)
    public GameObject clueEditSaveButton;    // Save positions button in clue edit panel
    public GameObject clueNameHint;          // Hint text for clue name editing

    // "+" butonuna bağlanacak
    public void ToggleMenu()
    {
        if (panel == null) return;

        bool anySubOpen =
            (clueEditPanel != null && clueEditPanel.activeSelf) ||
            (interactionPanel != null && interactionPanel.activeSelf) ||
            (popupEditorPanel != null && popupEditorPanel.activeSelf) ||
            (puzzleEditorPanel != null && puzzleEditorPanel.activeSelf) ||
            (prefabSelectorPanel != null && prefabSelectorPanel.activeSelf);

        // Eğer ana panel AÇIKSA veya alt panellerden biri AÇIKSA → hepsini kapat
        if (panel.activeSelf || anySubOpen)
        {
            panel.SetActive(false);
            if (clueEditPanel != null) clueEditPanel.SetActive(false);
            if (interactionPanel != null) interactionPanel.SetActive(false);
            if (popupEditorPanel != null) popupEditorPanel.SetActive(false);
            if (puzzleEditorPanel != null) puzzleEditorPanel.SetActive(false);
            if (prefabSelectorPanel != null) prefabSelectorPanel.SetActive(false);
            if (clueNamePanel != null) clueNamePanel.SetActive(false);
            if (clueEditModeButtons != null) clueEditModeButtons.SetActive(false);
            if (clueEditLocationHint != null) clueEditLocationHint.SetActive(false);
            if (clueEditSaveButton != null) clueEditSaveButton.SetActive(false);
            if (clueNameHint != null) clueNameHint.SetActive(false);

            if (anchors != null)
            {
                anchors.ExitEditCluesMode();
                anchors.ExitAdminModes();
            }

            if (closeOverlay != null) closeOverlay.SetActive(false);
            if (plusButton != null) plusButton.SetActive(true);
        }
        else
        {
            // Hiçbiri açık değilse → ana admin paneli aç
            panel.SetActive(true);
            if (plusButton != null) plusButton.SetActive(false);
            if (closeOverlay != null) closeOverlay.SetActive(true);
        }
    }

    // "Edit hierarchy" - şimdilik sadece modları kapatıyor, ileride genişletiriz
    public void OnEditHierarchyClicked()
    {
        if (hierarchyPanel == null || hierarchyContent == null)
        {
            Debug.LogWarning("[AdminMenu] Hierarchy panel/content not assigned.");
            return;
        }

        // Ana admin modlarını kapat
        if (anchors != null)
            anchors.ExitAdminModes();

        // Paneller
        if (panel != null) panel.SetActive(false);
        hierarchyPanel.SetActive(true);

        // Overlay ve + buton davranışı
        if (closeOverlay != null) closeOverlay.SetActive(true);
        if (plusButton != null) plusButton.SetActive(false);

        // Map id
        int currentMapId = 0;
        if (mapRootProvider != null) currentMapId = mapRootProvider.mapId;
        else if (anchors != null && anchors.mapRootProvider != null) currentMapId = anchors.mapRootProvider.mapId;

        if (currentMapId == 0)
        {
            Debug.LogWarning("[AdminMenu] No map selected (mapId=0). Cannot load hierarchy list.");
            ClearHierarchyContent();
            AddHierarchyMessageRow("Map seçili değil");
            return;
        }

        StartCoroutine(LoadAndBuildHierarchy(currentMapId));
    }


    // "Add object" - mevcut PlaceHere akışını tetikler
    public void OnAddObjectClicked()
    {
        if (anchors == null) return;
        anchors.PlaceHere();
        Debug.Log("[AdminMenu] Add object pressed → PlaceHere().");
        panel.SetActive(false);
    }

    // "Delete object" - delete moduna geç
    public void OnDeleteObjectClicked()
    {
        if (anchors == null) return;

        anchors.EnterDeleteMode();
        Debug.Log("[AdminMenu] Delete mode ON. Tap anchor to delete.");

        // Admin paneli kapat
        if (panel != null)
            panel.SetActive(false);

        // Delete mode'da overlay kapalı olsun ki ilk tık doğrudan objeye gitsin
        if (closeOverlay != null)
            closeOverlay.SetActive(false);

        // Kullanıcı isterse menüyü tekrar açabilsin diye + butonunu göster
        if (plusButton != null)
            plusButton.SetActive(true);
    }

    // "Edit Clues" - open clue edit panel where objects can be dragged and then saved
    public void OnEditCluesClicked()
    {
        if (anchors == null || clueEditPanel == null) return;

        // Reset all admin modes; user will choose location vs type from inside the panel
        anchors.ExitAdminModes();
        Debug.Log("[AdminMenu] Edit Clues clicked. Choose location or object type.");

        clueEditPanel.SetActive(true);
        if (clueEditModeButtons != null) clueEditModeButtons.SetActive(true);
        if (prefabSelectorPanel != null) prefabSelectorPanel.SetActive(false);
        if (clueNamePanel != null) clueNamePanel.SetActive(false);
        if (clueEditLocationHint != null) clueEditLocationHint.SetActive(false);
        if (clueEditSaveButton != null) clueEditSaveButton.SetActive(false);
        if (clueNameHint != null) clueNameHint.SetActive(false);

        if (panel != null) panel.SetActive(false);
        if (closeOverlay != null) closeOverlay.SetActive(false);
        if (plusButton != null) plusButton.SetActive(true);
    }

    // Button to enter location edit mode (drag clues)
    public void OnChangeLocationClicked()
    {
        if (anchors == null) return;

        anchors.EnterLocationEditMode();

        if (clueEditLocationHint != null)
            clueEditLocationHint.SetActive(true);

        // Show the save button only in location edit mode
        if (clueEditSaveButton != null)
            clueEditSaveButton.SetActive(true);

        // In location mode we only drag positions; no need for the prefab selector or clue name panel
        if (prefabSelectorPanel != null)
            prefabSelectorPanel.SetActive(false);
        if (clueNamePanel != null)
            clueNamePanel.SetActive(false);
        if (clueNameHint != null)
            clueNameHint.SetActive(false);

        Debug.Log("[AdminMenu] Change Location mode ON. Drag clues, then press Save.");
    }

    // Button to enter prefab edit mode (change object type)
    public void OnChangeTypeClicked()
    {
        if (anchors == null) return;

        anchors.EnterPrefabEditMode();
        if (clueEditLocationHint != null)
            clueEditLocationHint.SetActive(false);
        if (clueEditSaveButton != null)
            clueEditSaveButton.SetActive(false);
        if (clueNamePanel != null)
            clueNamePanel.SetActive(false);
        if (clueNameHint != null)
            clueNameHint.SetActive(false);

        // Eski prefab seçimlerini temizle
        var selector = UnityEngine.Object.FindFirstObjectByType<PrefabSelectorUI>();
        if (selector != null)
        {
            selector.ClearSelection();
        }

        // In prefab mode we show the selector panel
        if (prefabSelectorPanel != null)
            prefabSelectorPanel.SetActive(true);

        Debug.Log("[AdminMenu] Change Object Type mode ON. Tap a clue, then choose prefab.");
    }

    public void OnChangeClueNameClicked()
    {
        if (anchors == null) return;

        anchors.EnterClueNameEditMode();

        // hide other hints and panels
        if (clueEditLocationHint != null) clueEditLocationHint.SetActive(false);
        if (prefabSelectorPanel != null) prefabSelectorPanel.SetActive(false);
        if (clueEditSaveButton != null) clueEditSaveButton.SetActive(false);

        // show clue name panel
        if (clueNamePanel != null) clueNamePanel.SetActive(true);
        if (clueNameHint != null) clueNameHint.SetActive(true);

        Debug.Log("[AdminMenu] Change Clue Name mode ON. Tap a clue, then edit its name.");
    }

    // Button on the clue edit panel: save all current anchor transforms to Firebase
    public void OnSaveCluePositionsClicked()
    {
        if (anchors == null) return;

        anchors.SaveAllAnchorTransforms();
        anchors.ExitEditCluesMode();
        Debug.Log("[AdminMenu] Clue positions saved.");
        if (clueEditPanel != null) clueEditPanel.SetActive(false);
    }

    // "Add Interaction" - open interaction options panel
    public void OnAddInteractionClicked()
    {
        if (interactionPanel == null) return;
        Debug.Log("[AdminMenu] Add Interaction clicked.");
        interactionPanel.SetActive(true);
        if (panel != null) panel.SetActive(false);
        if (closeOverlay != null) closeOverlay.SetActive(false);
        if (plusButton != null) plusButton.SetActive(true);
    }

    // "Add popup message" - start selection of an anchor and then open popup editor
    public void OnAddPopupMessageClicked()
    {
        if (anchors == null) return;

        anchors.BeginSelectAnchorForPopup();
        Debug.Log("[AdminMenu] Add popup message: tap an object to select anchor.");

        if (popupEditorPanel != null)
        {
            popupEditorPanel.SetActive(true);
        }

        if (interactionPanel != null)
        {
            interactionPanel.SetActive(false);
        }

        if (panel != null)
        {
            panel.SetActive(false);
        }

        if (closeOverlay != null) closeOverlay.SetActive(false);
        if (plusButton != null) plusButton.SetActive(true);
    }

    public void OnMapAdminClicked()
    {
        // 1) Map admin panelini aç/kapat
        if (mapAdminUI != null)
        {
            mapAdminUI.TogglePanel();
        }

        // 2) Eski admin paneli kapat
        if (panel != null)
        {
            panel.SetActive(false);
        }

        if (closeOverlay != null)
        {
            bool mapAdminOpen = (mapAdminUI != null && mapAdminUI.panelRoot != null && mapAdminUI.panelRoot.activeSelf);
            closeOverlay.SetActive(mapAdminOpen);
        }
    }
    // Overlay'den tüm admin panellerini kapatmak için kullanılacak method

    public void CloseAllPanels()
    {
        // Ana admin paneli kapat
        if (panel != null)
            panel.SetActive(false);

        if (hierarchyPanel != null)
            hierarchyPanel.SetActive(false);

        // Alt panelleri kapat
        if (clueEditPanel != null)
            clueEditPanel.SetActive(false);
        if (interactionPanel != null)
            interactionPanel.SetActive(false);
        if (popupEditorPanel != null)
            popupEditorPanel.SetActive(false);
        if (puzzleEditorPanel != null)
            puzzleEditorPanel.SetActive(false);
        if (prefabSelectorPanel != null)
            prefabSelectorPanel.SetActive(false);
        if (clueNamePanel != null) clueNamePanel.SetActive(false);
        if (clueEditModeButtons != null)
            clueEditModeButtons.SetActive(false);
        if (clueEditLocationHint != null)
            clueEditLocationHint.SetActive(false);
        if (clueEditSaveButton != null)
            clueEditSaveButton.SetActive(false);
        if (clueNameHint != null) clueNameHint.SetActive(false);

        // Map admin panelini kapat
        if (mapAdminUI != null && mapAdminUI.panelRoot != null)
            mapAdminUI.panelRoot.SetActive(false);

        // Overlay'i kapat
        if (closeOverlay != null)
            closeOverlay.SetActive(false);

        if (plusButton != null)
            plusButton.SetActive(true);

        Debug.Log("[AdminMenu] All panels closed via overlay click.");
    }

    // ---------------- Hierarchy (Clue Index Editor) ----------------

    private DatabaseReference DB => FirebaseInitializer.DB;

    private void ClearHierarchyContent()
    {
        if (hierarchyContent == null) return;
        for (int i = hierarchyContent.childCount - 1; i >= 0; i--)
            Destroy(hierarchyContent.GetChild(i).gameObject);
    }

    private void AddHierarchyMessageRow(string message)
    {
        if (hierarchyContent == null) return;

        var row = new GameObject("MessageRow", typeof(RectTransform));
        row.transform.SetParent(hierarchyContent, false);

        var tmp = row.AddComponent<TextMeshProUGUI>();
        tmp.text = message;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = 32;
        tmp.color = Color.white;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;

        var le = row.AddComponent<LayoutElement>();
        le.minHeight = hierarchyRowMinHeight;
    }

    private class ClueRow
    {
        public string clueId;
        public string displayName;
        public int index;
    }

    private IEnumerator LoadAndBuildHierarchy(int mapId)
    {
        if (_hierarchyLoading) yield break;
        _hierarchyLoading = true;

        // Firebase hazır olana kadar bekle
        float wait = 0f;
        while (!FirebaseInitializer.Ready && wait < 10f)
        {
            wait += Time.unscaledDeltaTime;
            yield return null;
        }

        if (!FirebaseInitializer.Ready || DB == null)
        {
            _hierarchyLoading = false;
            ClearHierarchyContent();
            AddHierarchyMessageRow("Firebase hazır değil");
            yield break;
        }

        bool done = false;
        Exception err = null;
        DataSnapshot snap = null;

        // DB: anchors/{mapId}/{clueId}/...
        DB.Child(anchorsRootKey).Child(mapId.ToString())
            .GetValueAsync()
            .ContinueWithOnMainThread(t =>
            {
                try
                {
                    if (t.IsCompletedSuccessfully) snap = t.Result;
                    done = true;
                }
                catch (Exception e)
                {
                    err = e;
                    done = true;
                }
            });

        while (!done) yield return null;

        ClearHierarchyContent();

        if (err != null)
        {
            Debug.LogWarning("[AdminMenu] Load hierarchy error: " + err);
            AddHierarchyMessageRow("Liste alınamadı");
            _hierarchyLoading = false;
            yield break;
        }

        if (snap == null || !snap.Exists)
        {
            AddHierarchyMessageRow("Clue bulunamadı");
            _hierarchyLoading = false;
            yield break;
        }

        var rows = new List<ClueRow>();
        foreach (var child in snap.Children)
        {
            string id = child.Key;

            // Eğer bu node bir clue değilse (ör: boş / farklı yapı), atla
            // (Clue node'larında en azından clueIndex veya clueName bekliyoruz)
            bool looksLikeClue = (child.Child(clueIndexFieldPrimary).Exists || (!string.IsNullOrEmpty(clueIndexFieldAlt) && child.Child(clueIndexFieldAlt).Exists) || child.Child(clueNameField).Exists);
            if (!looksLikeClue)
                continue;

            // name (optional)
            string nm = null;
            if (child.Child(clueNameField).Exists && child.Child(clueNameField).Value != null)
                nm = child.Child(clueNameField).Value.ToString();

            // index (optional)
            int idx = 0;
            bool hasIdx = false;

            if (child.Child(clueIndexFieldPrimary).Exists && child.Child(clueIndexFieldPrimary).Value != null)
                hasIdx = int.TryParse(child.Child(clueIndexFieldPrimary).Value.ToString(), out idx);

            if (!hasIdx && !string.IsNullOrEmpty(clueIndexFieldAlt) && child.Child(clueIndexFieldAlt).Exists && child.Child(clueIndexFieldAlt).Value != null)
                hasIdx = int.TryParse(child.Child(clueIndexFieldAlt).Value.ToString(), out idx);

            string display = string.IsNullOrWhiteSpace(nm) ? id : nm;
            rows.Add(new ClueRow { clueId = id, displayName = display, index = hasIdx ? idx : 0 });
        }

        // Sort: index asc; same index -> alphabetic
        var sorted = rows
            .OrderBy(r => r.index)
            .ThenBy(r => r.displayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var r in sorted)
            CreateHierarchyButtonRow(mapId, r);

        _hierarchyLoading = false;
    }

    private void CreateHierarchyButtonRow(int mapId, ClueRow row)
    {
        // Root button row
        var go = new GameObject($"ClueRow_{row.clueId}", typeof(RectTransform), typeof(Image), typeof(Button), typeof(HorizontalLayoutGroup));
        go.transform.SetParent(hierarchyContent, false);

        var img = go.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.08f);

        var hlg = go.GetComponent<HorizontalLayoutGroup>();
        hlg.padding = new RectOffset(16, 16, 12, 12);
        hlg.spacing = 16;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = false;

        var le = go.AddComponent<LayoutElement>();
        le.minHeight = hierarchyRowMinHeight;

        // Index label
        var idxGO = new GameObject("Index", typeof(RectTransform), typeof(TextMeshProUGUI));
        idxGO.transform.SetParent(go.transform, false);
        var idxTMP = idxGO.GetComponent<TextMeshProUGUI>();
        idxTMP.text = row.index.ToString();
        idxTMP.alignment = TextAlignmentOptions.MidlineLeft;
        idxTMP.fontSize = 34;
        idxTMP.color = Color.white;
        idxTMP.textWrappingMode = TextWrappingModes.NoWrap;

        var idxLE = idxGO.AddComponent<LayoutElement>();
        idxLE.preferredWidth = 90;
        idxLE.flexibleWidth = 0;

        // Name label (NoWrap + Ellipsis)
        var nameGO = new GameObject("Name", typeof(RectTransform), typeof(TextMeshProUGUI));
        nameGO.transform.SetParent(go.transform, false);
        var nameTMP = nameGO.GetComponent<TextMeshProUGUI>();
        nameTMP.text = row.displayName;
        nameTMP.alignment = TextAlignmentOptions.MidlineLeft;
        nameTMP.fontSize = 34;
        nameTMP.color = Color.white;
        nameTMP.textWrappingMode = TextWrappingModes.NoWrap;
        nameTMP.overflowMode = TextOverflowModes.Ellipsis;

        var nameLE = nameGO.AddComponent<LayoutElement>();
        nameLE.flexibleWidth = 1;
        nameLE.minWidth = 0;

        // Click -> numeric input -> write index -> refresh
        var btn = go.GetComponent<Button>();
        btn.onClick.AddListener(() => StartCoroutine(EditClueIndex(mapId, row.clueId, row.index)));
    }

    private IEnumerator EditClueIndex(int mapId, string clueId, int currentIndex)
    {
        var kb = TouchScreenKeyboard.Open(currentIndex.ToString(), TouchScreenKeyboardType.NumberPad, false, false, false, false, "New index");
        if (kb == null) yield break;

        // done / wasCanceled deprecated -> status kullan
        while (kb.status == TouchScreenKeyboard.Status.Visible)
            yield return null;

        if (kb.status == TouchScreenKeyboard.Status.Canceled)
            yield break;

        // Bazı cihazlarda Done yerine LostFocus gelebiliyor; yine de text'i parse etmeyi deniyoruz.
        if (!int.TryParse(kb.text, out int newIndex))
        {
            Debug.LogWarning($"[AdminMenu] Invalid index input: '{kb.text}'");
            yield break;
        }

        bool done = false;
        Exception err = null;

        DB.Child(anchorsRootKey).Child(mapId.ToString()).Child(clueId).Child(clueIndexFieldPrimary)
            .SetValueAsync(newIndex)
            .ContinueWithOnMainThread(t =>
            {
                if (t.IsFaulted) err = t.Exception;
                done = true;
            });

        while (!done) yield return null;

        if (err != null)
        {
            Debug.LogWarning("[AdminMenu] Failed to set clue index: " + err);
            yield break;
        }

        StartCoroutine(LoadAndBuildHierarchy(mapId));
    }
    // "Add puzzle" - start selection of an anchor and then open puzzle editor
    public void OnAddPuzzleClicked()
    {
        if (anchors == null) return;
        anchors.BeginSelectAnchorForPuzzle();
        Debug.Log("[AdminMenu] Add puzzle: tap an object to select anchor.");
        if (puzzleEditorPanel != null)
        {
            puzzleEditorPanel.SetActive(true);
        }
        if (interactionPanel != null)
        {
            interactionPanel.SetActive(false);
        }
        if (panel != null)
        {
            panel.SetActive(false);
        }
        if (closeOverlay != null) closeOverlay.SetActive(false);
        if (plusButton != null) plusButton.SetActive(true);
    }

}
