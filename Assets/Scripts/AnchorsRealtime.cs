using System;
using System.Collections.Generic;
using System.Globalization;
using Firebase.Database;
using Firebase.Extensions;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem.EnhancedTouch;
using TMPro;
using ETouch = UnityEngine.InputSystem.EnhancedTouch.Touch;

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
    public AppUIManager appUIManager;

    [Header("Progress UI")]
    [Tooltip("TMP text that shows current map progress, e.g. 3 / 10")]
    public TMP_Text progressText;

    [Tooltip("Optional root object for the progress UI. This will be hidden in admin mode.")]
    public GameObject progressRoot;


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

    // The mapDbKey for which anchors are currently subscribed (unique per map, not per room)
    private string _activeAnchorsMapKey = null;

    // Subscribe flag
    private bool _subscribed = false;

    // -------- User progress (per-user) --------
    [Header("User Progress (Per-User)")]
    [Tooltip("DB root for users. Progress stored under users/{uid}/progress/{mapDbKey}/solved/{clueId}=true (mapDbKey is unique per map, not per room)")]
    public string usersRootKey = "users";

    [Tooltip("Child key under users/{uid} for progress data")]
    public string progressKey = "progress";

    [Header("XP & Level")]
    public int xpPerClue = 10;
    public int xpPerMapComplete = 50;

    // uid -> solved clueIds for current map (we only cache current user's current map here)
    private readonly HashSet<string> _userSolvedIds = new();

    // True once Firebase confirms this user already completed this map.
    // While true: new anchors are silently added to _userSolvedIds so progress
    // stays at 100 %, and MarkSolvedForCurrentUser is a no-op.
    private bool _mapAlreadyCompleted = false;

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

    private string CurrentMapDbKey
    {
        get
        {
            if (mapRootProvider != null && !string.IsNullOrEmpty(mapRootProvider.SelectedMapDbKey))
                return mapRootProvider.SelectedMapDbKey;

            // Legacy fallback: only use immersalMapId when it has been set (> 0).
            // When immersalMapId == 0, the map has not been identified yet — returning "0"
            // would cause TrySubscribeOnce to subscribe to anchors/0/ (which doesn't exist)
            // and lock _subscribed=true, preventing the real map from ever loading.
            if (mapRootProvider != null && mapRootProvider.immersalMapId > 0)
                return mapRootProvider.immersalMapId.ToString(); // legacy fallback only

            return null;
        }
    }

    #region Admin
    [Header("EditClueLocAR Buttons")]
    [Tooltip("Btn_DeleteClue — tıklanınca delete modunu toggle eder; mod aktifken koyu renk gösterir.")]
    public UnityEngine.UI.Button btnDeleteClue;

    private Color _deleteBtnNormalColor = Color.white;
    // Colour shown while delete mode is active (darker tint).
    private static readonly Color DeleteActiveTint = new Color(0.55f, 0.15f, 0.15f, 1f);

    [Header("Admin")]
    public bool adminMode = false;

    /// <summary>True while Screen_EditClueLocAR is open. Shows all anchors regardless of progression.</summary>
    [HideInInspector] public bool creatorEditSession = false;

    private bool _linkMode = false;
    private bool _deleteMode = false;
    private string _pendingLinkAnchorId = null;
    private bool _waitingForPuzzleAnchor = false;
    private string _currentPuzzleAnchorId = null;


    // Edit modes
    private bool _editLocationMode = false;   // drag/move clues
    private bool _editPrefabMode = false;     // change object type
    private bool _clueEditMode = false;       // tap clue to open Screen_EditClue

    // Two-finger rotation (twist gesture) — active in EditLocation mode
    private string _activeDragAnchorId = null;
    private bool   _isTwisting         = false;

    // Currently selected anchor for editing
    private string _currentEditAnchorId = null;   // for prefab edit
    private string _currentNameAnchorId = null;   // for clue name edit
    #endregion

    private class AnchorMeta
    {
        public int immersalMapId;
        public int clueIndex;
        public bool visible;
        public bool solved;
        public string prefabKey;
        public string clueName;

        public string clueType;
        public string popupMessage;

        // puzzle data (optional)
        public string puzzleHint;
        public string puzzlePassword;
        public string puzzleSolvedMessage;
    }


    [Serializable]
    public class ClueEditData
    {
        public string anchorId;
        public int immersalMapId;
        public string clueName;
        public int clueIndex;
        public string prefabKey;
        public string clueType;
        public string popupMessage;
        public string puzzleHint;
        public string puzzlePassword;
        public string puzzleSolvedMessage;
    }

    private string _selectedEditAnchorId;

    void OnEnable()  => EnhancedTouchSupport.Enable();
    void OnDisable() => EnhancedTouchSupport.Disable();

    void Update()
    {
        if (_editLocationMode)
        {
            var touches = ETouch.activeTouches;
            if (touches.Count >= 2)
                HandleTwoFingerRotation(touches[0], touches[1]);
            else
                _isTwisting = false;
        }
    }

    /// <summary>
    /// Called by AnchorHandle when a drag begins so we know which anchor to rotate.
    /// </summary>
    public void SetActiveDragAnchor(string id) => _activeDragAnchorId = id;

    private void HandleTwoFingerRotation(ETouch t0, ETouch t1)
    {
        Vector2 pos0  = t0.screenPosition;
        Vector2 pos1  = t1.screenPosition;

        if (!_isTwisting)
        {
            // First frame of gesture: select the nearest anchor and skip rotation this frame.
            _activeDragAnchorId = FindNearestSpawnedAnchorToScreen((pos0 + pos1) * 0.5f);
            _isTwisting = true;
            return;
        }

        // Reconstruct previous finger positions using per-frame delta.
        Vector2 prev0 = pos0 - (Vector2)t0.delta;
        Vector2 prev1 = pos1 - (Vector2)t1.delta;

        Vector2 prevDir = prev1 - prev0;
        Vector2 currDir = pos1  - pos0;

        // Need a minimum spread so tiny pinch noise doesn't cause huge angle errors.
        if (prevDir.magnitude < 20f || currDir.magnitude < 20f) return;

        // Signed angle: positive = CCW on screen.
        float delta = Vector2.SignedAngle(prevDir, currDir);

        // Clamp to ignore frame-spikes (finger lift/reposition).
        if (Mathf.Abs(delta) < 0.3f || Mathf.Abs(delta) > 25f) return;

        if (string.IsNullOrEmpty(_activeDragAnchorId)) return;
        if (!_spawned.TryGetValue(_activeDragAnchorId, out var go) || go == null) return;

        // Rotation axis = perpendicular to the finger line, projected into world space via camera axes.
        // Fingers vertical   → perp = screen-X → camera.right  → pitch (X-like)
        // Fingers horizontal → perp = screen-Y → camera.up     → yaw   (Y-like)
        // Fingers diagonal   → blend of both
        var cam = Camera.main;
        if (cam == null) return;

        Vector2 fingerDir2D = (pos1 - pos0).normalized;
        // 90° CCW perpendicular in screen space
        Vector2 perpScreen = new Vector2(-fingerDir2D.y, fingerDir2D.x);
        Vector3 rotAxis = (perpScreen.x * cam.transform.right
                         + perpScreen.y * cam.transform.up).normalized;

        go.transform.Rotate(rotAxis, -delta, Space.World);
    }

    /// <summary>Returns the anchorId whose world position is closest to the given screen point.</summary>
    private string FindNearestSpawnedAnchorToScreen(Vector2 screenPos)
    {
        var cam = Camera.main;
        if (cam == null || _spawned.Count == 0) return null;

        string best = null;
        float  bestDist = float.MaxValue;

        foreach (var kvp in _spawned)
        {
            if (kvp.Value == null) continue;
            Vector3 sp = cam.WorldToScreenPoint(kvp.Value.transform.position);
            if (sp.z <= 0) continue; // behind camera
            float d = Vector2.Distance(screenPos, new Vector2(sp.x, sp.y));
            if (d < bestDist) { bestDist = d; best = kvp.Key; }
        }

        return best;
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

        // Wire Btn_DeleteClue and cache its normal colour.
        if (btnDeleteClue != null)
        {
            var img = btnDeleteClue.GetComponent<UnityEngine.UI.Image>();
            if (img != null) _deleteBtnNormalColor = img.color;
            btnDeleteClue.onClick.RemoveListener(OnClickDeleteClue);
            btnDeleteClue.onClick.AddListener(OnClickDeleteClue);
        }

        UpdateProgressUI();
    }
    // ----------- Map Switching & Anchor Clearing -----------

    private void HandleMapSelected(int newImmersalMapId, string newMapName)
    {
        // Compare by DB key — two maps in the same room share an immersalMapId but have different dbKeys.
        string newMapKey = mapRootProvider != null ? mapRootProvider.SelectedMapDbKey : null;

        // If we never subscribed to anchors yet, nothing to tear down. The user is just
        // preparing to enter AR (e.g. via the Create flow). TrySubscribeOnce will pick up
        // the new map when the AR runtime starts and IsLocalized becomes true.
        if (string.IsNullOrEmpty(_activeAnchorsMapKey))
        {
            Debug.Log($"[Anchors] Map selected before any active subscription -> immersalId={newImmersalMapId}, dbKey={newMapKey}.");
            return;
        }

        if (!string.IsNullOrEmpty(newMapKey) && newMapKey == _activeAnchorsMapKey) return;

        Debug.Log($"[Anchors] Map changed -> immersalId={newImmersalMapId}, dbKey={newMapKey}. Restarting AR runtime so Immersal can re-localize.");
        _activeAnchorsMapKey = newMapKey;

        // Tear down state tied to the previous map.
        UnsubscribeCurrentMap();
        ClearSpawnedAnchors();
        _userSolvedIds.Clear();
        _unlockedClueIndex = 1;

        // Allow TrySubscribeOnce to run again once Immersal re-localizes.
        _subscribed = false;
        InvokeRepeating(nameof(TrySubscribeOnce), 1f, 1f);

        UpdateProgressUI();

        // No arRuntimeRoot toggle needed — Immersal keeps running and re-localizes
        // to the new map automatically. MapRootProvider's OnSuccessfulLocalizations
        // subscription will set localized=true once the new map is detected.
        // (Toggling arRuntimeRoot mid-session causes Immersal to abort permanently.)
    }

    private void ClearSpawnedAnchors()
    {
        foreach (var kv in _spawned)
        {
            if (kv.Value != null)
                UnityEngine.Object.Destroy(kv.Value);
        }
        _spawned.Clear();
        _anchors.Clear();
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


    void TrySubscribeOnce()
    {
        if (_subscribed) { CancelInvoke(nameof(TrySubscribeOnce)); return; }
        if (!FirebaseReady || mapRootProvider == null || !mapRootProvider.IsLocalized) return;

        // Resolve the map key BEFORE locking _subscribed. If the key isn't ready yet
        // (e.g. MarkLocalized fired before auto-select completed, so immersalMapId is still 0
        // and SelectedMapDbKey is still empty), we return WITHOUT setting _subscribed=true so
        // TrySubscribeOnce keeps retrying every second until a real key is available.
        int immersalMapId = mapRootProvider.immersalMapId;
        string mapKey = CurrentMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            // immersalMapId == 0 means no map is selected yet — silent wait, not an error.
            if (immersalMapId != 0)
                Debug.LogWarning("[Anchors] Cannot subscribe yet: map db key not ready (immersalMapId=" + immersalMapId + "). Will retry.");
            return;
        }

        _subscribed = true;
        CancelInvoke(nameof(TrySubscribeOnce));

        // 1) Mevcutları bir kerelik yükle
        DB.Child("anchors").Child(mapKey).GetValueAsync().ContinueWithOnMainThread(t =>
        {
            if (t.IsCompletedSuccessfully && t.Result != null && t.Result.Exists)
            {
                foreach (var s in t.Result.Children)
                {
                    var (id, localPos, localRot) = LocalPoseFromSnapshot(s);
                    var meta = MetaFromSnapshot(s, immersalMapId);
                    _anchors[id] = meta;
                    SpawnOrMoveLocal(id, localPos, localRot, meta.visible);
                }
                Debug.Log($"[Anchors] Loaded existing for immersalMapId={immersalMapId}, dbKey={mapKey}");

                UpdateProgressUI();
            }

            // 3) User progress yükle ve hiyerarşik görünürlüğü uygula.
            // IMPORTANT: must start AFTER anchors are in _anchors so that
            // ComputeUnlockedIndex() sees the real clueIndex values and
            // RefreshAllVisibility() can update already-spawned objects.
            StartCoroutine(LoadUserProgressAndRefresh(mapKey));
        });

        // 2) Canlı dinle (başkası koyarsa anında gör)
        UnsubscribeCurrentMap();

        _currentMapRef = DB.Child("anchors").Child(mapKey);

        _onChildAdded = (sender, args) =>
        {
            var (id, localPos, localRot) = LocalPoseFromSnapshot(args.Snapshot);
            var meta = MetaFromSnapshot(args.Snapshot, immersalMapId);
            _anchors[id] = meta;
            SpawnOrMoveLocal(id, localPos, localRot, meta.visible);

            // If this user already completed the map, treat every newly-added
            // anchor as instantly solved so the progress counter stays at 100 %.
            if (_mapAlreadyCompleted)
                _userSolvedIds.Add(id);
        };

        _onChildChanged = (sender, args) =>
        {
            var (id, localPos, localRot) = LocalPoseFromSnapshot(args.Snapshot);
            var meta = MetaFromSnapshot(args.Snapshot, immersalMapId);
            _anchors[id] = meta;
            SpawnOrMoveLocal(id, localPos, localRot, meta.visible);
        };

        _currentMapRef.ChildAdded += _onChildAdded;
        _currentMapRef.ChildChanged += _onChildChanged;

        // Track which mapDbKey we're currently subscribed to (not immersalMapId — maps in the same room share it).
        _activeAnchorsMapKey = mapKey;
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
        int immersalMapId = mapRootProvider.immersalMapId;
        string mapKey = CurrentMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            Debug.LogWarning("[Anchors] Cannot place clue: selected map db key is empty.");
            return;
        }

        var meta = new AnchorMeta
        {
            immersalMapId = immersalMapId,
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
            {"immersalMapId", immersalMapId}, // Immersal scan ID stored separately from the unique mapDbKey
            {"mapDbKey", mapKey},
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
        DB.Child("anchors").Child(mapKey).Child(id).SetValueAsync(data);

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
    private AnchorMeta MetaFromSnapshot(DataSnapshot s, int fallbackImmersalMapId)
    {
        // "mapId" is the legacy Firebase field name for the Immersal scan ID.
        int immersalMapId = fallbackImmersalMapId;
        if (s.Child("mapId").Exists)
            immersalMapId = Convert.ToInt32(s.Child("mapId").Value);
        else if (s.Child("immersalMapId").Exists)
            immersalMapId = Convert.ToInt32(s.Child("immersalMapId").Value);

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

        // ---- clueType / popup ----
        string popupMessage = null;
        if (s.Child("popupMessage").Exists && s.Child("popupMessage").Value != null)
            popupMessage = s.Child("popupMessage").Value.ToString();

        string clueType = "default";
        if (s.Child("clueType").Exists && s.Child("clueType").Value != null)
        {
            clueType = s.Child("clueType").Value.ToString();
        }
        else
        {
            // clueType yoksa popupMessage varsa message say
            if (!string.IsNullOrEmpty(popupMessage))
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
            immersalMapId = immersalMapId,
            clueIndex = clueIndex,
            visible = visible,
            solved = solved,
            prefabKey = prefabKey,
            clueName = clueName,

            clueType = clueType,
            popupMessage = popupMessage,
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

        string mapKey = CurrentMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            Debug.LogWarning("[Anchors][Admin] Cannot save transforms: selected map db key is empty.");
            return;
        }
        var map = mapRootProvider.mapRoot;
        if (map == null)
        {
            Debug.LogWarning("[Anchors][Admin] Map root is null, cannot save transforms.");
            return;
        }

        var refMap = DB.Child("anchors").Child(mapKey);

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
        string mapKey = CurrentMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            Debug.LogWarning("[Anchors][Admin] Cannot change prefab: selected map db key is empty.");
            return;
        }

        DB.Child("anchors")
          .Child(mapKey)
          .Child(anchorId)
          .Child("prefabKey")
          .SetValueAsync(prefabKey);

        Debug.Log("[Anchors][Admin] Prefab changed for anchor " + anchorId + " -> " + prefabKey);
    }


    // ----- New Create/Edit clue API -----

    public List<string> GetCurrentAnchorIdsSorted()
    {
        var ids = new List<string>(_anchors.Keys);
        ids.Sort((a, b) =>
        {
            int ai = _anchors.TryGetValue(a, out var am) && am != null ? am.clueIndex : 0;
            int bi = _anchors.TryGetValue(b, out var bm) && bm != null ? bm.clueIndex : 0;
            int cmp = ai.CompareTo(bi);
            return cmp != 0 ? cmp : string.CompareOrdinal(a, b);
        });
        return ids;
    }

    public void SetSelectedAnchorForEdit(string anchorId)
    {
        if (string.IsNullOrEmpty(anchorId) || !_anchors.ContainsKey(anchorId))
        {
            Debug.LogWarning("[Anchors][Edit] Cannot select anchor for edit: " + anchorId);
            _selectedEditAnchorId = null;
            return;
        }

        _selectedEditAnchorId = anchorId;
        Debug.Log("[Anchors][Edit] Selected anchor: " + anchorId);
    }

    public string GetSelectedAnchorForEdit()
    {
        return _selectedEditAnchorId;
    }

    public bool TryGetSelectedClueEditData(out ClueEditData data)
    {
        data = null;
        if (string.IsNullOrEmpty(_selectedEditAnchorId))
            return false;

        return TryGetClueEditData(_selectedEditAnchorId, out data);
    }

    public bool TryGetClueEditData(string anchorId, out ClueEditData data)
    {
        data = null;

        if (string.IsNullOrEmpty(anchorId) || !_anchors.TryGetValue(anchorId, out var meta) || meta == null)
            return false;

        data = new ClueEditData
        {
            anchorId = anchorId,
            immersalMapId = meta.immersalMapId,
            clueName = meta.clueName ?? "",
            clueIndex = meta.clueIndex,
            prefabKey = meta.prefabKey ?? defaultPrefabKey,
            clueType = NormalizeClueTypeForUI(meta.clueType),
            popupMessage = meta.popupMessage ?? "",
            puzzleHint = meta.puzzleHint ?? "",
            puzzlePassword = meta.puzzlePassword ?? "",
            puzzleSolvedMessage = meta.puzzleSolvedMessage ?? ""
        };

        return true;
    }

    public void SaveClueEditData(ClueEditData data)
    {
        if (data == null || string.IsNullOrEmpty(data.anchorId))
        {
            Debug.LogWarning("[Anchors][Edit] Save failed: data or anchorId is empty.");
            return;
        }

        if (!_anchors.TryGetValue(data.anchorId, out var meta) || meta == null)
        {
            Debug.LogWarning("[Anchors][Edit] Save failed: anchor not found: " + data.anchorId);
            return;
        }

        string normalizedType = NormalizeClueTypeForDb(data.clueType);
        string prefabKey = string.IsNullOrWhiteSpace(data.prefabKey) ? defaultPrefabKey : data.prefabKey.Trim();
        string clueName = data.clueName ?? "";
        int clueIndex = Mathf.Max(1, data.clueIndex);

        meta.clueName = clueName;
        meta.clueIndex = clueIndex;
        meta.prefabKey = prefabKey;
        meta.clueType = normalizedType;
        meta.popupMessage = normalizedType == "message" ? (data.popupMessage ?? "") : null;
        meta.puzzleHint = normalizedType == "puzzle" ? (data.puzzleHint ?? "") : null;
        meta.puzzlePassword = normalizedType == "puzzle" ? (data.puzzlePassword ?? "") : null;
        meta.puzzleSolvedMessage = normalizedType == "puzzle" ? (data.puzzleSolvedMessage ?? "") : null;
        _anchors[data.anchorId] = meta;

        string mapKey = CurrentMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            Debug.LogWarning("[Anchors][Edit] Save failed: selected map db key is empty.");
            return;
        }

        var anchorRef = DB.Child("anchors").Child(mapKey).Child(data.anchorId);

        var updates = new Dictionary<string, object>
        {
            {"clueName", clueName},
            {"clueIndex", clueIndex},
            {"prefabKey", prefabKey},
            {"clueType", normalizedType}
        };

        anchorRef.UpdateChildrenAsync(updates).ContinueWithOnMainThread(t =>
        {
            if (!t.IsCompletedSuccessfully)
            {
                Debug.LogWarning("[Anchors][Edit] Metadata save failed: " + t.Exception);
                return;
            }

            Debug.Log("[Anchors][Edit] Metadata saved for anchor " + data.anchorId);
        });

        if (normalizedType == "message")
        {
            anchorRef.Child("popupMessage").SetValueAsync(data.popupMessage ?? "");
            anchorRef.Child("puzzle").RemoveValueAsync();
        }
        else if (normalizedType == "puzzle")
        {
            anchorRef.Child("popupMessage").RemoveValueAsync();
            var puzzleUpdates = new Dictionary<string, object>
            {
                {"hint", data.puzzleHint ?? ""},
                {"password", data.puzzlePassword ?? ""},
                {"solvedMessage", data.puzzleSolvedMessage ?? ""}
            };
            anchorRef.Child("puzzle").UpdateChildrenAsync(puzzleUpdates);
        }
        else
        {
            anchorRef.Child("popupMessage").RemoveValueAsync();
            anchorRef.Child("puzzle").RemoveValueAsync();
        }

        ChangePrefabForAnchor(data.anchorId, prefabKey);
        RefreshAllVisibility();
    }

    public void DeleteSelectedAnchorForEdit()
    {
        if (string.IsNullOrEmpty(_selectedEditAnchorId))
        {
            Debug.LogWarning("[Anchors][Edit] Delete failed: no selected anchor.");
            return;
        }

        string id = _selectedEditAnchorId;
        _selectedEditAnchorId = null;
        DeleteAnchor(id);
    }

    private static string NormalizeClueTypeForUI(string clueType)
    {
        if (string.IsNullOrWhiteSpace(clueType)) return "clue";

        string t = clueType.Trim().ToLowerInvariant();
        if (t == "default") return "clue";
        if (t == "message") return "popup";
        if (t == "popup") return "popup";
        if (t == "puzzle") return "puzzle";
        if (t == "clue") return "clue";
        return "clue";
    }

    private static string NormalizeClueTypeForDb(string clueType)
    {
        if (string.IsNullOrWhiteSpace(clueType)) return "default";

        string t = clueType.Trim().ToLowerInvariant();
        if (t == "popup") return "message";
        if (t == "message") return "message";
        if (t == "puzzle") return "puzzle";
        return "default";
    }

    // ----- Popup message admin flow -----

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


    // Save a popup message string for the currently selected anchor
    public void SetPopupMessage(string message)
    {
        Debug.LogWarning("[Anchors][Admin] Popup editing via direct anchor selection is deprecated. Use EditClueUI instead.");
        return;
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

        string mapKey = CurrentMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            Debug.LogWarning("[Anchors][Admin] Cannot save puzzle: selected map db key is empty.");
            return;
        }

        string anchorId = _currentPuzzleAnchorId;

        var updates = new Dictionary<string, object>
        {
            {"clueType", "puzzle"},
            {"puzzle/hint", hint ?? ""},
            {"puzzle/password", password ?? ""},
            {"puzzle/solvedMessage", solvedMessage ?? ""}
        };

        DB.Child("anchors").Child(mapKey).Child(anchorId).UpdateChildrenAsync(updates);

        // local cache
        if (_anchors.TryGetValue(anchorId, out var m) && m != null)
        {
            m.clueType = "puzzle";
            m.popupMessage = null;
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

        string mapKey = CurrentMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            Debug.LogWarning("[Anchors][Admin] Cannot load popup message: selected map db key is empty.");
            onResult?.Invoke(null);
            return;
        }

        DB.Child("anchors")
          .Child(mapKey)
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

        if (appUIManager == null)
            appUIManager = FindFirstObjectByType<AppUIManager>();

        if (appUIManager == null)
        {
            Debug.LogWarning("[Anchors][User] AppUIManager is not assigned.");
            return;
        }

        if (!_anchors.TryGetValue(id, out var meta) || meta == null)
        {
            Debug.LogWarning("[Anchors][User] No meta found for anchor id: " + id);
            return;
        }

        // Use the unique DB key for progress — immersalMapId is not unique across maps in the same room.
        string mapDbKey = CurrentMapDbKey;

        string title = string.IsNullOrEmpty(meta.clueName)
            ? $"Clue {meta.clueIndex}"
            : meta.clueName;

        // 1) PUZZLE clue: open Screen_Puzzle through AppUIManager.
        if (string.Equals(meta.clueType, "puzzle", StringComparison.OrdinalIgnoreCase))
        {
            string hint = !string.IsNullOrEmpty(meta.puzzleHint)
                ? meta.puzzleHint
                : "(Puzzle)";

            // If already solved, show solved message as normal popup.
            if (_userSolvedIds.Contains(id))
            {
                string solvedMsg = !string.IsNullOrEmpty(meta.puzzleSolvedMessage)
                    ? meta.puzzleSolvedMessage
                    : "Doğru!";

                LogPopupShowTiming("Screen_Popup", id);
                appUIManager.ShowCluePopup(title, solvedMsg);
                return;
            }

            if (string.IsNullOrEmpty(meta.puzzlePassword))
            {
                Debug.LogWarning("[Anchors][User] Puzzle clue has no password configured. Showing hint only.");
                LogPopupShowTiming("Screen_Popup", id);
                appUIManager.ShowCluePopup(title, hint);
                return;
            }

            LogPopupShowTiming("Screen_Puzzle", id);
            appUIManager.ShowCluePuzzle(id, title, hint);
            return;
        }

        // 2) MESSAGE clue: open Screen_Popup through AppUIManager, then mark solved.
        // Check either explicit "message" clueType OR a non-empty popupMessage so that
        // legacy anchors without a clueType field but with a popupMessage still work.
        bool isMessageType = string.Equals(meta.clueType, "message", StringComparison.OrdinalIgnoreCase);
        if (isMessageType || !string.IsNullOrEmpty(meta.popupMessage))
        {
            string popupBody = !string.IsNullOrEmpty(meta.popupMessage)
                ? meta.popupMessage
                : title; // fallback: show at least the clue name
            LogPopupShowTiming("Screen_Popup", id);
            appUIManager.ShowCluePopup(title, popupBody);
            MarkSolvedForCurrentUser(mapDbKey, id);
            return;
        }

        // 3) DEFAULT clue: no screen needed, just mark solved.
        MarkSolvedForCurrentUser(mapDbKey, id);
    }

    public void SubmitPuzzleAnswer(string anchorId, string enteredAnswer)
    {
        if (string.IsNullOrEmpty(anchorId))
        {
            Debug.LogWarning("[Anchors][User] Puzzle submit failed: anchorId is empty.");
            return;
        }

        if (!_anchors.TryGetValue(anchorId, out var meta) || meta == null)
        {
            Debug.LogWarning("[Anchors][User] Puzzle submit failed. No meta for anchor id: " + anchorId);
            return;
        }

        if (appUIManager == null)
            appUIManager = FindFirstObjectByType<AppUIManager>();

        string input = (enteredAnswer ?? string.Empty).Trim();
        string answer = (meta.puzzlePassword ?? string.Empty).Trim();

        if (string.Equals(input, answer, StringComparison.OrdinalIgnoreCase))
        {
            string title = string.IsNullOrEmpty(meta.clueName)
                ? $"Clue {meta.clueIndex}"
                : meta.clueName;

            string solvedMsg = !string.IsNullOrEmpty(meta.puzzleSolvedMessage)
                ? meta.puzzleSolvedMessage
                : "Doğru!";

            MarkSolvedForCurrentUser(CurrentMapDbKey, anchorId);

            if (appUIManager != null)
                appUIManager.ShowCluePopup(title, solvedMsg);
        }
        else
        {
            Debug.Log("[Anchors][User] Wrong puzzle password for anchor " + anchorId);

            if (appUIManager != null)
                appUIManager.SetPuzzleFeedback("Wrong answer, try again.");
        }
    }

    // ----- Admin Section -----

    // Called by AnchorHandle when in admin mode or any creator edit mode
    public void OnAnchorClickedAsAdmin(string id)
    {

        if (_clueEditMode)
        {
            SetSelectedAnchorForEdit(id);

            if (appUIManager == null)
                appUIManager = FindFirstObjectByType<AppUIManager>();

            if (appUIManager != null)
                appUIManager.ShowEditClue();
            else
                Debug.LogWarning("[Anchors] AppUIManager not found");

            return;
        }

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
            return;
        }

        // Popup mesaj editörü açıkken ve özel olarak anchor seçme modundaysak

        if (_waitingForPuzzleAnchor)
        {
            _currentPuzzleAnchorId = id;
            _waitingForPuzzleAnchor = false;
            Debug.Log("[Anchors][Admin] Puzzle anchor selected: " + id);
            return;
        }

        // In location-edit mode (Btn_EditLocation) anchors are draggable — a tap/click
        // should not open any screen. The drag itself is handled by AnchorHandle.OnBeginDrag.
        if (_editLocationMode)
        {
            Debug.Log("[Anchors][Admin] Anchor clicked in location-edit mode — ignoring (drag only).");
            return;
        }

        // No special mode active — show a read-only preview of the clue popup/puzzle
        // so admins can verify what users will see.
        Debug.Log("[Anchors][Admin] Anchor clicked (preview mode): " + id);

        if (!_anchors.TryGetValue(id, out var previewMeta) || previewMeta == null)
            return;

        if (appUIManager == null)
            appUIManager = FindFirstObjectByType<AppUIManager>();

        if (appUIManager == null)
            return;

        string previewTitle = !string.IsNullOrEmpty(previewMeta.clueName)
            ? previewMeta.clueName
            : $"Clue {previewMeta.clueIndex}";

        if (string.Equals(previewMeta.clueType, "puzzle", StringComparison.OrdinalIgnoreCase))
        {
            string hint = !string.IsNullOrEmpty(previewMeta.puzzleHint) ? previewMeta.puzzleHint : "(Puzzle)";
            appUIManager.ShowCluePuzzle(id, $"[Preview] {previewTitle}", hint);
        }
        else if (string.Equals(previewMeta.clueType, "message", StringComparison.OrdinalIgnoreCase)
                 || !string.IsNullOrEmpty(previewMeta.popupMessage))
        {
            string msg = !string.IsNullOrEmpty(previewMeta.popupMessage)
                ? previewMeta.popupMessage
                : previewTitle;
            appUIManager.ShowCluePopup($"[Preview] {previewTitle}", msg);
        }
        else
        {
            // Default type — nothing to preview, just log
            Debug.Log($"[Anchors][Admin] Anchor {id} is default type (no popup content to preview).");
        }
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

    /// <summary>
    /// Called by Btn_DeleteClue in Screen_EditClueLocAR.
    /// Toggles delete mode — no adminMode check so map creators can use it.
    /// </summary>
    public void OnClickDeleteClue()
    {
        if (_deleteMode)
        {
            _deleteMode = false;
            SetDeleteButtonTint(false);
            Debug.Log("[Anchors] Delete mode OFF (toggled off)");
        }
        else
        {
            ExitEditModes();
            _deleteMode = true;
            _linkMode   = false;
            SetDeleteButtonTint(true);
            Debug.Log("[Anchors] Delete mode ON");
        }
    }

    private void SetDeleteButtonTint(bool active)
    {
        if (btnDeleteClue == null) return;
        var img = btnDeleteClue.GetComponent<UnityEngine.UI.Image>();
        if (img != null) img.color = active ? DeleteActiveTint : _deleteBtnNormalColor;
    }

    public void EnterDeleteMode()
    {
        if (!adminMode) return;
        ExitEditModes();
        _deleteMode = true;
        _linkMode = false;
        _pendingLinkAnchorId = null;
        SetDeleteButtonTint(true);
        Debug.Log("[Anchors][Admin] Delete mode ON");
    }

    /// <summary>Called by EditClueLocARUI when delete mode is exited externally (e.g. after single-shot delete).</summary>
    public System.Action OnDeleteModeExited;

    public void ExitDeleteMode()
    {
        _deleteMode = false;
        SetDeleteButtonTint(false);
        OnDeleteModeExited?.Invoke();
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

    /// <summary>True when any creator/admin edit mode is active (location, clue, or delete).</summary>
    public bool IsInAnyEditMode()
    {
        return _editLocationMode || _clueEditMode || _deleteMode;
    }

    // New explicit edit mode controls

    public void EnterLocationEditMode()
    {
        _deleteMode = false;
        _linkMode = false;
        _editLocationMode = true;
        _editPrefabMode = false;
        _clueEditMode = false;
        RefreshAllVisibility(); // show all anchors while editing
        Debug.Log("[Anchors] Edit LOCATION mode ON");
    }

    public void EnterPrefabEditMode()
    {
        if (!adminMode) return;
        _deleteMode = false;
        _linkMode = false;
        _editPrefabMode = true;
        _editLocationMode = false;
        _clueEditMode = false;
        _currentEditAnchorId = null;   // ESKİ seçili anchor unutulsun
        Debug.Log("[Anchors][Admin] Edit PREFAB mode ON");
    }

    public void EnterClueEditMode()
    {
        _deleteMode = false;
        _linkMode = false;
        _editLocationMode = false;
        _editPrefabMode = false;
        _clueEditMode = true;
        _currentEditAnchorId = null;
        _currentNameAnchorId = null;
        Debug.Log("[Anchors] Clue EDIT mode ON");
    }

    public void EnterClueNameEditMode()
    {
        if (!adminMode) return;
        _deleteMode = false;
        _linkMode = false;
        _editLocationMode = false;
        _editPrefabMode = false;
        _clueEditMode = false;
        _currentNameAnchorId = null;
        Debug.Log("[Anchors][Admin] Edit CLUE NAME mode ON");
    }

    public void ExitEditModes()
    {
        _editLocationMode = false;
        _editPrefabMode = false;
        _clueEditMode = false;
        _currentEditAnchorId = null;
        _currentNameAnchorId = null;
        RefreshAllVisibility(); // restore normal unlock-gated visibility
        Debug.Log("[Anchors] Edit modes OFF");
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
        string mapKey = CurrentMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            Debug.LogWarning("[Anchors][Admin] Cannot delete: selected map db key is empty.");
            return;
        }

        // Remove from Firebase
        DB.Child("anchors").Child(mapKey).Child(id).RemoveValueAsync();

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

        string mapKey = CurrentMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            Debug.LogWarning("[Anchors][Admin] Cannot link anchors: selected map db key is empty.");
            return;
        }
        var updates = new Dictionary<string, object>
        {
            {$"{idA}/clueIndex", linkedIndex},
            {$"{idB}/clueIndex", linkedIndex}
        };

        DB.Child("anchors").Child(mapKey).UpdateChildrenAsync(updates);
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

        string mapKey = CurrentMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            Debug.LogWarning("[Anchors][Admin] Cannot set clue name: selected map db key is empty.");
            return;
        }

        DB.Child("anchors")
          .Child(mapKey)
          .Child(anchorId)
          .Child("clueName")
          .SetValueAsync(newName);

        Debug.Log("[Anchors][Admin] Clue name updated for anchor " + anchorId + " -> " + newName);
    }
    // ----------- Admin edit helpers -----------

    /// <summary>
    /// Ensures _anchors is populated for the current map WITHOUT requiring AR localization.
    /// Used by Screen_EditClues so the clue list shows correctly even before entering AR.
    ///
    /// - If _anchors is already loaded for the current map key, calls onLoaded immediately.
    /// - If a different map is selected (or _anchors is empty), fetches from Firebase once
    ///   and populates _anchors (metadata only — no GameObjects are spawned).
    /// - Safe to call while an AR subscription is active: won't clear live data.
    /// </summary>
    public void LoadAnchorsForEditing(System.Action onLoaded = null)
    {
        string mapKey = CurrentMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            Debug.LogWarning("[Anchors][Edit] LoadAnchorsForEditing: map db key empty.");
            onLoaded?.Invoke();
            return;
        }

        // Already loaded and correct map — nothing to do.
        if (_anchors.Count > 0 && string.Equals(_activeAnchorsMapKey, mapKey, StringComparison.Ordinal))
        {
            onLoaded?.Invoke();
            return;
        }

        int immersalId = mapRootProvider != null ? mapRootProvider.immersalMapId : 0;
        Debug.Log($"[Anchors][Edit] LoadAnchorsForEditing: fetching from Firebase. mapKey={mapKey}");

        DB.Child("anchors").Child(mapKey).GetValueAsync().ContinueWithOnMainThread(t =>
        {
            // Clear stale data from a different map only when no live AR subscription is running.
            if (!string.Equals(_activeAnchorsMapKey, mapKey, StringComparison.Ordinal) && !_subscribed)
                _anchors.Clear();

            _activeAnchorsMapKey = mapKey;

            if (t.IsCompletedSuccessfully && t.Result != null && t.Result.Exists)
            {
                foreach (var snap in t.Result.Children)
                {
                    // The Firebase child key equals the anchor id (set explicitly in PlaceHere).
                    string id = snap.Key;
                    if (!string.IsNullOrEmpty(id))
                        _anchors[id] = MetaFromSnapshot(snap, immersalId);
                }
                Debug.Log($"[Anchors][Edit] Loaded {_anchors.Count} anchors for editing. mapKey={mapKey}");
            }
            else
            {
                Debug.Log($"[Anchors][Edit] No anchors found for editing. mapKey={mapKey}");
            }

            onLoaded?.Invoke();
        });
    }

    // ----------- Approval status -----------

    /// <summary>
    /// Resets the current map's approvalStatus to "pending" in Firebase.
    /// Call this after any admin save or delete that modifies map content,
    /// so the map must be re-approved before it goes live again.
    /// </summary>
    public void ResetMapApprovalToPending()
    {
        string mapKey = CurrentMapDbKey;
        if (string.IsNullOrEmpty(mapKey))
        {
            Debug.LogWarning("[Anchors] Cannot reset approval: map db key is empty.");
            return;
        }

        var updates = new System.Collections.Generic.Dictionary<string, object>
        {
            { "approvalStatus", "pending" }
        };

        DB.Child("maps").Child(mapKey).UpdateChildrenAsync(updates)
            .ContinueWithOnMainThread(t =>
            {
                if (!t.IsCompletedSuccessfully)
                {
                    Debug.LogWarning("[Anchors] Failed to reset approval status: " + t.Exception);
                    return;
                }
                Debug.Log($"[Anchors] Map approval reset to pending. mapKey={mapKey}");
            });
    }

    // ----------- USER PROGRESS (PER-USER) -----------

    private System.Collections.IEnumerator LoadUserProgressAndRefresh(string mapDbKey)
    {
        _userSolvedIds.Clear();
        _unlockedClueIndex = 1;
        _mapAlreadyCompleted = false;

        if (adminMode)
        {
            RefreshAllVisibility();
            UpdateProgressUI();
            yield break;
        }

        string uid = CurrentUid;
        if (string.IsNullOrEmpty(uid))
        {
            Debug.LogWarning("[Anchors][User] No uid yet; progress not loaded. Showing only base visibility.");
            RefreshAllVisibility();
            UpdateProgressUI();
            yield break;
        }

        // Read the entire progress/{mapDbKey} node so we get both
        // the solved list AND the mapCompleted flag in one round-trip.
        bool done = false;
        DataSnapshot progressSnap = null;
        DB.Child(usersRootKey)
          .Child(uid)
          .Child(progressKey)
          .Child(mapDbKey)
          .GetValueAsync()
          .ContinueWithOnMainThread(t =>
          {
              if (t.IsCompletedSuccessfully) progressSnap = t.Result;
              done = true;
          });

        while (!done) yield return null;

        // ── Check mapCompleted first ────────────────────────────────────────
        if (progressSnap != null && progressSnap.Exists)
        {
            var completedChild = progressSnap.Child("mapCompleted");
            if (completedChild.Exists && completedChild.Value != null)
            {
                bool flagVal = false;
                bool.TryParse(completedChild.Value.ToString(), out flagVal);
                if (!flagVal && completedChild.Value.ToString() == "True") flagVal = true;
                _mapAlreadyCompleted = flagVal;
            }
        }

        if (_mapAlreadyCompleted)
        {
            // Map is already done for this user.
            // Mark every current anchor as solved so the progress counter
            // shows 100 % regardless of how many clues the owner added since.
            foreach (var id in _anchors.Keys)
                _userSolvedIds.Add(id);

            _unlockedClueIndex = ComputeUnlockedIndex();
            RefreshAllVisibility();
            UpdateProgressUI();
            Debug.Log($"[Anchors][User] Map already completed — frozen at 100 %. anchors={_anchors.Count}");
            yield break;
        }

        // ── Normal path: load solved clues ─────────────────────────────────
        var solvedSnap = progressSnap?.Child("solved");
        if (solvedSnap != null && solvedSnap.Exists)
        {
            foreach (var c in solvedSnap.Children)
            {
                if (c == null || c.Value == null) continue;

                bool val = false;
                if (bool.TryParse(c.Value.ToString(), out val) && val)
                    _userSolvedIds.Add(c.Key);
                else if (c.Value is bool b && b)
                    _userSolvedIds.Add(c.Key);
                else if (c.Value.ToString() == "1")
                    _userSolvedIds.Add(c.Key);
            }
        }

        _unlockedClueIndex = ComputeUnlockedIndex();
        RefreshAllVisibility();
        UpdateProgressUI();

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

        // Admin OR creator edit session: show all DB-visible anchors, no unlock gating
        if (adminMode || creatorEditSession || IsInAnyEditMode()) return meta.visible;

        // Base visibility flag from DB
        if (!meta.visible) return false;

        // Hierarchical gating
        return meta.clueIndex <= _unlockedClueIndex;
    }

    public void RefreshAllVisibility()
    {
        // Ensure unlockedIndex is up to date (anchors might have changed).
        // Skip in admin or creator edit session — all anchors are shown unconditionally.
        if (!adminMode && !creatorEditSession && !IsInAnyEditMode())
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
    private void UpdateProgressUI()
    {
        // Hide progress bar for admins and for creators in edit session
        bool show = !adminMode && !creatorEditSession && !IsInAnyEditMode();

        if (progressRoot != null)
            progressRoot.SetActive(show);

        if (!show)
            return;

        if (progressText != null)
            progressText.text = GetCurrentMapProgressText();
    }
    /// Returns solved and total clue counts for the currently loaded map.
    /// Note: `_anchors` only contains anchors of the active map after map switching.
    public (int solved, int total) GetCurrentMapProgress()
    {
        int total = 0;
        int solved = 0;

        foreach (var kv in _anchors)
        {
            total++;
            if (_userSolvedIds.Contains(kv.Key))
                solved++;
        }

        return (solved, total);
    }
    /// Convenience string for UI, e.g. "3 / 10".
    public string GetCurrentMapProgressText()
    {
        var progress = GetCurrentMapProgress();
        return $"{progress.solved} / {progress.total}";
    }

    // ----------- XP & Level helpers -----------

    private int CalculateLevelFromXp(int xp)
    {
        // Simple curve
        return Mathf.FloorToInt(Mathf.Sqrt(xp / 100f)) + 1;
    }

    private void AddXpForCurrentUser(int amount)
    {
        string uid = CurrentUid;
        if (string.IsNullOrEmpty(uid)) return;

        var userRef = DB.Child(usersRootKey).Child(uid);

        userRef.Child("xp").GetValueAsync().ContinueWithOnMainThread(t =>
        {
            int currentXp = 0;
            if (t.IsCompletedSuccessfully && t.Result != null && t.Result.Exists && t.Result.Value != null)
                int.TryParse(t.Result.Value.ToString(), out currentXp);

            int newXp = currentXp + amount;
            int newLevel = CalculateLevelFromXp(newXp);

            var updates = new Dictionary<string, object>
            {
                {"xp", newXp},
                {"level", newLevel}
            };

            userRef.UpdateChildrenAsync(updates);

            Debug.Log($"[XP] +{amount} XP → Total: {newXp}, Level: {newLevel}");
        });
    }

    private void MarkSolvedForCurrentUser(string mapDbKey, string anchorId)
    {
        if (adminMode) return;

        // Map already completed — don't give XP or re-trigger completion logic.
        if (_mapAlreadyCompleted) return;

        string uid = CurrentUid;
        if (string.IsNullOrEmpty(uid))
        {
            Debug.LogWarning("[Anchors][User] Cannot mark solved: uid is null.");
            return;
        }

        // Prevent duplicate solve handling and duplicate XP for the same clue.
        if (_userSolvedIds.Contains(anchorId))
        {
            Debug.Log($"[Anchors][User] Anchor already solved, skipping duplicate reward. anchorId={anchorId}");
            return;
        }

        // Local cache update first
        _userSolvedIds.Add(anchorId);
        // Give XP for solving a clue
        AddXpForCurrentUser(xpPerClue);

        // Persist: users/{uid}/progress/{mapDbKey}/solved/{clueId} = true  (mapDbKey is unique per map)
        DB.Child(usersRootKey)
          .Child(uid)
          .Child(progressKey)
          .Child(mapDbKey)
          .Child("solved")
          .Child(anchorId)
          .SetValueAsync(true);

        // Recompute unlocked index and refresh visibility
        _unlockedClueIndex = ComputeUnlockedIndex();
        RefreshAllVisibility();
        UpdateProgressUI();

        // Check if map is fully completed
        var progress = GetCurrentMapProgress();
        if (progress.total > 0 && progress.solved == progress.total)
        {
            OnMapFullyCompleted(uid, mapDbKey);
        }

        Debug.Log($"[Anchors][User] Marked solved. anchorId={anchorId}, unlockedIndex={_unlockedClueIndex}");
    }
    /// <summary>
    /// Called exactly once when the user solves the last clue in a map.
    /// - Marks the map as completed in Firebase (prevents double-counting on restart).
    /// - Increments users/{uid}/completedMaps with a transaction (race-condition-safe).
    /// - Awards map-completion XP.
    /// - Shows Screen_VoteMap so the user can rate the experience.
    /// </summary>
    private void OnMapFullyCompleted(string uid, string mapDbKey)
    {
        Debug.Log($"[Anchors][User] Map fully completed! uid={uid}, mapDbKey={mapDbKey}");

        // 1) Persist the completed flag so we don't re-trigger on the next app launch.
        //    If the flag already exists we bail out — this path should never be reached
        //    twice because MarkSolvedForCurrentUser deduplicates via _userSolvedIds,
        //    but the Firebase check is an extra safety net.
        var completedRef = DB.Child(usersRootKey).Child(uid)
                             .Child(progressKey).Child(mapDbKey)
                             .Child("mapCompleted");

        completedRef.GetValueAsync().ContinueWithOnMainThread(checkTask =>
        {
            bool alreadyCompleted = false;
            if (checkTask.IsCompletedSuccessfully
                && checkTask.Result != null
                && checkTask.Result.Exists
                && checkTask.Result.Value != null)
            {
                bool.TryParse(checkTask.Result.Value.ToString(), out alreadyCompleted);
            }

            if (alreadyCompleted)
            {
                Debug.Log("[Anchors][User] Map was already marked completed — skipping rewards.");
                return;
            }

            // Write completed flag + snapshot how many clues existed at completion time.
            // completedClueCount lets us audit history; mapCompleted is the authoritative flag.
            var completionData = new Dictionary<string, object>
            {
                { "mapCompleted",       true                         },
                { "completedClueCount", GetCurrentMapProgress().total },
                { "completedAt",        ServerValue.Timestamp        }
            };
            DB.Child(usersRootKey).Child(uid)
              .Child(progressKey).Child(mapDbKey)
              .UpdateChildrenAsync(completionData);

            // 2) Increment completedMaps counter (transaction prevents race conditions).
            DB.Child(usersRootKey).Child(uid).Child("completedMaps")
                .RunTransaction(mutableData =>
                {
                    int current = 0;
                    if (mutableData.Value != null)
                        int.TryParse(mutableData.Value.ToString(), out current);
                    mutableData.Value = current + 1;
                    return TransactionResult.Success(mutableData);
                });

            // 3) Award map-completion bonus XP.
            AddXpForCurrentUser(xpPerMapComplete);
            Debug.Log("[XP] Map completed bonus awarded");

            // 4) Show the vote screen so the user can rate the map.
            if (appUIManager == null)
                appUIManager = FindFirstObjectByType<AppUIManager>();

            if (appUIManager != null)
                appUIManager.ShowVoteMap();
            else
                Debug.LogWarning("[Anchors][User] AppUIManager not found — cannot show VoteMap screen.");
        });
    }

    private void OnDestroy()
    {
        if (mapRootProvider != null)
            mapRootProvider.OnMapSelected -= HandleMapSelected;

        UnsubscribeCurrentMap();
    }
}