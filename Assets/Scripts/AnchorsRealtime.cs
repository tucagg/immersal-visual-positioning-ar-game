using System;
using System.Collections.Generic;
using System.Globalization;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class AnchorsRealtime : MonoBehaviour
{
    [Header("Refs")]
    public MapRootProvider mapRootProvider;   // XR Map manual7 burada
    public GameObject placedPrefab;           // Varsayılan küp prefab

    [System.Serializable]
    public class PrefabOption
    {
        public string key;           // DB'ye yazılacak anahtar (ör: "cube", "key", "note")
        public GameObject prefab;    // Gerçek prefab
        public string displayName;   // UI'da gözükecek isim
    }

    [Header("Prefab Library")]
    public List<PrefabOption> prefabOptions = new List<PrefabOption>();
    public string defaultPrefabKey = "cube";

    public float forwardMeters = 2f;       // Kameranın önüne kaç metre

    [Header("Anchor Defaults")]
    public int defaultClueIndex = 1;
    public bool defaultVisible = true;
    public bool defaultSolved = false;

    [Header("User UI")]
    public CluePopupUI userPopupUI;

    [Tooltip("Assign the PuzzlePopUpUI component here (kept as MonoBehaviour to avoid compile dependency).")]
    public PuzzlePopUpUI puzzlePopupUI;

    [Header("Logout UI")]
    [Tooltip("Bottom-right logo button. Tapping this toggles the logout button/panel.")]
    public Button logoButton;

    [Tooltip("Root GameObject of the logout button or panel that should appear/disappear.")]
    public GameObject logoutButtonRoot;

    [Tooltip("Full-screen invisible overlay button that closes the logout UI when tapped. Optional but recommended.")]
    public Button logoutOverlayButton;

    private bool _logoutMenuOpen = false;

    private DatabaseReference DB => FirebaseInitializer.DB;
    private bool FirebaseReady => FirebaseInitializer.Ready;

    // Bu cihazda spawn'ladıklarımız (id -> GO)
    private readonly Dictionary<string, GameObject> _spawned = new();

    // Anchor metadata (id -> meta)
    private readonly Dictionary<string, AnchorMeta> _anchors = new();

    // Current map subscription tracking (so we can unsubscribe on map switch)
    private DatabaseReference _currentMapRef;
    private EventHandler<ChildChangedEventArgs> _onChildAdded;
    private EventHandler<ChildChangedEventArgs> _onChildChanged;

    // The mapId for which anchors are currently displayed/subscribed
    private int _activeAnchorsMapId = -1;

    // Subscribe flag
    private bool _subscribed = false;

    // -------- User progress (per-user) --------
    [Header("User Progress (Per-User)")]
    [Tooltip("DB root for users. Progress will be stored under users/{uid}/progress/{mapId}/solved/{clueId}=true")]
    public string usersRootKey = "users";

    [Tooltip("Child key under users/{uid} for progress data")]
    public string progressKey = "progress";

    // uid -> solved clueIds for current map (we only cache current user's current map here)
    private readonly HashSet<string> _userSolvedIds = new();

    // Current unlocked index for the user in this map
    private int _unlockedClueIndex = 1;

    // -------- Timing (ms precision) --------
    [Header("Debug Timing")]
    [Tooltip("Logs anchor click time and popup show time with ms precision, including delta.")]
    public bool logPopupTiming = true;

    private double _lastAnchorClickRealtime;
    private string _lastAnchorClickId;

    private void LogPopupShowTiming(string popupName, string anchorId)
    {
        if (!logPopupTiming) return;

        double now = Time.realtimeSinceStartupAsDouble;
        double deltaMs = (_lastAnchorClickId == anchorId)
            ? (now - _lastAnchorClickRealtime) * 1000.0
            : double.NaN;

        string nowS = now.ToString("F6", CultureInfo.InvariantCulture);
        string deltaS = double.IsNaN(deltaMs) ? "(unknown)" : deltaMs.ToString("F3", CultureInfo.InvariantCulture) + "ms";

        Debug.Log($"[Anchors][Timing] {popupName} SHOW for anchor={anchorId} at t={nowS}s, deltaFromClick={deltaS}");
    }

    private string CurrentUid
    {
        get
        {
            // Prefer AuthManager if present
            if (AuthManager.Instance != null && AuthManager.Instance.CurrentUser != null)
                return AuthManager.Instance.CurrentUser.UserId;

            // Fallback: FirebaseAuth directly (if you later remove AuthManager)
            try
            {
                var u = Firebase.Auth.FirebaseAuth.DefaultInstance?.CurrentUser;
                return u != null ? u.UserId : null;
            }
            catch
            {
                return null;
            }
        }
    }

    #region Admin
    [Header("Admin")]
    public bool adminMode = false;
    private bool _linkMode = false;
    private bool _deleteMode = false;
    private string _pendingLinkAnchorId = null;
    private bool _waitingForPuzzleAnchor = false;
    private string _currentPuzzleAnchorId = null;

    // Popup selection state
    private bool _waitingForPopupAnchor = false;
    private string _currentPopupAnchorId = null;

    // Edit modes
    private bool _editLocationMode = false;   // drag/move clues
    private bool _editPrefabMode = false;     // change object type
    private bool _editClueNameMode = false;   // change clue name

    // Currently selected anchor for editing
    private string _currentEditAnchorId = null;   // for prefab edit
    private string _currentNameAnchorId = null;   // for clue name edit
    #endregion

    private class AnchorMeta
    {
        public int mapId;
        public int clueIndex;
        public bool visible;
        public bool solved;
        public string prefabKey;
        public string clueName;

        public string clueType;

        // puzzle data (optional)
        public string puzzleHint;
        public string puzzlePassword;
        public string puzzleSolvedMessage;
    }

    void Start()
    {
        // Localize + Firebase hazır olduğunda bir kere subscribe ol
        InvokeRepeating(nameof(TrySubscribeOnce), 1f, 1f);

        // Subscribe to map selection changes
        if (mapRootProvider != null)
        {
            mapRootProvider.OnMapSelected -= HandleMapSelected;
            mapRootProvider.OnMapSelected += HandleMapSelected;
        }

        // Ensure logout UI starts hidden and wire up toggle/close.
        SetLogoutMenuOpen(false);

        if (logoButton != null)
            logoButton.onClick.AddListener(ToggleLogoutMenu);

        if (logoutOverlayButton != null)
            logoutOverlayButton.onClick.AddListener(CloseLogoutMenu);
    }
    // ----------- Map Switching & Anchor Clearing -----------

    private void HandleMapSelected(int newMapId, string newMapName)
    {
        if (newMapId == _activeAnchorsMapId) return;

        Debug.Log($"[Anchors] Map changed -> {newMapId}. Restarting scene for a clean state (keep auth). ");
        _activeAnchorsMapId = newMapId;

        // Unsubscribe to avoid callbacks during teardown
        UnsubscribeCurrentMap();

        // Close popups (avoid lingering UI)
        if (userPopupUI != null) userPopupUI.Hide();
        if (puzzlePopupUI != null) puzzlePopupUI.Hide();

        // Preferred: full reset via AuthManager, but keep the signed-in session.
        if (AuthManager.Instance != null)
        {
            AuthManager.Instance.ReloadSceneForMapChangeKeepAuth(newMapId, newMapName);
            return;
        }
    }

    private void UnsubscribeCurrentMap()
    {
        if (_currentMapRef == null) return;

        try
        {
            if (_onChildAdded != null)
                _currentMapRef.ChildAdded -= _onChildAdded;
            if (_onChildChanged != null)
                _currentMapRef.ChildChanged -= _onChildChanged;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Anchors] Unsubscribe error: " + e);
        }
        finally
        {
            _currentMapRef = null;
            _onChildAdded = null;
            _onChildChanged = null;
        }
    }

    private void SetLogoutMenuOpen(bool open)
    {
        _logoutMenuOpen = open;

        if (logoutButtonRoot != null)
            logoutButtonRoot.SetActive(open);

        if (logoutOverlayButton != null)
            logoutOverlayButton.gameObject.SetActive(open);
    }

    public void ToggleLogoutMenu()
    {
        SetLogoutMenuOpen(!_logoutMenuOpen);
    }

    public void CloseLogoutMenu()
    {
        SetLogoutMenuOpen(false);
    }

    void TrySubscribeOnce()
    {
        if (_subscribed) { CancelInvoke(nameof(TrySubscribeOnce)); return; }
        if (!FirebaseReady || mapRootProvider == null || !mapRootProvider.IsLocalized) return;

        _subscribed = true;
        CancelInvoke(nameof(TrySubscribeOnce));

        int mapId = mapRootProvider.mapId;

        // 1) Mevcutları bir kerelik yükle
        DB.Child("anchors").Child(mapId.ToString()).GetValueAsync().ContinueWithOnMainThread(t =>
        {
            if (t.IsCompletedSuccessfully && t.Result != null && t.Result.Exists)
            {
                foreach (var s in t.Result.Children)
                {
                    var (id, localPos, localRot) = LocalPoseFromSnapshot(s);
                    var meta = MetaFromSnapshot(s, mapId);
                    _anchors[id] = meta;
                    SpawnOrMoveLocal(id, localPos, localRot, meta.visible);
                }
                Debug.Log($"[Anchors] Loaded existing for map {mapId}");
            }
        });

        // 2) Canlı dinle (başkası koyarsa anında gör)
        UnsubscribeCurrentMap();

        _currentMapRef = DB.Child("anchors").Child(mapId.ToString());

        _onChildAdded = (sender, args) =>
        {
            var (id, localPos, localRot) = LocalPoseFromSnapshot(args.Snapshot);
            var meta = MetaFromSnapshot(args.Snapshot, mapId);
            _anchors[id] = meta;
            SpawnOrMoveLocal(id, localPos, localRot, meta.visible);
        };

        _onChildChanged = (sender, args) =>
        {
            var (id, localPos, localRot) = LocalPoseFromSnapshot(args.Snapshot);
            var meta = MetaFromSnapshot(args.Snapshot, mapId);
            _anchors[id] = meta;
            SpawnOrMoveLocal(id, localPos, localRot, meta.visible);
        };

        _currentMapRef.ChildAdded += _onChildAdded;
        _currentMapRef.ChildChanged += _onChildChanged;

        // Track which mapId we're currently subscribed to.
        _activeAnchorsMapId = mapId;

        // 3) User progress yükle ve hiyerarşik görünürlüğü uygula
        StartCoroutine(LoadUserProgressAndRefresh(mapId));
    }

    public void PlaceHere()
    {
        if (!FirebaseReady) { Debug.LogWarning("[Anchors] Firebase not ready"); return; }
        if (mapRootProvider == null || !mapRootProvider.IsLocalized)
        {
            Debug.LogWarning("[Anchors] Not localized yet (map root hazır değil)");
            return;
        }

        // 1) Kameranın önüne dünya uzayında bir poz/rot al
        var cam = Camera.main.transform;
        Vector3 wPos = cam.position + cam.forward * forwardMeters;
        Quaternion wRot = Quaternion.LookRotation(cam.forward, Vector3.up);

        // 2) Dünya -> HARİTA (XR Map) dönüşümü
        var map = mapRootProvider.mapRoot;
        Vector3 localPos = map.InverseTransformPoint(wPos);
        Quaternion localRot = Quaternion.Inverse(map.rotation) * wRot;

        // 3) Kaydı hazırla
        string id = Guid.NewGuid().ToString("N");
        int mapId = mapRootProvider.mapId;

        var meta = new AnchorMeta
        {
            mapId = mapId,
            clueIndex = defaultClueIndex,
            visible = defaultVisible,
            solved = defaultSolved,
            prefabKey = defaultPrefabKey,
            clueType = "default"
        };

        // Basit varsayılan isim: "Clue {index}"
        meta.clueName = $"Clue {meta.clueIndex}";

        var data = new Dictionary<string, object>
        {
            {"id", id},
            {"mapId", mapId},
            {"clueIndex", meta.clueIndex},
            {"visible", meta.visible},
           // {"solved", meta.solved}, //bunu kaldırdık çünkü per-user tutuyoruz
            {"prefabKey", meta.prefabKey},
            {"clueName", meta.clueName},   // 🔹 Yeni field
            {"clueType", "default"}, // 🔹 Yeni field
            {"localPos", new Dictionary<string, object>{{"x",localPos.x},{"y",localPos.y},{"z",localPos.z}}},
            {"localRot", new Dictionary<string, object>{{"x",localRot.x},{"y",localRot.y},{"z",localRot.z},{"w",localRot.w}}}
        };

        // 4) Firebase'e yaz
        DB.Child("anchors").Child(mapId.ToString()).Child(id).SetValueAsync(data);

        // 5) Bu cihazda da hemen spawn et (LOCAL pozla, map'in child'ı olarak)
        _anchors[id] = meta;
        SpawnOrMoveLocal(id, localPos, localRot, meta.visible);
    }

    // ----- Yardımcılar -----

    // Eski mantık: sadece local poz/rot çıkarıyoruz
    private (string id, Vector3 localPos, Quaternion localRot) LocalPoseFromSnapshot(DataSnapshot s)
    {
        string id = s.Child("id").Value.ToString();
        var lp = s.Child("localPos");
        var lr = s.Child("localRot");

        Vector3 localPos = new Vector3(
            Convert.ToSingle(lp.Child("x").Value),
            Convert.ToSingle(lp.Child("y").Value),
            Convert.ToSingle(lp.Child("z").Value)
        );
        Quaternion localRot = new Quaternion(
            Convert.ToSingle(lr.Child("x").Value),
            Convert.ToSingle(lr.Child("y").Value),
            Convert.ToSingle(lr.Child("z").Value),
            Convert.ToSingle(lr.Child("w").Value)
        );

        return (id, localPos, localRot);
    }

    // Metadata'yı snapshot'tan oku (clueIndex, visible, solved, prefabKey)
    private AnchorMeta MetaFromSnapshot(DataSnapshot s, int fallbackMapId)
    {
        int mapId = fallbackMapId;
        if (s.Child("mapId").Exists)
        {
            mapId = Convert.ToInt32(s.Child("mapId").Value);
        }

        int clueIndex = defaultClueIndex;
        if (s.Child("clueIndex").Exists)
        {
            clueIndex = Convert.ToInt32(s.Child("clueIndex").Value);
        }

        bool visible = defaultVisible;
        if (s.Child("visible").Exists)
        {
            visible = Convert.ToBoolean(s.Child("visible").Value);
        }

        bool solved = defaultSolved;
        if (s.Child("solved").Exists)
        {
            solved = Convert.ToBoolean(s.Child("solved").Value);
        }

        string prefabKey = defaultPrefabKey;
        if (s.Child("prefabKey").Exists && s.Child("prefabKey").Value != null)
        {
            prefabKey = s.Child("prefabKey").Value.ToString();
        }

        // clueName varsa kullan, yoksa basit bir fallback üret
        string clueName;
        if (s.Child("clueName").Exists && s.Child("clueName").Value != null)
        {
            clueName = s.Child("clueName").Value.ToString();
        }
        else
        {
            clueName = $"Clue {clueIndex}";
        }

        // ---- clueType ----
        string clueType = "default";
        if (s.Child("clueType").Exists && s.Child("clueType").Value != null)
        {
            clueType = s.Child("clueType").Value.ToString();
        }
        else
        {
            // clueType yoksa popupMessage varsa message say
            if (s.Child("popupMessage").Exists && s.Child("popupMessage").Value != null)
                clueType = "message";
        }

        // ---- puzzle (optional) ----
        string puzzleHint = null;
        string puzzlePassword = null;
        string puzzleSolvedMessage = null;

        if (s.Child("puzzle").Exists)
        {
            var p = s.Child("puzzle");
            if (p.Child("hint").Exists && p.Child("hint").Value != null)
                puzzleHint = p.Child("hint").Value.ToString();
            if (p.Child("password").Exists && p.Child("password").Value != null)
                puzzlePassword = p.Child("password").Value.ToString();
            if (p.Child("solvedMessage").Exists && p.Child("solvedMessage").Value != null)
                puzzleSolvedMessage = p.Child("solvedMessage").Value.ToString();

            if (!string.IsNullOrEmpty(puzzlePassword))
                clueType = "puzzle";
        }

        return new AnchorMeta
        {
            mapId = mapId,
            clueIndex = clueIndex,
            visible = visible,
            solved = solved,
            prefabKey = prefabKey,
            clueName = clueName,

            clueType = clueType,
            puzzleHint = puzzleHint,
            puzzlePassword = puzzlePassword,
            puzzleSolvedMessage = puzzleSolvedMessage
        };
    }

    private GameObject GetPrefabByKey(string key)
    {
        if (string.IsNullOrEmpty(key))
            key = defaultPrefabKey;

        foreach (var opt in prefabOptions)
        {
            if (opt != null && opt.prefab != null && opt.key == key)
                return opt.prefab;
        }

        // Bulamazsak fallback olarak default placedPrefab kullan
        return placedPrefab;
    }

    // Küpü mapRoot'un child'ı yap ve local poz/rot'u aynen uygula
    private void SpawnOrMoveLocal(string id, Vector3 localPos, Quaternion localRot, bool visible)
    {
        var map = mapRootProvider.mapRoot;
        if (map == null) return;

        // Bu anchor için meta varsa prefabKey kullan
        GameObject prefabToUse = placedPrefab;
        if (_anchors.TryGetValue(id, out var meta) && meta != null)
        {
            prefabToUse = GetPrefabByKey(meta.prefabKey);
        }

        if (!_spawned.TryGetValue(id, out var go) || go == null)
        {
            go = Instantiate(prefabToUse, map);   // parent = mapRoot

            // AnchorHandle varsa id'yi at
            var handle = go.GetComponent<AnchorHandle>();
            if (handle != null)
            {
                handle.anchorId = id;
            }

            _spawned[id] = go;
        }

        var t = go.transform;
        t.SetParent(map, false);          // emin ol
        t.localPosition = localPos;
        t.localRotation = localRot;

        // Visible flag: admin uses meta.visible; user uses hierarchical gating
        bool effectiveVisible = visible;
        if (_anchors.TryGetValue(id, out var meta2) && meta2 != null)
            effectiveVisible = ShouldBeVisibleForUser(id, meta2);

        go.SetActive(effectiveVisible);
    }

    // Save all current anchor transforms (clue positions) back to Firebase using local space (the same space as placement)
    public void SaveAllAnchorTransforms()
    {
        if (!FirebaseReady || mapRootProvider == null || !mapRootProvider.IsLocalized)
        {
            Debug.LogWarning("[Anchors][Admin] Cannot save transforms, not localized or Firebase not ready.");
            return;
        }

        int mapId = mapRootProvider.mapId;
        var map = mapRootProvider.mapRoot;
        if (map == null)
        {
            Debug.LogWarning("[Anchors][Admin] Map root is null, cannot save transforms.");
            return;
        }

        var refMap = DB.Child("anchors").Child(mapId.ToString());

        foreach (var kvp in _spawned)
        {
            string id = kvp.Key;
            var go = kvp.Value;
            if (go == null) continue;

            var t = go.transform;

            // Kullanılan yerleştirme mantığıyla aynı: mapRoot altında local uzayda sakla
            Vector3 localPos = t.localPosition;
            Quaternion localRot = t.localRotation;

            var poseData = new Dictionary<string, object>
            {
                {
                    "localPos",
                    new Dictionary<string, object>
                    {
                        {"x", localPos.x},
                        {"y", localPos.y},
                        {"z", localPos.z}
                    }
                },
                {
                    "localRot",
                    new Dictionary<string, object>
                    {
                        {"x", localRot.x},
                        {"y", localRot.y},
                        {"z", localRot.z},
                        {"w", localRot.w}
                    }
                }
            };

            refMap.Child(id).UpdateChildrenAsync(poseData);
        }

        Debug.Log("[Anchors][Admin] All anchor transforms saved to Firebase (local space).");
    }

    public void ChangePrefabForAnchor(string anchorId, string prefabKey)
    {
        if (!_anchors.TryGetValue(anchorId, out var meta))
        {
            Debug.LogWarning("[Anchors][Admin] No anchor meta for id: " + anchorId);
            return;
        }

        // Meta güncelle
        meta.prefabKey = prefabKey;
        _anchors[anchorId] = meta;

        // Mevcut GO'yu bul
        if (_spawned.TryGetValue(anchorId, out var go) && go != null)
        {
            var map = mapRootProvider.mapRoot;
            if (map == null) return;

            // Poz/rot'u kaydet
            Vector3 localPos = go.transform.localPosition;
            Quaternion localRot = go.transform.localRotation;

            Destroy(go);
            _spawned.Remove(anchorId);

            // Yeni prefab'ı instantiate et
            GameObject prefab = GetPrefabByKey(prefabKey);
            var newGo = Instantiate(prefab, map);
            var t = newGo.transform;
            t.localPosition = localPos;
            t.localRotation = localRot;

            var handle = newGo.GetComponent<AnchorHandle>();
            if (handle != null)
            {
                handle.anchorId = anchorId;
            }

            _spawned[anchorId] = newGo;
        }

        // Firebase'e yaz
        int mapId = meta.mapId;
        DB.Child("anchors")
          .Child(mapId.ToString())
          .Child(anchorId)
          .Child("prefabKey")
          .SetValueAsync(prefabKey);

        Debug.Log("[Anchors][Admin] Prefab changed for anchor " + anchorId + " -> " + prefabKey);
    }

    // ----- Popup message admin flow -----

    // Called by AdminMenu to begin selecting an anchor for popup message
    public void BeginSelectAnchorForPopup()
    {
        _waitingForPopupAnchor = true;
        _currentPopupAnchorId = null;
    }
    public void BeginSelectAnchorForPuzzle()
    {
        _waitingForPuzzleAnchor = true;
        _currentPuzzleAnchorId = null;
        Debug.Log("[Anchors][Admin] Select an anchor for PUZZLE...");
    }

    public string GetCurrentPuzzleAnchorId()
    {
        return _currentPuzzleAnchorId;
    }

    // Used by popup editor UI to know which anchor is currently selected for popup editing
    public string GetCurrentPopupAnchorId()
    {
        return _currentPopupAnchorId;
    }

    // Save a popup message string for the currently selected anchor
    public void SetPopupMessage(string message)
    {
        if (string.IsNullOrEmpty(_currentPopupAnchorId))
        {
            Debug.LogWarning("[Anchors][Admin] No anchor selected for popup message.");
            return;
        }

        if (!FirebaseReady || mapRootProvider == null || !mapRootProvider.IsLocalized)
        {
            Debug.LogWarning("[Anchors][Admin] Cannot save popup message, Firebase or localization not ready.");
            return;
        }

        // 🔒 PUZZLE GUARD — puzzle clue'lara popup eklenemez / silinemez
        if (_anchors.TryGetValue(_currentPopupAnchorId, out var existing) && existing != null &&
            string.Equals(existing.clueType, "puzzle", StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("[Anchors][Admin] This clue is PUZZLE. Popup Message cannot be edited. Use Add Puzzle.");
            return;
        }

        int mapId = mapRootProvider.mapId;
        var anchorRef = DB.Child("anchors")
                          .Child(mapId.ToString())
                          .Child(_currentPopupAnchorId);

        bool empty = string.IsNullOrWhiteSpace(message);

        // 🔹 Popup silme
        if (empty)
        {
            anchorRef.Child("popupMessage").RemoveValueAsync();
            anchorRef.Child("clueType").SetValueAsync("default");

            if (_anchors.TryGetValue(_currentPopupAnchorId, out var meta) && meta != null)
            {
                meta.clueType = "default";
                _anchors[_currentPopupAnchorId] = meta;
            }

            Debug.Log("[Anchors][Admin] Popup message cleared for anchor " + _currentPopupAnchorId);
            return;
        }

        // 🔹 Popup ekleme / güncelleme
        anchorRef.Child("popupMessage").SetValueAsync(message);
        anchorRef.Child("clueType").SetValueAsync("message");

        if (_anchors.TryGetValue(_currentPopupAnchorId, out var meta2) && meta2 != null)
        {
            meta2.clueType = "message";
            _anchors[_currentPopupAnchorId] = meta2;
        }

        Debug.Log("[Anchors][Admin] Popup message saved for anchor " + _currentPopupAnchorId);
    }

    public void SetPuzzleForSelectedAnchor(string hint, string password, string solvedMessage)
    {
        if (string.IsNullOrEmpty(_currentPuzzleAnchorId))
        {
            Debug.LogWarning("[Anchors][Admin] No anchor selected for puzzle.");
            return;
        }

        if (!FirebaseReady || mapRootProvider == null || !mapRootProvider.IsLocalized)
        {
            Debug.LogWarning("[Anchors][Admin] Cannot save puzzle, Firebase or localization not ready.");
            return;
        }

        int mapId = mapRootProvider.mapId;
        string anchorId = _currentPuzzleAnchorId;

        var updates = new Dictionary<string, object>
    {
        {"clueType", "puzzle"},
        {"puzzle/hint", hint ?? ""},
        {"puzzle/password", password ?? ""},
        {"puzzle/solvedMessage", solvedMessage ?? ""}
    };

        DB.Child("anchors").Child(mapId.ToString()).Child(anchorId).UpdateChildrenAsync(updates);

        // local cache
        if (_anchors.TryGetValue(anchorId, out var m) && m != null)
        {
            m.clueType = "puzzle";
            m.puzzleHint = hint;
            m.puzzlePassword = password;
            m.puzzleSolvedMessage = solvedMessage;
            _anchors[anchorId] = m;
        }
    }

    // Load popup message for a given anchor id and return via callback.
    public void GetPopupMessageForAnchor(string anchorId, System.Action<string> onResult)
    {
        if (string.IsNullOrEmpty(anchorId))
        {
            onResult?.Invoke(null);
            return;
        }

        if (!FirebaseReady || mapRootProvider == null)
        {
            Debug.LogWarning("[Anchors][Admin] Cannot load popup message, Firebase or mapRootProvider not ready.");
            onResult?.Invoke(null);
            return;
        }

        int mapId = mapRootProvider.mapId;
        DB.Child("anchors")
          .Child(mapId.ToString())
          .Child(anchorId)
          .Child("popupMessage")
          .GetValueAsync()
          .ContinueWithOnMainThread(t =>
          {
              if (t.IsCompletedSuccessfully && t.Result != null && t.Result.Exists && t.Result.Value != null)
              {
                  onResult?.Invoke(t.Result.Value.ToString());
              }
              else
              {
                  onResult?.Invoke(null);
              }
          });
    }

    // ----- User-side interaction -----
    public void OnAnchorClickedAsUser(string id)
    {
        // Use realtime clock for ms precision (not affected by Time.timeScale).
        double clickTime = Time.realtimeSinceStartupAsDouble;
        _lastAnchorClickRealtime = clickTime;
        _lastAnchorClickId = id;

        if (logPopupTiming)
        {
            string clickS = clickTime.ToString("F6", CultureInfo.InvariantCulture);
            Debug.Log($"[Anchors][Timing] Anchor CLICK id={id} at t={clickS}s");
        }

        Debug.Log($"[Anchors][User] Anchor clicked: {id}");

        if (userPopupUI == null)
        {
            Debug.LogWarning("[Anchors][User] userPopupUI is not assigned.");
            return;
        }

        if (!_anchors.TryGetValue(id, out var meta) || meta == null)
        {
            Debug.LogWarning("[Anchors][User] No meta found for anchor id: " + id);
            return;
        }

        int mapId = meta.mapId;

        string title = string.IsNullOrEmpty(meta.clueName)
            ? $"Clue {meta.clueIndex}"
            : meta.clueName;

        // 1) PUZZLE clue: show PuzzlePopUpUI, solved only after correct password
        if (string.Equals(meta.clueType, "puzzle", StringComparison.OrdinalIgnoreCase))
        {
            string hint = !string.IsNullOrEmpty(meta.puzzleHint)
                ? meta.puzzleHint
                : "(Puzzle)";

            // ✅ Daha önce çözüldüyse direkt sonucu göster
            if (_userSolvedIds.Contains(id))
            {
                string solvedMsg = !string.IsNullOrEmpty(meta.puzzleSolvedMessage)
                    ? meta.puzzleSolvedMessage
                    : "Doğru!";

                LogPopupShowTiming("CluePopupUI", id);
                userPopupUI.Show(solvedMsg, title);
                return;
            }

            if (string.IsNullOrEmpty(meta.puzzlePassword))
            {
                Debug.LogWarning("[Anchors][User] Puzzle clue has no password configured. Showing hint only.");
                LogPopupShowTiming("CluePopupUI", id);
                userPopupUI.Show(hint, title);
                return;
            }
            PuzzlePopup_SetFeedback(""); // Clear previous feedback
            LogPopupShowTiming("PuzzlePopUpUI", id);
            PuzzlePopup_Show(hint, title, (entered) =>
            {
                string input = (entered ?? string.Empty).Trim();
                string answer = (meta.puzzlePassword ?? string.Empty).Trim();

                if (string.Equals(input, answer, StringComparison.OrdinalIgnoreCase))
                {
                    PuzzlePopup_Hide();

                    string solvedMsg = !string.IsNullOrEmpty(meta.puzzleSolvedMessage)
                        ? meta.puzzleSolvedMessage
                        : "Doğru!";

                    LogPopupShowTiming("CluePopupUI", id);
                    userPopupUI.Show(solvedMsg, title);

                    MarkSolvedForCurrentUser(mapId, id);
                }
                else
                {
                    Debug.Log("[Anchors][User] Wrong puzzle password for anchor " + id);
                    PuzzlePopup_SetFeedback("Wrong answer, try again.");
                }
            });

            return;
        }

        // 2) MESSAGE / DEFAULT: fetch popupMessage and then decide.
        GetPopupMessageForAnchor(id, msg =>
        {
            if (!string.IsNullOrEmpty(msg))
            {
                // message clue: show message and mark solved
                LogPopupShowTiming("CluePopupUI", id);
                userPopupUI.Show(msg, title);
                MarkSolvedForCurrentUser(mapId, id);
                return;
            }

            // default clue: no message -> just mark solved
            MarkSolvedForCurrentUser(mapId, id);
        });
    }

    // ----- Admin Section -----

    // Called by AnchorHandle when in admin mode
    public void OnAnchorClickedAsAdmin(string id)
    {
        if (!adminMode)
            return;

        // Önce yüksek öncelikli admin modlarını işle (silme, link, edit vs.)
        if (_deleteMode)
        {
            Debug.Log("[Anchors][Admin] Deleting anchor in delete mode: " + id);
            DeleteAnchor(id);
            ExitDeleteMode(); // single-shot: after one delete, exit delete mode
            return;
        }

        if (_linkMode)
        {
            HandleLinkClick(id);
            return;
        }

        // Eğer PREFAB edit modundaysak, bu tıklamayı prefab seçimi için kullan
        if (_editPrefabMode)
        {
            _currentEditAnchorId = id;
            Debug.Log("[Anchors][Admin] Anchor selected for prefab edit: " + id);

            var selector = UnityEngine.Object.FindFirstObjectByType<PrefabSelectorUI>();
            if (selector != null)
            {
                selector.OnAnchorSelectedForPrefab(id);
            }
            return;
        }

        // Eğer isim edit modundaysak, bu tıklamayı isim düzenlemek için kullan
        if (_editClueNameMode)
        {
            if (!_anchors.ContainsKey(id))
            {
                Debug.LogWarning("[Anchors][Admin] Clicked anchor not in cache for name edit: " + id);
                return;
            }

            _currentNameAnchorId = id;
            Debug.Log("[Anchors][Admin] Anchor selected for clue name edit: " + id);

            var nameUi = UnityEngine.Object.FindFirstObjectByType<ClueNameEditorUI>();
            if (nameUi != null)
            {
                nameUi.RefreshSelectedAnchor();
            }
            return;
        }

        // Popup mesaj editörü açıkken ve özel olarak anchor seçme modundaysak
        var popupUi = UnityEngine.Object.FindFirstObjectByType<PopupMessageEditorUI>();
        if (popupUi != null && popupUi.gameObject.activeInHierarchy && _waitingForPopupAnchor)
        {
            // ✅ Puzzle clue'lar için popup message editini engelle
            if (_anchors.TryGetValue(id, out var pm) && pm != null &&
                string.Equals(pm.clueType, "puzzle", StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[Anchors][Admin] This clue is PUZZLE. Popup Message cannot be added/edited for puzzle clues.");
                // waiting modunda kal, admin başka clue'ya tıklasın
                _waitingForPopupAnchor = true;
                _currentPopupAnchorId = null;
                return;
            }

            _currentPopupAnchorId = id;
            _waitingForPopupAnchor = false;
            Debug.Log("[Anchors][Admin] Popup anchor selected: " + id);

            popupUi.RefreshSelectedAnchor();
            return;
        }

        if (_waitingForPuzzleAnchor)
        {
            _currentPuzzleAnchorId = id;
            _waitingForPuzzleAnchor = false;
            Debug.Log("[Anchors][Admin] Puzzle anchor selected: " + id);
            // Refresh PuzzleEditorUI if present and open
            var puzzleUi = UnityEngine.Object.FindFirstObjectByType<PuzzleEditorUI>();
            if (puzzleUi != null && puzzleUi.gameObject.activeInHierarchy)
            {
                puzzleUi.RefreshSelectedAnchor();
            }
            return;
        }

        // Hiçbir özel mod yoksa, sadece logla
        Debug.Log("[Anchors][Admin] Anchor clicked: " + id);
    }

    // Helpers to control admin modes from the admin UI:
    public void EnterLinkMode()
    {
        if (!adminMode) return;
        ExitEditModes();
        _linkMode = true;
        _deleteMode = false;
        _pendingLinkAnchorId = null;
        Debug.Log("[Anchors][Admin] Link mode ON");
    }

    public void EnterDeleteMode()
    {
        if (!adminMode) return;
        ExitEditModes();
        _deleteMode = true;
        _linkMode = false;
        _pendingLinkAnchorId = null;
        Debug.Log("[Anchors][Admin] Delete mode ON");
    }

    public void ExitDeleteMode()
    {
        _deleteMode = false;
        Debug.Log("[Anchors][Admin] Delete mode OFF");
    }

    public void ExitAdminModes()
    {
        _linkMode = false;
        _deleteMode = false;
        ExitEditModes();
        _pendingLinkAnchorId = null;
        Debug.Log("[Anchors][Admin] Admin modes OFF");
    }

    // Backward-compatible wrappers: the old "EditClues" mode is now the location edit mode
    public void EnterEditCluesMode()
    {
        EnterLocationEditMode();
    }

    public void ExitEditCluesMode()
    {
        ExitEditModes();
    }

    // Used by AnchorHandle to decide if dragging is allowed
    public bool IsInEditCluesMode()
    {
        return _editLocationMode;
    }

    // New explicit edit mode controls

    public void EnterLocationEditMode()
    {
        if (!adminMode) return;
        _deleteMode = false;
        _linkMode = false;
        _editLocationMode = true;
        _editPrefabMode = false;
        _editClueNameMode = false;
        Debug.Log("[Anchors][Admin] Edit LOCATION mode ON");
    }

    public void EnterPrefabEditMode()
    {
        if (!adminMode) return;
        _deleteMode = false;
        _linkMode = false;
        _editPrefabMode = true;
        _editLocationMode = false;
        _editClueNameMode = false;
        _currentEditAnchorId = null;   // ESKİ seçili anchor unutulsun
        Debug.Log("[Anchors][Admin] Edit PREFAB mode ON");
    }

    public void EnterClueNameEditMode()
    {
        if (!adminMode) return;
        _deleteMode = false;
        _linkMode = false;
        _editClueNameMode = true;
        _editLocationMode = false;
        _editPrefabMode = false;
        _currentNameAnchorId = null;
        Debug.Log("[Anchors][Admin] Edit CLUE NAME mode ON");
    }

    public void ExitEditModes()
    {
        _editLocationMode = false;
        _editPrefabMode = false;
        _editClueNameMode = false;
        _currentEditAnchorId = null;
        _currentNameAnchorId = null;
        Debug.Log("[Anchors][Admin] Edit modes OFF");
    }

    // Private helper to process link clicks using the existing LinkAnchors logic:
    private void HandleLinkClick(string id)
    {
        if (!_anchors.ContainsKey(id))
        {
            Debug.LogWarning("[Anchors][Admin] Clicked anchor not in cache: " + id);
            return;
        }

        if (string.IsNullOrEmpty(_pendingLinkAnchorId))
        {
            _pendingLinkAnchorId = id;
            Debug.Log("[Anchors][Admin] First anchor selected for link: " + id);
        }
        else
        {
            if (_pendingLinkAnchorId == id)
            {
                Debug.Log("[Anchors][Admin] Same anchor clicked twice, clearing selection.");
                _pendingLinkAnchorId = null;
                return;
            }

            Debug.Log($"[Anchors][Admin] Linking anchors: {_pendingLinkAnchorId} & {id}");
            LinkAnchors(_pendingLinkAnchorId, id);
            _pendingLinkAnchorId = null;
        }
    }

    // Public DeleteAnchor method that removes an anchor from Firebase and from the scene:
    public void DeleteAnchor(string id)
    {
        if (!_anchors.ContainsKey(id))
        {
            Debug.LogWarning("[Anchors][Admin] Cannot delete, anchor not found: " + id);
            return;
        }

        var meta = _anchors[id];
        int mapId = meta.mapId;

        // Remove from Firebase
        DB.Child("anchors").Child(mapId.ToString()).Child(id).RemoveValueAsync();

        // Remove spawned GameObject if any
        if (_spawned.TryGetValue(id, out var go) && go != null)
        {
            UnityEngine.Object.Destroy(go);
        }
        _spawned.Remove(id);
        _anchors.Remove(id);

        Debug.Log("[Anchors][Admin] Anchor deleted: " + id);
    }

    // Admin menü için basit bir anchor-link fonksiyonu
    public void LinkAnchors(string idA, string idB)
    {
        if (!_anchors.ContainsKey(idA) || !_anchors.ContainsKey(idB))
        {
            Debug.LogWarning($"[Anchors] One or both anchor ids not found: {idA}, {idB}");
            return;
        }

        var a = _anchors[idA];
        var b = _anchors[idB];

        int linkedIndex = Mathf.Min(a.clueIndex, b.clueIndex);
        a.clueIndex = linkedIndex;
        b.clueIndex = linkedIndex;

        _anchors[idA] = a;
        _anchors[idB] = b;

        int mapId = a.mapId;
        var updates = new Dictionary<string, object>
        {
            {$"{idA}/clueIndex", linkedIndex},
            {$"{idB}/clueIndex", linkedIndex}
        };

        DB.Child("anchors").Child(mapId.ToString()).UpdateChildrenAsync(updates);
    }

    // ---- Clue name helpers for editor UI ----

    public string GetCurrentClueNameAnchorId()
    {
        return _currentNameAnchorId;
    }

    public string GetClueName(string anchorId)
    {
        if (string.IsNullOrEmpty(anchorId)) return null;
        if (_anchors.TryGetValue(anchorId, out var meta) && meta != null)
        {
            return meta.clueName;
        }
        return null;
    }

    public void SetClueName(string anchorId, string newName)
    {
        if (string.IsNullOrEmpty(anchorId)) return;
        if (!_anchors.TryGetValue(anchorId, out var meta) || meta == null)
        {
            Debug.LogWarning("[Anchors][Admin] Cannot set clue name, anchor not found: " + anchorId);
            return;
        }

        meta.clueName = newName;
        _anchors[anchorId] = meta;

        int mapId = meta.mapId;
        DB.Child("anchors")
          .Child(mapId.ToString())
          .Child(anchorId)
          .Child("clueName")
          .SetValueAsync(newName);

        Debug.Log("[Anchors][Admin] Clue name updated for anchor " + anchorId + " -> " + newName);
    }
    // ----------- Puzzle popup helpers (reflection-based) -----------

    private void PuzzlePopup_Show(string hint, string title, Action<string> onSubmit)
    {
        if (puzzlePopupUI == null)
        {
            Debug.LogWarning("[Anchors][User] puzzlePopupUI is not assigned. Showing hint in CluePopupUI.");
            if (userPopupUI != null)
                userPopupUI.Show(hint, title);
            return;
        }

        var t = puzzlePopupUI.GetType();

        // Prefer: Show(string hint, string title, Action<string> onSubmit)
        var m = t.GetMethod("Show", new Type[] { typeof(string), typeof(string), typeof(Action<string>) });
        if (m != null)
        {
            m.Invoke(puzzlePopupUI, new object[] { hint, title, onSubmit });
            return;
        }

        // Fallback: Show(string hint, Action<string> onSubmit)
        var m2 = t.GetMethod("Show", new Type[] { typeof(string), typeof(Action<string>) });
        if (m2 != null)
        {
            m2.Invoke(puzzlePopupUI, new object[] { hint, onSubmit });
            return;
        }

        Debug.LogWarning("[Anchors][User] PuzzlePopUpUI has no compatible Show(...) method. Showing hint in CluePopupUI.");
        if (userPopupUI != null)
            userPopupUI.Show(hint, title);
    }

    private void PuzzlePopup_Hide()
    {
        if (puzzlePopupUI == null) return;
        var t = puzzlePopupUI.GetType();
        var m = t.GetMethod("Hide");
        if (m != null)
            m.Invoke(puzzlePopupUI, null);
        else
        {
            // fallback: disable gameobject
            puzzlePopupUI.gameObject.SetActive(false);
        }
    }
    private void PuzzlePopup_SetFeedback(string message)
    {
        if (puzzlePopupUI == null) return;
        var t = puzzlePopupUI.GetType();
        var m = t.GetMethod("SetFeedback", new Type[] { typeof(string) });
        if (m != null) m.Invoke(puzzlePopupUI, new object[] { message });
    }

    // ----------- USER PROGRESS (PER-USER) -----------

    private System.Collections.IEnumerator LoadUserProgressAndRefresh(int mapId)
    {
        _userSolvedIds.Clear();
        _unlockedClueIndex = 1;

        if (adminMode)
        {
            // Admin her şeyi görebilir
            RefreshAllVisibility();
            yield break;
        }

        string uid = CurrentUid;
        if (string.IsNullOrEmpty(uid))
        {
            Debug.LogWarning("[Anchors][User] No uid yet; progress not loaded. Showing only base visibility.");
            RefreshAllVisibility();
            yield break;
        }

        // users/{uid}/progress/{mapId}/solved
        bool done = false;
        DataSnapshot snap = null;
        DB.Child(usersRootKey)
          .Child(uid)
          .Child(progressKey)
          .Child(mapId.ToString())
          .Child("solved")
          .GetValueAsync()
          .ContinueWithOnMainThread(t =>
          {
              if (t.IsCompletedSuccessfully) snap = t.Result;
              done = true;
          });

        while (!done) yield return null;

        if (snap != null && snap.Exists)
        {
            foreach (var c in snap.Children)
            {
                // solved/{clueId}: true
                if (c != null && c.Value != null)
                {
                    bool val;
                    if (bool.TryParse(c.Value.ToString(), out val) && val)
                        _userSolvedIds.Add(c.Key);
                    else if (c.Value is bool b && b)
                        _userSolvedIds.Add(c.Key);
                    else
                    {
                        // If stored as 1/0 etc.
                        if (c.Value.ToString() == "1") _userSolvedIds.Add(c.Key);
                    }
                }
            }
        }

        _unlockedClueIndex = ComputeUnlockedIndex();
        RefreshAllVisibility();

        Debug.Log($"[Anchors][User] Progress loaded. solvedCount={_userSolvedIds.Count}, unlockedIndex={_unlockedClueIndex}");
    }

    private int ComputeUnlockedIndex()
    {
        // If no anchors, default 1
        if (_anchors.Count == 0) return 1;

        // Get distinct indices sorted
        var indices = new List<int>();
        foreach (var kv in _anchors)
        {
            var m = kv.Value;
            if (m == null) continue;
            if (!indices.Contains(m.clueIndex)) indices.Add(m.clueIndex);
        }
        indices.Sort();

        if (indices.Count == 0) return 1;

        // Walk from smallest index upwards
        foreach (var idx in indices)
        {
            bool allSolvedAtIdx = true;
            foreach (var kv in _anchors)
            {
                var m = kv.Value;
                if (m == null) continue;
                if (m.clueIndex != idx) continue;

                // Per-user solved check
                if (!_userSolvedIds.Contains(kv.Key))
                {
                    allSolvedAtIdx = false;
                    break;
                }
            }

            if (!allSolvedAtIdx)
                return idx; // this is the current unlocked stage
        }

        // Everything solved -> unlock max
        return indices[indices.Count - 1];
    }

    private bool ShouldBeVisibleForUser(string anchorId, AnchorMeta meta)
    {
        if (meta == null) return false;

        // Admin: use DB visible flag only
        if (adminMode) return meta.visible;

        // Base visibility flag from DB
        if (!meta.visible) return false;

        // Hierarchical gating
        return meta.clueIndex <= _unlockedClueIndex;
    }

    private void RefreshAllVisibility()
    {
        // Ensure unlockedIndex is up to date (anchors might have changed)
        if (!adminMode)
            _unlockedClueIndex = ComputeUnlockedIndex();

        foreach (var kv in _spawned)
        {
            var id = kv.Key;
            var go = kv.Value;
            if (go == null) continue;

            if (_anchors.TryGetValue(id, out var meta))
                go.SetActive(ShouldBeVisibleForUser(id, meta));
        }
    }

    private void MarkSolvedForCurrentUser(int mapId, string anchorId)
    {
        if (adminMode) return;

        string uid = CurrentUid;
        if (string.IsNullOrEmpty(uid))
        {
            Debug.LogWarning("[Anchors][User] Cannot mark solved: uid is null.");
            return;
        }

        // Local cache update first
        _userSolvedIds.Add(anchorId);

        // Persist: users/{uid}/progress/{mapId}/solved/{clueId} = true
        DB.Child(usersRootKey)
          .Child(uid)
          .Child(progressKey)
          .Child(mapId.ToString())
          .Child("solved")
          .Child(anchorId)
          .SetValueAsync(true);

        // Recompute unlocked index and refresh visibility
        _unlockedClueIndex = ComputeUnlockedIndex();
        RefreshAllVisibility();

        Debug.Log($"[Anchors][User] Marked solved. anchorId={anchorId}, unlockedIndex={_unlockedClueIndex}");
    }
    private void OnDestroy()
    {
        if (logoButton != null)
            logoButton.onClick.RemoveListener(ToggleLogoutMenu);

        if (logoutOverlayButton != null)
            logoutOverlayButton.onClick.RemoveListener(CloseLogoutMenu);

        if (mapRootProvider != null)
            mapRootProvider.OnMapSelected -= HandleMapSelected;

        UnsubscribeCurrentMap();
    }
}