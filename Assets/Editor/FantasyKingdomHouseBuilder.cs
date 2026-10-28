#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class FantasyKingdomHouseBuilder : EditorWindow
{
    sealed class PickerShortcutContext : IShortcutContext
    {
        internal readonly FantasyKingdomHouseBuilder owner;

        internal PickerShortcutContext(FantasyKingdomHouseBuilder owner) { this.owner = owner; }

        public bool active => owner && !EditorGUIUtility.editingTextField &&
            (focusedWindow is SceneView || focusedWindow == owner || mouseOverWindow is SceneView);
    }

    PickerShortcutContext pickerShortcutContext;
    SceneView pendingPickerSceneView;

    // A contextual shortcut takes precedence over Unity's global P / Enter Prefab Mode.
    [Shortcut("Fantasy Kingdom/House Builder/Sample Under Cursor", typeof(PickerShortcutContext), KeyCode.P)]
    static void SampleUnderCursorShortcut(ShortcutArguments args)
    {
        var context = args.context as PickerShortcutContext;
        if (context == null || !context.owner || context.owner.floorDragActive) return;
        SceneView view = mouseOverWindow as SceneView ?? focusedWindow as SceneView;
        if (!view) return;
        // Picking needs the Scene view's camera/GUI context, not the global shortcut callback.
        context.owner.pendingPickerSceneView = view;
        view.Repaint();
    }

    // Modular placement editor for POLYGON Fantasy Kingdom house and castle assets.
    const string HouseFolder = "Assets/PolygonFantasyKingdom/Prefabs/House";
    const string CastleFolder = "Assets/PolygonFantasyKingdom/Prefabs/Castle";
    const string PropsFolder = "Assets/PolygonFantasyKingdom/Prefabs/Props";
    const string ItemsFolder = "Assets/PolygonFantasyKingdom/Prefabs/Items";
    const string EnvironmentFolder = "Assets/PolygonFantasyKingdom/Prefabs/Environments";
    const string GenericFolder = "Assets/PolygonFantasyKingdom/Prefabs/Generic";
    const string PropPathsFolder = PropsFolder + "/Paths";
    static readonly string[] AssetFolders = { HouseFolder, CastleFolder, PropsFolder, ItemsFolder, EnvironmentFolder, GenericFolder };
    const string RootName = "HOUSE BUILD";
    const string PrefGrid = "FKHouseBuilder.Grid";
    const string PrefLevel = "FKHouseBuilder.Level";
    const string PrefLevelStep = "FKHouseBuilder.LevelStep";
    const string PrefFloorOffset = "FKHouseBuilder.FloorOffset";
    const string PrefBottomAnchor = "FKHouseBuilder.BottomAnchor";
    const string PrefShowHouse = "FKHouseBuilder.ShowHouse";
    const string PrefShowCastle = "FKHouseBuilder.ShowCastle";
    const string PrefCalibrated = "FKHouseBuilder.GridCalibratedV1";
    const float KitHalfModule = 1.25f;
    const float KitStoreyHeight = 2.5f;

    static readonly string[] Categories =
    {
        "All", "Base", "Floor", "Wall", "Door & Window", "Roof", "Tower",
        "Stairs", "Chimney", "Railing", "Beam & Support", "Battlements",
        "Pillar", "Gate & Bridge", "Destroyed", "Other"
    };

    readonly List<GameObject> allPrefabs = new List<GameObject>();
    static readonly string[] PropCategories = { "All", "Furniture", "Containers", "Lighting", "Tabletop", "Decorations", "Other" };
    static readonly string[] EnvironmentCategories = { "All", "Trees", "Plants", "Rocks", "Ground", "Paths & Structures", "Path Bricks", "Water", "Sky", "Other" };
    [SerializeField] bool propsTab;
    [SerializeField] bool roomTab;
    [SerializeField] bool environmentTab;
    [SerializeField] bool showEnvironmentAssets = true;
    [SerializeField] bool showGenericAssets = true;
    [SerializeField] bool environmentOnSurface = true;
    [SerializeField] bool environmentAlignToSurface;
    bool IsEnvironmentTab => environmentTab && !propsTab && !roomTab;
    bool UsesPropPlacement => propsTab || IsEnvironmentTab;
    bool WallPlacement => propsTab && propsOnWall;
    bool SurfacePlacement => propsTab ? propsOnSurface || propsOnWall : IsEnvironmentTab && environmentOnSurface;
    bool AlignPlacementToSurface => IsEnvironmentTab ? environmentOnSurface && environmentAlignToSurface : alignToSurface;
    string[] ActiveCategories => IsEnvironmentTab ? EnvironmentCategories : propsTab ? PropCategories : Categories;
    [SerializeField] GameObject roomFloor;
    [SerializeField] GameObject roomWall;
    [SerializeField] GameObject roomCornerPillar;
    [SerializeField] GameObject roomEdgePillar;
    [SerializeField] int roomSlot;
    [SerializeField] bool roomReplaceExisting;
    [SerializeField] bool ignoreRoomAssetTypes;
    [SerializeField] float roomJointSpacing = 2.5f;
    bool roomRectangleDrag;
    Bounds roomFloorBounds, roomWallBounds, roomCornerBounds, roomEdgeBounds;
    Vector2 roomTileSize;
    float roomWallSpan;
    struct RoomPiece
    {
        public GameObject prefab;
        public Bounds bounds;
        public Vector3 position;
        public Quaternion rotation;
        public HouseBuilderPieceKind kind;
    }
    struct RoomOccupant
    {
        public GameObject instance;
        public HouseBuilderPieceKind kind;
        public Bounds bounds;
        public Vector3 wallAxis;
    }
    readonly List<RoomOccupant> roomOccupants = new List<RoomOccupant>();
    int roomSkippedPieces;
    [SerializeField] bool propsOnSurface;
    [SerializeField] bool propsOnWall;
    [SerializeField] bool useActualMeshSurface;
    struct PlacementSurfaceHit
    {
        public Vector3 point, normal;
        public float distance;
        public GameObject gameObject;
    }
    sealed class SurfaceMeshData
    {
        public Vector3[] vertices;
        public int[] triangles;
    }
    readonly Dictionary<Mesh, SurfaceMeshData> surfaceMeshCache = new Dictionary<Mesh, SurfaceMeshData>();
    MeshFilter[] surfaceMeshTargets;
    bool surfaceMeshTargetsDirty = true;
    [SerializeField] int propWallFacing;
    [SerializeField] bool propSnapping;
    [SerializeField] float propGrid = .1f;
    [SerializeField] float propSurfaceOffset;
    [SerializeField] float propRotationStep = 15f;
    HouseBuilderFloorMarker surfaceFloor;
    bool cursorValid;
    readonly List<GameObject> shownPrefabs = new List<GameObject>();
    Vector2 scroll;
    Vector2 controlsScroll;
    float controlsContentWidth;
    readonly Rect[] roomSlotClearRects = new Rect[4];
    const float ControlsColumnWidth = 350f;
    const float AssetTileWidth = 96f;
    const float PalettePadding = 28f;
    const float ColumnSpacing = 12f;
    const float WideLayoutThreshold = ControlsColumnWidth + ColumnSpacing + PalettePadding + AssetTileWidth * 2f;
    string search = "";
    int categoryIndex;
    int selectedIndex = -1;
    float gridSize = KitHalfModule;
    float level;
    float levelStep = KitStoreyHeight;
    float floorOffset;
    float yaw;
    bool buildMode = true;
    [SerializeField] bool emptyCursorMode;
    Tool unityToolBeforeBuildMode = Tool.None;
    bool builderOwnsUnityTool;
    bool eraseMode;
    bool pickerMode;
    bool alignToSurface;
    bool bottomAnchoredPlacement = true;
    bool showNames = true;
    bool showFloorGuide = true;
    bool showHouseAssets = true;
    bool showCastleAssets = true;
    Vector3 cursorPosition;
    Vector3 cursorNormal = Vector3.up;
    Bounds selectedBounds = new Bounds(Vector3.zero, Vector3.one);
    GUIStyle selectedButton;
    GUIStyle nameStyle;
    GUIStyle tabButtonStyle;
    GUIStyle tabLabelStyle;
    GameObject previewInstance;
    GameObject previewSource;
    bool floorCleanupScheduled;
    [SerializeField] bool floorRectangleFill = true;
    bool floorDragActive;
    bool wallPerimeterDrag;
    Vector3 floorDragStart;
    Vector2 floorDragStep;
    int floorDragX;
    int floorDragZ;
    const int MaxFloorFillTiles = 1024;
    [SerializeField] bool floorIsolationActive;
    [SerializeField] bool placementExpanded = true;
    [SerializeField] bool floorOptionsExpanded = true;
    [SerializeField] bool roomAssetsExpanded = true;

    [MenuItem("Tools/Fantasy Kingdom/House Builder %#h")]
    public static void Open()
    {
        FantasyKingdomHouseBuilder window = GetWindow<FantasyKingdomHouseBuilder>();
        window.titleContent = new GUIContent("House Builder");
        window.minSize = new Vector2(340f, 420f);
        window.Show();
    }

    void OnEnable()
    {
        if (!EditorPrefs.GetBool(PrefCalibrated, false))
        {
            EditorPrefs.SetFloat(PrefGrid, KitHalfModule);
            EditorPrefs.SetFloat(PrefLevelStep, KitStoreyHeight);
            EditorPrefs.SetBool(PrefCalibrated, true);
        }
        gridSize = EditorPrefs.GetFloat(PrefGrid, KitHalfModule);
        level = EditorPrefs.GetFloat(PrefLevel, 0f);
        levelStep = EditorPrefs.GetFloat(PrefLevelStep, KitStoreyHeight);
        floorOffset = EditorPrefs.GetFloat(PrefFloorOffset, 0f);
        bottomAnchoredPlacement = EditorPrefs.GetBool(PrefBottomAnchor, true);
        showHouseAssets = EditorPrefs.GetBool(PrefShowHouse, true);
        showCastleAssets = EditorPrefs.GetBool(PrefShowCastle, true);
        RefreshAssets();
        pickerShortcutContext = new PickerShortcutContext(this);
        ShortcutManager.RegisterContext(pickerShortcutContext);
        SceneView.beforeSceneGui += ClaimSceneViewInput;
        SceneView.duringSceneGui += DuringSceneGUI;
        EditorApplication.update += RepaintWhilePreviewsLoad;
        EditorApplication.hierarchyChanged += ScheduleEmptyFloorCleanup;
    }

    void OnDisable()
    {
        CancelFloorDrag();
        if (pickerShortcutContext != null) ShortcutManager.UnregisterContext(pickerShortcutContext);
        pickerShortcutContext = null;
        pendingPickerSceneView = null;
        SceneView.beforeSceneGui -= ClaimSceneViewInput;
        SceneView.duringSceneGui -= DuringSceneGUI;
        EditorApplication.update -= RepaintWhilePreviewsLoad;
        EditorApplication.hierarchyChanged -= ScheduleEmptyFloorCleanup;
        EditorApplication.delayCall -= CleanupEmptyFloorGroups;
        RestoreUnityToolIfNeeded();
        DestroyPreview();
        EditorPrefs.SetFloat(PrefGrid, gridSize);
        EditorPrefs.SetFloat(PrefLevel, level);
        EditorPrefs.SetFloat(PrefLevelStep, levelStep);
        EditorPrefs.SetFloat(PrefFloorOffset, floorOffset);
        EditorPrefs.SetBool(PrefBottomAnchor, bottomAnchoredPlacement);
        EditorPrefs.SetBool(PrefShowHouse, showHouseAssets);
        EditorPrefs.SetBool(PrefShowCastle, showCastleAssets);
    }

    void OnGUI()
    {
        if (HandleBuilderWindowShortcut(Event.current)) return;
        int previousFloor = CurrentFloorIndex();
        EnsureStyles();
        bool wideLayout = position.width >= WideLayoutThreshold;
        float paletteWidth = wideLayout ? position.width - ControlsColumnWidth - ColumnSpacing : position.width;
        if (wideLayout) EditorGUILayout.BeginHorizontal();
        EditorGUILayout.BeginVertical(wideLayout ? GUILayout.Width(ControlsColumnWidth) : GUILayout.ExpandWidth(true));
        float previousLabelWidth = EditorGUIUtility.labelWidth;
        if (wideLayout)
        {
            EditorGUIUtility.labelWidth = 145f;
            controlsScroll.x = 0f;
            controlsContentWidth = ControlsColumnWidth - Mathf.Max(15f, GUI.skin.verticalScrollbar.fixedWidth) - 8f;
            controlsScroll = GUILayout.BeginScrollView(controlsScroll, false, true, GUIStyle.none, GUI.skin.verticalScrollbar,
                GUILayout.Width(ControlsColumnWidth));
            // A vertical scroll view otherwise sizes its content to the widest child, even
            // though the parent column clips that width. Fix the content, not just its rows.
            EditorGUILayout.BeginVertical(GUILayout.Width(controlsContentWidth), GUILayout.MinWidth(controlsContentWidth), GUILayout.MaxWidth(controlsContentWidth));
        }
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("POLYGON Fantasy Kingdom — Building & Environment Editor", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("P: SAMPLE UNDER CURSOR    U: TOGGLE EDITOR    D: EMPTY CURSOR\nI: TOGGLE FLOOR ISOLATION    O: REPLACE SELECTED\nDELETE: ERASE UNDER CURSOR (BUILD ON)\nA/S: ROTATE    Z/X: FLOORS    Q/W/E/R/T: UNITY TOOLS\nWindow width: " + position.width.ToString("0") + " px", MessageType.Info);

        bool buildModeBeforeToolbar = buildMode;
        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            buildMode = GUILayout.Toggle(buildMode, buildMode ? "BUILD MODE ON" : "BUILD MODE OFF", EditorStyles.toolbarButton, GUILayout.Width(120));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(58))) RefreshAssets();
        }
        SyncUnityToolForBuildMode();
        if (!buildModeBeforeToolbar && buildMode) SceneView.lastActiveSceneView?.Focus();
        int previousTab = roomTab ? 3 : propsTab ? 1 : IsEnvironmentTab ? 2 : 0;
        int nextTab = DrawEditorTabs(previousTab);
        if (nextTab != previousTab)
        {
            CancelFloorDrag();
            propsTab = nextTab == 1;
            environmentTab = nextTab == 2;
            roomTab = nextTab == 3;
            categoryIndex = 0;
            search = "";
            scroll = Vector2.zero;
            ApplyFilter();
        }
        if (roomTab) DrawRoomSlots();

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            placementExpanded = EditorGUILayout.Foldout(placementExpanded, "Placement", true, EditorStyles.foldoutHeader);
            if (placementExpanded)
            {
            EditorGUILayout.LabelField("Measured kit module: 2.5 m full / 1.25 m half", EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(CurrentFloorLabel(), MessageType.None);
            gridSize = Mathf.Max(.05f, EditorGUILayout.FloatField(new GUIContent("Grid Size", "XZ snap spacing"), gridSize));
            if (UsesPropPlacement)
            {
                if (IsEnvironmentTab)
                {
                    int placement = EditorGUILayout.Popup("Environment Placement", environmentOnSurface ? 1 : 0,
                        new[] { "Active Floor", "Surface Under Cursor" });
                    environmentOnSurface = placement == 1;
                    if (environmentOnSurface)
                        environmentAlignToSurface = EditorGUILayout.ToggleLeft(new GUIContent("Align to Surface Normal", "Rotate the item's up axis to match the surface under the cursor. Rotation still turns around that surface normal."), environmentAlignToSurface);
                }
                else
                {
                    int propPlacement = EditorGUILayout.Popup("Prop Placement", propsOnWall ? 2 : propsOnSurface ? 1 : 0,
                        new[] { "Active Floor", "Surface Under Cursor", "Wall" });
                    propsOnWall = propPlacement == 2;
                    propsOnSurface = propPlacement == 1;
                }
                if (SurfacePlacement)
                    useActualMeshSurface = EditorGUILayout.ToggleLeft(new GUIContent("Use Actual Mesh Surface", "Place on visible static mesh triangles instead of colliders; useful for individual shelf boards"), useActualMeshSurface);
                if (WallPlacement)
                    propWallFacing = EditorGUILayout.Popup("Prop Front Axis", propWallFacing, new[] { "+Z", "-Z", "+X", "-X" });
                propSnapping = EditorGUILayout.Toggle(IsEnvironmentTab ? "Snap to Grid" : "Snap Props to Grid", propSnapping);
                if (propSnapping) propGrid = Mathf.Max(.01f, EditorGUILayout.FloatField(IsEnvironmentTab ? "Placement Grid Size" : "Prop Grid Size", propGrid));
                propSurfaceOffset = EditorGUILayout.FloatField(IsEnvironmentTab ? "Surface Offset" : "Prop Surface Offset", propSurfaceOffset);
                propRotationStep = Mathf.Max(1f, EditorGUILayout.FloatField(IsEnvironmentTab ? "Rotation Step" : "Prop Rotation Step", propRotationStep));
                EditorGUILayout.HelpBox(WallPlacement
                    ? "Aim at a visible wall " + (useActualMeshSurface ? "mesh" : "collider") + ". The mesh back sits against the wall; Surface Offset sets the gap. Change Front Axis if the model faces sideways. A/S rotate around the wall normal."
                    : useActualMeshSurface && SurfacePlacement
                        ? "Placement uses visible static mesh surfaces, including individual shelf boards. No collider is required. If no support is found, the active floor is used."
                        : (IsEnvironmentTab ? "Environment pieces" : "Props") + " sit on their mesh bottom. Surface placement needs a visible collider; otherwise the active floor is used. They stay upright unless surface alignment is enabled.", MessageType.None);
            }
            levelStep = Mathf.Max(.05f, EditorGUILayout.FloatField(new GUIContent("Floor Height", "Page Up/Down or Z/X floor increment"), levelStep));
            floorOffset = EditorGUILayout.FloatField(new GUIContent("Floor Offset", "Additional elevation for split levels, half floors, or raised rooms"), floorOffset);
            using (new EditorGUILayout.HorizontalScope())
            {
                level = EditorGUILayout.FloatField("Current Level", level);
                if (GUILayout.Button("−", GUILayout.Width(28))) ChangeLevel(-1);
                if (GUILayout.Button("+", GUILayout.Width(28))) ChangeLevel(1);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                yaw = EditorGUILayout.Slider("Rotation", yaw, 0f, 359f);
                if (GUILayout.Button("↻ " + (UsesPropPlacement ? propRotationStep : 90f).ToString("0.#") + "°", GUILayout.Width(62))) RotateSelection(90f);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀ Previous  [")) CyclePrefab(-1);
                if (GUILayout.Button("Next  ] ▶")) CyclePrefab(1);
            }
            if (GUILayout.Button("Replace Selected With Active Piece")) ReplaceSelected();
            }
        }

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            floorOptionsExpanded = EditorGUILayout.Foldout(floorOptionsExpanded, "Options & Floor Organization", true, EditorStyles.foldoutHeader);
            if (floorOptionsExpanded)
            {
            if (!IsEnvironmentTab)
                alignToSurface = EditorGUILayout.ToggleLeft("Align up-axis to clicked surface", alignToSurface);
            bottomAnchoredPlacement = EditorGUILayout.ToggleLeft("Anchor placement at mesh bottom", bottomAnchoredPlacement);
            showNames = EditorGUILayout.ToggleLeft("Show piece name at cursor", showNames);
            showFloorGuide = EditorGUILayout.ToggleLeft("Show active floor grid and height label", showFloorGuide);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select Build Root")) Selection.activeGameObject = GetBuildRoot(false);
                if (GUILayout.Button("Frame Build")) FrameBuild();
            }
            EditorGUILayout.LabelField("Floor Organization", EditorStyles.boldLabel);
            if (floorIsolationActive)
                EditorGUILayout.LabelField("Isolation follows current floor", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Isolate Current Floor")) IsolateCurrentFloor();
                if (GUILayout.Button("Show All Floors")) ShowAllFloors();
            }
            if (GUILayout.Button("Tag Selected to Current Floor")) TagSelectedToCurrentFloor();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Half Grid (1.25 m)")) gridSize = KitHalfModule;
                if (GUILayout.Button("Use Full Grid (2.5 m)")) gridSize = 2.5f;
            }
            }
        }

        EditorGUILayout.Space(3);
        search = EditorGUILayout.TextField("Search", search);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            if (IsEnvironmentTab)
            {
                showEnvironmentAssets = EditorGUILayout.ToggleLeft(new GUIContent("Environments", "Environment prefabs and the path-brick pieces from Props/Paths"), showEnvironmentAssets, GUILayout.MinWidth(110f));
                showGenericAssets = EditorGUILayout.ToggleLeft("Generic", showGenericAssets, GUILayout.MinWidth(110f));
            }
            else
            {
                showHouseAssets = EditorGUILayout.ToggleLeft("House Assets", showHouseAssets, GUILayout.MinWidth(110f));
                showCastleAssets = EditorGUILayout.ToggleLeft("Castle Assets", showCastleAssets, GUILayout.MinWidth(110f));
            }
        }
        string[] activeCategories = ActiveCategories;
        int nextCategory = GUILayout.SelectionGrid(categoryIndex, activeCategories, 3);
        if (nextCategory != categoryIndex) { categoryIndex = nextCategory; ApplyFilter(); }

        if (wideLayout)
        {
            EditorGUILayout.EndVertical();
            GUILayout.EndScrollView();
        }
        EditorGUIUtility.labelWidth = previousLabelWidth;
        EditorGUILayout.EndVertical();
        if (wideLayout) GUILayout.Space(8f);
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField((IsEnvironmentTab ? "Environment Assets" : propsTab ? "Props & Furniture" : "Building Assets") + " — " + shownPrefabs.Count + " assets", EditorStyles.miniBoldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawPalette(paletteWidth);
        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
        if (wideLayout) EditorGUILayout.EndHorizontal();

        if (floorIsolationActive && previousFloor != CurrentFloorIndex()) ApplyFloorIsolation();

        if (GUI.changed)
        {
            ApplyFilter();
            SceneView.RepaintAll();
        }
    }

    int DrawEditorTabs(int selectedTab)
    {
        string[] labels = { "Buildings", "Props", "Environment", "Room Creation" };
        Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
        int nextTab = selectedTab;
        for (int i = 0; i < labels.Length; i++)
        {
            float left = row.x + row.width * i / labels.Length;
            float right = row.x + row.width * (i + 1) / labels.Length;
            Rect button = new Rect(left, row.y, right - left, row.height);
            // Toolbar substitutes ButtonLeft/Mid/Right skin styles, losing custom alignment.
            // Draw the selection background and left-aligned text separately instead.
            bool selected = selectedTab == i;
            if (GUI.Toggle(button, selected, GUIContent.none, tabButtonStyle) && !selected) nextTab = i;
            GUI.Label(button, labels[i], tabLabelStyle);
        }
        return nextTab;
    }

    bool HandleBuilderWindowShortcut(Event e)
    {
        if (e.type != EventType.KeyDown || EditorGUIUtility.editingTextField ||
            e.control || e.command || e.alt) return false;

        if (e.keyCode == KeyCode.O && !floorDragActive)
        {
            ReplaceSelected();
            e.Use();
            return true;
        }

        if (TryGetUnityTool(e.keyCode, out Tool unityTool))
        {
            buildMode = false;
            CancelFloorDrag();
            DestroyPreview();
            SyncUnityToolForBuildMode();
            SelectUnityTool(unityTool);
            Repaint();
            e.Use();
            return true;
        }

        if (e.keyCode == KeyCode.U)
        {
            buildMode = !buildMode;
            if (!buildMode)
            {
                CancelFloorDrag();
                DestroyPreview();
            }
            SyncUnityToolForBuildMode();
            if (buildMode) SceneView.lastActiveSceneView?.Focus();
            e.Use();
            Repaint();
            return true;
        }

        if (e.keyCode == KeyCode.I)
        {
            if (floorIsolationActive) ShowAllFloors();
            else IsolateCurrentFloor();
            e.Use();
            Repaint();
            return true;
        }

        if (buildMode)
        {
            if (e.keyCode == KeyCode.D)
            {
                emptyCursorMode = !emptyCursorMode;
                Repaint();
                SceneView.RepaintAll();
                e.Use();
                return true;
            }
            if (e.keyCode == KeyCode.A) RotateSelection(-90f);
            else if (e.keyCode == KeyCode.S) RotateSelection(90f);
            else if (e.keyCode == KeyCode.Z) ChangeLevel(-1);
            else if (e.keyCode == KeyCode.X) ChangeLevel(1);
            else if (e.keyCode == KeyCode.PageUp) ChangeLevel(1);
            else if (e.keyCode == KeyCode.PageDown) ChangeLevel(-1);
            else if (e.keyCode == KeyCode.LeftBracket) CyclePrefab(-1);
            else if (e.keyCode == KeyCode.RightBracket) CyclePrefab(1);
            else return false;
            if (e.keyCode == KeyCode.LeftBracket || e.keyCode == KeyCode.RightBracket)
                emptyCursorMode = false;
            e.Use();
            return true;
        }

        if (e.keyCode == KeyCode.A) RotateSelectedObjectAroundCenter(-90f);
        else if (e.keyCode == KeyCode.S) RotateSelectedObjectAroundCenter(90f);
        else if (e.keyCode == KeyCode.LeftBracket) CycleSelectedObject(-1);
        else if (e.keyCode == KeyCode.RightBracket) CycleSelectedObject(1);
        else return false;
        e.Use();
        return true;
    }

    static bool TryGetUnityTool(KeyCode key, out Tool tool)
    {
        switch (key)
        {
            case KeyCode.Q: tool = Tool.View; return true;
            case KeyCode.W: tool = Tool.Move; return true;
            case KeyCode.E: tool = Tool.Rotate; return true;
            case KeyCode.R: tool = Tool.Scale; return true;
            case KeyCode.T: tool = Tool.Rect; return true;
            default: tool = Tool.None; return false;
        }
    }

    static void SelectUnityTool(Tool tool)
    {
        Tools.current = tool;
        SceneView.lastActiveSceneView?.Focus();
    }

    void DrawPalette(float availableWidth)
    {
        const float tileWidth = AssetTileWidth;
        int columns = Mathf.Max(1, Mathf.FloorToInt((availableWidth - PalettePadding) / tileWidth));
        for (int row = 0; row * columns < shownPrefabs.Count; row++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                for (int col = 0; col < columns; col++)
                {
                    int index = row * columns + col;
                    if (index >= shownPrefabs.Count) { GUILayout.FlexibleSpace(); continue; }
                    GameObject prefab = shownPrefabs[index];
                    bool selected = index == selectedIndex;
                    Texture2D preview = AssetPreview.GetAssetPreview(prefab);
                    GUIContent content = new GUIContent(preview ? preview : AssetPreview.GetMiniThumbnail(prefab), CleanName(prefab.name));
                    if (GUILayout.Button(content, selected ? selectedButton : GUI.skin.button, GUILayout.Width(tileWidth - 4f), GUILayout.Height(78f)))
                        ChoosePrefabFromUI(index);
                }
            }
        }
    }

    void ClaimSceneViewInput(SceneView sceneView)
    {
        if (!buildMode || EditorGUIUtility.editingTextField) return;
        SyncUnityToolForBuildMode();
        Event e = Event.current;
        if (e.type == EventType.Layout)
            HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        else if (e.type == EventType.KeyDown && !e.control && !e.command && !e.alt)
            GUIUtility.keyboardControl = 0;
    }

    void DuringSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;
        // Let Scene-view text controls keep their keystrokes, including Delete/Escape.
        if (e.isKey && EditorGUIUtility.editingTextField) return;
        SyncUnityToolForBuildMode();
        if (pendingPickerSceneView == sceneView && e.type == EventType.Repaint)
        {
            pendingPickerSceneView = null;
            if (!floorDragActive && !EditorGUIUtility.editingTextField)
            {
                emptyCursorMode = false;
                PickPrefabAt(e.mousePosition);
            }
        }
        int floorDragControl = GUIUtility.GetControlID("FantasyKingdomFloorRectangle".GetHashCode(), FocusType.Passive);
        if (floorDragActive)
        {
            HandleFloorDrag(e);
            return;
        }
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.O &&
            !e.control && !e.command && !e.alt && !EditorGUIUtility.editingTextField)
        {
            ReplaceSelected();
            e.Use();
            return;
        }
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.I &&
            !e.control && !e.command && !e.alt && !EditorGUIUtility.editingTextField)
        {
            if (floorIsolationActive) ShowAllFloors();
            else IsolateCurrentFloor();
            Repaint();
            e.Use();
            return;
        }
        // Eyedropper and erase are immediate key actions, never persistent modes.
        pickerMode = false;
        eraseMode = false;
        if (!buildMode)
        {
            DestroyPreview();
            bool plainShortcut = !e.control && !e.command && !e.alt;
            if (plainShortcut && e.type == EventType.KeyDown && e.keyCode == KeyCode.U)
            {
                buildMode = true;
                SyncUnityToolForBuildMode();
                RebuildPreview();
                Repaint();
                SceneView.RepaintAll();
                e.Use();
            }
            else if (plainShortcut && e.type == EventType.KeyDown && e.keyCode == KeyCode.P)
            {
                PickPrefabAt(e.mousePosition); e.Use();
            }
            else if (plainShortcut && e.type == EventType.KeyDown && e.keyCode == KeyCode.E)
            {
                Tools.current = Tool.Rotate;
                SceneView.RepaintAll();
                e.Use();
            }
            else if (plainShortcut && e.type == EventType.KeyDown && e.keyCode == KeyCode.A)
            {
                RotateSelectedObjectAroundCenter(-90f); e.Use();
            }
            else if (plainShortcut && e.type == EventType.KeyDown && e.keyCode == KeyCode.S)
            {
                RotateSelectedObjectAroundCenter(90f); e.Use();
            }
            else if (plainShortcut && e.type == EventType.KeyDown && e.keyCode == KeyCode.LeftBracket)
            {
                CycleSelectedObject(-1); e.Use();
            }
            else if (plainShortcut && e.type == EventType.KeyDown && e.keyCode == KeyCode.RightBracket)
            {
                CycleSelectedObject(1); e.Use();
            }
            return;
        }
        // Sampling, erasing and exiting Build Mode also work with an empty palette.
        // Preview and placement validate the active prefab independently.
        bool temporaryErase = e.shift;
        bool erasing = eraseMode || temporaryErase;

        UpdateCursor(e.mousePosition);
        UpdateMeshPreview(erasing || pickerMode || emptyCursorMode || roomTab);
        if (showFloorGuide)
        {
            DrawFloorGuide();
            DrawVerticalRuler(sceneView);
        }
        DrawCursor(erasing, pickerMode, emptyCursorMode);

        if (e.type == EventType.KeyDown)
        {
            bool plainShortcut = !e.control && !e.command && !e.alt;
            if (plainShortcut && e.keyCode == KeyCode.P)
            {
                emptyCursorMode = false;
                PickPrefabAt(e.mousePosition); e.Use();
                return;
            }
            if (plainShortcut && e.keyCode == KeyCode.Delete)
            {
                EraseAt(e.mousePosition); e.Use();
                return;
            }
            if (plainShortcut && e.keyCode == KeyCode.D)
            {
                emptyCursorMode = !emptyCursorMode;
                Repaint(); SceneView.RepaintAll(); e.Use();
                return;
            }
            if (plainShortcut && e.keyCode == KeyCode.U)
            {
                buildMode = false;
                pickerMode = false;
                eraseMode = false;
                DestroyPreview();
                SyncUnityToolForBuildMode();
                Repaint(); SceneView.RepaintAll(); e.Use();
                return;
            }
            if (plainShortcut && TryGetUnityTool(e.keyCode, out Tool unityTool))
            {
                buildMode = false;
                pickerMode = false;
                eraseMode = false;
                DestroyPreview();
                SyncUnityToolForBuildMode();
                SelectUnityTool(unityTool);
                Repaint();
                SceneView.RepaintAll();
                e.Use();
                return;
            }
            if (plainShortcut && e.keyCode == KeyCode.A) { RotateSelection(-90f); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.S) { RotateSelection(90f); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.Z) { ChangeLevel(-1); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.X) { ChangeLevel(1); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.PageUp) { ChangeLevel(1); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.PageDown) { ChangeLevel(-1); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.LeftBracket) { emptyCursorMode = false; CyclePrefab(-1); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.RightBracket) { emptyCursorMode = false; CyclePrefab(1); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.Escape)
            {
                buildMode = false;
                pickerMode = false;
                eraseMode = false;
                DestroyPreview();
                SyncUnityToolForBuildMode();
                Repaint(); SceneView.RepaintAll(); e.Use();
            }
        }

        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            if (pickerMode) PickPrefabAt(e.mousePosition);
            else if (erasing) EraseAt(e.mousePosition);
            else if (!emptyCursorMode)
            {
                if (roomTab && !PrepareRoomAssets())
                {
                    ShowNotification(new GUIContent("Assign at least one valid room asset slot"));
                }
                else if (cursorValid && (roomTab || CanFillFloorRectangle() || CanFillWallPerimeter()))
                {
                    floorDragActive = true;
                    roomRectangleDrag = roomTab;
                    if (roomRectangleDrag) CacheRoomOccupants();
                    wallPerimeterDrag = !roomTab && CanFillWallPerimeter();
                    floorDragStart = cursorPosition;
                    floorDragStep = roomTab ? new Vector2(CommonModule(roomTileSize.x, roomWallSpan), CommonModule(roomTileSize.y, roomWallSpan))
                        : wallPerimeterDrag ? Vector2.one * GetWallSpan() : GetFloorTileSpacing();
                    floorDragX = floorDragZ = 0;
                    GUIUtility.hotControl = floorDragControl;
                    SceneView.RepaintAll();
                }
                else if (!roomTab) PlaceSelected();
            }
            e.Use();
        }
    }

    bool CanFillFloorRectangle()
    {
        if (UsesPropPlacement || !floorRectangleFill || selectedIndex < 0 || selectedIndex >= shownPrefabs.Count) return false;
        string name = shownPrefabs[selectedIndex].name;
        // Rectangular floor panels only: exclude trim beams, rounded sections,
        // hatches, gaps, and decorative flooring whose footprint is not a tile.
        return name.IndexOf("_Floor_", StringComparison.OrdinalIgnoreCase) >= 0 &&
            !new[] { "beam", "round", "gap", "pool", "hatch" }.Any(s => name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0) &&
            !alignToSurface && Mathf.Abs(Mathf.DeltaAngle(yaw, Mathf.Round(yaw / 90f) * 90f)) < .01f;
    }

    void DrawRoomSlots()
    {
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            roomAssetsExpanded = EditorGUILayout.Foldout(roomAssetsExpanded, "Room Assets", true, EditorStyles.foldoutHeader);
            if (!roomAssetsExpanded) return;
            // ToggleLeft's automatic minimum width applies twice in a horizontal layout,
            // forcing this entire help box wider than the controls scroll viewport.
            Rect toggles = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight, GUILayout.MinWidth(0));
            float toggleWidth = Mathf.Max(0f, (toggles.width - 6f) * 0.5f);
            roomReplaceExisting = EditorGUI.ToggleLeft(new Rect(toggles.x, toggles.y, toggleWidth, toggles.height),
                new GUIContent("Replace Existing", "Replace overlapping pieces of the same room role"), roomReplaceExisting);
            ignoreRoomAssetTypes = EditorGUI.ToggleLeft(new Rect(toggles.x + toggleWidth + 6f, toggles.y, toggleWidth, toggles.height),
                new GUIContent("Ignore Asset Types", "Allow any supported prefab in any room slot"), ignoreRoomAssetTypes);
            if (ignoreRoomAssetTypes)
                EditorGUILayout.LabelField("Any supported prefab can fill any slot; the slot determines its room role.", EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.HelpBox("Choose any combination of slots; empty slots are skipped. P samples the prefab under the Scene-view cursor into the selected slot. Replace Existing replaces overlapping pieces of the same type. Otherwise existing pieces are kept. Drag opposite room corners to apply.", MessageType.None);
            string[] labels = { "Floor Tile", "Wall", "Corner Pillar", "Between-wall Pillar" };
            GameObject[] values = { roomFloor, roomWall, roomCornerPillar, roomEdgePillar };
            for (int i = 0; i < labels.Length; i++)
            {
                Rect row = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f, GUILayout.MinWidth(0));
                GetRoomSlotRects(row, out Rect selector, out Rect field, out Rect clear);
                if (Event.current.type == EventType.Repaint) roomSlotClearRects[i] = clear;
                if (GUI.Toggle(selector, roomSlot == i, labels[i], GUI.skin.button)) roomSlot = i;
                GameObject next = (GameObject)EditorGUI.ObjectField(field, values[i], typeof(GameObject), false);
                if (next != values[i])
                {
                    if (!next || ValidRoomSlot(next, i)) SetRoomSlot(i, next);
                    else ShowNotification(new GUIContent("Use a compatible House/Castle prefab for this slot"));
                }
                if (GUI.Button(clear, new GUIContent("×", "Clear this slot"))) SetRoomSlot(i, null);
            }
            if (!roomWall)
                roomJointSpacing = Mathf.Max(KitHalfModule, EditorGUILayout.FloatField("Wall Joint Spacing", roomJointSpacing));
            EditorGUILayout.LabelField("Empty slots create and replace nothing. Without a wall, Joint Spacing controls pillar intervals.", EditorStyles.wordWrappedMiniLabel);
        }
    }

    static void GetRoomSlotRects(Rect row, out Rect selector, out Rect field, out Rect clear)
    {
        const float clearWidth = 23f, gap = 4f;
        // Reserve the clear button first. GUILayout's ObjectField minimum width can otherwise
        // push the trailing button outside the 350px controls column and its scroll viewport.
        float selectorWidth = Mathf.Min(135f, Mathf.Max(0f, row.width - clearWidth - gap * 2f) * 0.45f);
        selector = new Rect(row.x, row.y, selectorWidth, row.height);
        clear = new Rect(row.xMax - clearWidth, row.y, clearWidth, row.height);
        field = new Rect(selector.xMax + gap, row.y, Mathf.Max(0f, clear.x - gap - selector.xMax - gap), row.height);
    }

    bool ValidRoomSlot(GameObject prefab, int slot)
    {
        string path = AssetDatabase.GetAssetPath(prefab);
        if (!IsBuilderAssetPath(path)) return false;
        if (ignoreRoomAssetTypes) return true;
        if (IsPropPath(path) || IsEnvironmentPath(path)) return false;
        string n = prefab.name.ToLowerInvariant();
        if (slot >= 2) return true;
        if (slot == 0)
            return n.Contains("_floor_") && !new[] { "beam", "round", "gap", "pool", "hatch" }.Any(n.Contains);
        return n.Contains("_wall_") && !new[] { "corner", "round", "curved", "peak", "cap", "beam", "angle", "cut", "destroyed", "rubble", "slope", "block", "corbel", "drain", "archway" }.Any(n.Contains);
    }

    void SetRoomSlot(int slot, GameObject value)
    {
        if (slot == 0) roomFloor = value;
        else if (slot == 1) roomWall = value;
        else if (slot == 2) roomCornerPillar = value;
        else roomEdgePillar = value;
    }

    void AssignRoomSlot(GameObject prefab)
    {
        if (ValidRoomSlot(prefab, roomSlot)) SetRoomSlot(roomSlot, prefab);
        else ShowNotification(new GUIContent("Select a compatible prefab for the active room slot"));
    }

    static float NominalModule(float size)
    {
        return Mathf.Max(KitHalfModule, Mathf.Round(Mathf.Abs(size) / KitHalfModule) * KitHalfModule);
    }

    static float CommonModule(float a, float b)
    {
        int x = Mathf.Max(1, Mathf.RoundToInt(a / KitHalfModule));
        int y = Mathf.Max(1, Mathf.RoundToInt(b / KitHalfModule));
        int p = x, q = y;
        while (q != 0) { int r = p % q; p = q; q = r; }
        return x / p * y * KitHalfModule;
    }

    bool PrepareRoomAssets()
    {
        if (!roomFloor && !roomWall && !roomCornerPillar && !roomEdgePillar) return false;
        if (roomFloor && !ValidRoomSlot(roomFloor, 0) || roomWall && !ValidRoomSlot(roomWall, 1)) return false;
        roomFloorBounds = roomFloor ? CalculateBounds(roomFloor) : default;
        roomWallBounds = roomWall ? CalculateBounds(roomWall) : default;
        roomCornerBounds = roomCornerPillar ? CalculateBounds(roomCornerPillar) : default;
        roomEdgeBounds = roomEdgePillar ? CalculateBounds(roomEdgePillar) : default;
        Vector3 size = roomFloor ? Vector3.Scale(roomFloorBounds.size, roomFloor.transform.localScale) : Vector3.one * NominalModule(roomJointSpacing);
        roomTileSize = new Vector2(NominalModule(size.x), NominalModule(size.z));
        size = roomWall ? Vector3.Scale(roomWallBounds.size, roomWall.transform.localScale) : Vector3.one * NominalModule(roomJointSpacing);
        roomWallSpan = NominalModule(Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)));
        if (!roomFloor) roomTileSize = Vector2.one * roomWallSpan;
        if (!roomWall && !roomEdgePillar) roomWallSpan = KitHalfModule;
        return true;
    }

    List<RoomPiece> GetRoomPlan(out int totalCount)
    {
        float width = Mathf.Max(1, Mathf.Abs(floorDragX)) * floorDragStep.x;
        float depth = Mathf.Max(1, Mathf.Abs(floorDragZ)) * floorDragStep.y;
        int fx = Mathf.RoundToInt(width / roomTileSize.x), fz = Mathf.RoundToInt(depth / roomTileSize.y);
        int wx = Mathf.RoundToInt(width / roomWallSpan), wz = Mathf.RoundToInt(depth / roomWallSpan);
        long count = (roomFloor ? (long)fx * fz : 0) + (roomWall ? 2L * (wx + wz) : 0) + (roomCornerPillar ? 4 : 0)
            + (roomEdgePillar ? 2L * (wx - 1 + wz - 1) : 0);
        totalCount = (int)Math.Min(int.MaxValue, count);
        var plan = new List<RoomPiece>();
        if (count > MaxFloorFillTiles) return plan;
        Vector3 min = floorDragStart + new Vector3(floorDragX < 0 ? -width : 0, 0, floorDragZ < 0 ? -depth : 0);
        HouseBuilderPieceKind slotKind = HouseBuilderPieceKind.Floor;
        Action<GameObject, Bounds, Vector3, float> add = (prefab, bounds, bottomCenter, degrees) =>
        {
            if (!prefab) return;
            Quaternion rotation = Quaternion.Euler(0, degrees, 0);
            Vector3 scaledCenter = Vector3.Scale(bounds.center, prefab.transform.localScale);
            scaledCenter.y = bounds.min.y * prefab.transform.localScale.y;
            plan.Add(new RoomPiece { prefab = prefab, bounds = bounds, rotation = rotation,
                kind = slotKind,
                position = bottomCenter - rotation * scaledCenter });
        };
        for (int x = 0; roomFloor && x < fx; x++) for (int z = 0; z < fz; z++)
            add(roomFloor, roomFloorBounds, min + new Vector3((x + .5f) * roomTileSize.x, 0, (z + .5f) * roomTileSize.y), 0);
        float thickness = roomFloor ? roomFloorBounds.size.y * Mathf.Abs(roomFloor.transform.localScale.y) : 0f;
        slotKind = HouseBuilderPieceKind.Wall;
        Vector3 wallOrigin = min + Vector3.up * thickness;
        Vector3 wallSize = roomWall ? Vector3.Scale(roomWallBounds.size, roomWall.transform.localScale) : Vector3.right;
        float northYaw = Mathf.Abs(wallSize.x) >= Mathf.Abs(wallSize.z) ? 0 : -90;
        for (int x = 0; roomWall && x < wx; x++)
        {
            add(roomWall, roomWallBounds, wallOrigin + new Vector3((x+.5f)*roomWallSpan, 0, 0), northYaw + 180);
            add(roomWall, roomWallBounds, wallOrigin + new Vector3((x+.5f)*roomWallSpan, 0, depth), northYaw);
        }
        for (int z = 0; roomWall && z < wz; z++)
        {
            add(roomWall, roomWallBounds, wallOrigin + new Vector3(0, 0, (z+.5f)*roomWallSpan), northYaw + 270);
            add(roomWall, roomWallBounds, wallOrigin + new Vector3(width, 0, (z+.5f)*roomWallSpan), northYaw + 90);
        }
        slotKind = HouseBuilderPieceKind.Pillar;
        if (roomCornerPillar)
        {
            add(roomCornerPillar, roomCornerBounds, wallOrigin, 180);
            add(roomCornerPillar, roomCornerBounds, wallOrigin + Vector3.right * width, 90);
            add(roomCornerPillar, roomCornerBounds, wallOrigin + new Vector3(width,0,depth), 0);
            add(roomCornerPillar, roomCornerBounds, wallOrigin + Vector3.forward * depth, 270);
        }
        if (roomEdgePillar)
        {
            for (int x = 1; x < wx; x++)
            {
                add(roomEdgePillar, roomEdgeBounds, wallOrigin + new Vector3(x*roomWallSpan,0,0), 180);
                add(roomEdgePillar, roomEdgeBounds, wallOrigin + new Vector3(x*roomWallSpan,0,depth), 0);
            }
            for (int z = 1; z < wz; z++)
            {
                add(roomEdgePillar, roomEdgeBounds, wallOrigin + new Vector3(0,0,z*roomWallSpan), 270);
                add(roomEdgePillar, roomEdgeBounds, wallOrigin + new Vector3(width,0,z*roomWallSpan), 90);
            }
        }
        return plan;
    }

    void HandleRoomRectangle(Event e)
    {
        if (e.type != EventType.Repaint && e.type != EventType.MouseUp && e.type != EventType.MouseDrag) return;
        if (e.type == EventType.MouseUp) CacheRoomOccupants();
        List<RoomPiece> plan = GetRoomPlan(out int count);
        var replacedInstances = new HashSet<GameObject>();
        roomSkippedPieces = 0;
        if (count <= MaxFloorFillTiles)
        {
            var occupied = roomReplaceExisting ? new List<RoomOccupant>() : new List<RoomOccupant>(roomOccupants);
            plan = plan.Where(piece =>
            {
                RoomOccupant candidate = RoomOccupancy(piece);
                if (occupied.Any(old => RoomPiecesOverlap(candidate, old)))
                {
                    roomSkippedPieces++;
                    return false;
                }
                if (roomReplaceExisting)
                    foreach (RoomOccupant old in roomOccupants)
                        if (old.instance && RoomPiecesOverlap(candidate, old)) replacedInstances.Add(old.instance);
                occupied.Add(candidate);
                return true;
            }).ToList();
        }
        if (e.type == EventType.Repaint)
        {
            Color previous = Handles.color;
            Handles.color = count > MaxFloorFillTiles ? Color.red : new Color(.15f,1f,.55f,.9f);
            foreach (RoomPiece piece in plan)
                using (new Handles.DrawingScope(Matrix4x4.TRS(piece.position, piece.rotation, piece.prefab.transform.localScale)))
                    Handles.DrawWireCube(piece.bounds.center, piece.bounds.size);
            if (roomReplaceExisting)
            {
                Handles.color = new Color(1f,.35f,.2f,.9f);
                foreach (RoomOccupant old in roomOccupants)
                    if (old.instance && replacedInstances.Contains(old.instance)) Handles.DrawWireCube(old.bounds.center, old.bounds.size);
            }
            Handles.Label(floorDragStart, plan.Count + " new pieces, " + replacedInstances.Count + " replaced, " + roomSkippedPieces + " skipped — " +
                (count > MaxFloorFillTiles ? "maximum 1024" : "release to build / Esc to cancel"));
            Handles.color = previous;
        }
        if (e.type == EventType.MouseUp && e.button == 0)
        {
            if (count <= MaxFloorFillTiles && plan.Count > 0)
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                try
                {
                    Transform parent = GetFloorRoot(CurrentFloorIndex(), true).transform;
                    foreach (RoomPiece piece in plan)
                    {
                        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(piece.prefab, parent.gameObject.scene);
                        Undo.RegisterCreatedObjectUndo(instance, "Create Room Piece");
                        instance.transform.SetParent(parent, true);
                        instance.transform.SetPositionAndRotation(piece.position, piece.rotation);
                        instance.transform.localScale = piece.prefab.transform.localScale;
                        float bottomY = piece.position.y + piece.bounds.min.y * piece.prefab.transform.localScale.y;
                        ApplyFloorMarker(instance, CurrentFloorIndex(), level, bottomY - level);
                        instance.GetComponent<HouseBuilderFloorMarker>().PieceKind = piece.kind;
                    }
                    foreach (GameObject old in replacedInstances)
                        if (old) Undo.DestroyObjectImmediate(old);
                    EditorSceneManager.MarkSceneDirty(parent.gameObject.scene);
                    if (floorIsolationActive) ApplyFloorIsolation();
                }
                finally { Undo.SetCurrentGroupName(roomReplaceExisting ? "Replace Room Assets" : "Create Room"); Undo.CollapseUndoOperations(group); }
            }
            else if (count > MaxFloorFillTiles) ShowNotification(new GUIContent("Room too large; maximum 1024 pieces"));
            if (count <= MaxFloorFillTiles)
                ShowNotification(new GUIContent(plan.Count + " placed; " + replacedInstances.Count + " replaced; " + roomSkippedPieces + " skipped"));
            CancelFloorDrag();
            e.Use();
        }
        else if (e.type == EventType.MouseDrag) { SceneView.RepaintAll(); e.Use(); }
    }

    void CacheRoomOccupants()
    {
        roomOccupants.Clear();
        GameObject buildRoot = GetBuildRoot(false);
        if (!buildRoot) return;
        foreach (Transform t in buildRoot.GetComponentsInChildren<Transform>(true))
        {
            if (PrefabUtility.GetNearestPrefabInstanceRoot(t.gameObject) != t.gameObject) continue;
            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
            if (!source || !IsBuilderAssetPath(AssetDatabase.GetAssetPath(source))) continue;
            HouseBuilderFloorMarker marker = t.GetComponent<HouseBuilderFloorMarker>();
            HouseBuilderPieceKind kind = marker ? marker.PieceKind : HouseBuilderPieceKind.Unspecified;
            if (kind == HouseBuilderPieceKind.Unspecified)
            {
                if (IsPropPath(AssetDatabase.GetAssetPath(source)) || IsEnvironmentPath(AssetDatabase.GetAssetPath(source))) continue;
                string n = source.name.ToLowerInvariant();
                if (source == roomFloor || n.Contains("_floor_")) kind = HouseBuilderPieceKind.Floor;
                else if (source == roomWall || n.Contains("_wall_")) kind = HouseBuilderPieceKind.Wall;
                else if (source == roomCornerPillar || source == roomEdgePillar || n.Contains("pillar") || n.Contains("column")) kind = HouseBuilderPieceKind.Pillar;
                else continue;
            }
            Bounds local = CalculateBounds(source);
            Bounds world = TransformRoomBounds(local, t.localToWorldMatrix);
            Vector3 dimensions = Vector3.Scale(local.size, t.lossyScale);
            Vector3 axis = t.TransformDirection(Mathf.Abs(dimensions.x) >= Mathf.Abs(dimensions.z) ? Vector3.right : Vector3.forward);
            axis.y = 0;
            roomOccupants.Add(new RoomOccupant { instance = t.gameObject, kind = kind, bounds = world, wallAxis = axis.normalized });
        }
    }

    static Bounds TransformRoomBounds(Bounds local, Matrix4x4 matrix)
    {
        Bounds world = new Bounds(matrix.MultiplyPoint3x4(local.center), Vector3.zero);
        for (int x = -1; x <= 1; x += 2) for (int y = -1; y <= 1; y += 2) for (int z = -1; z <= 1; z += 2)
            world.Encapsulate(matrix.MultiplyPoint3x4(local.center + Vector3.Scale(local.extents, new Vector3(x,y,z))));
        return world;
    }

    static RoomOccupant RoomOccupancy(RoomPiece piece)
    {
        Vector3 scale = piece.prefab.transform.localScale;
        Vector3 size = Vector3.Scale(piece.bounds.size, scale);
        Vector3 axis = piece.rotation * (Mathf.Abs(size.x) >= Mathf.Abs(size.z) ? Vector3.right : Vector3.forward);
        return new RoomOccupant { kind = piece.kind, wallAxis = axis,
            bounds = TransformRoomBounds(piece.bounds, Matrix4x4.TRS(piece.position, piece.rotation, scale)) };
    }

    static bool RoomPiecesOverlap(RoomOccupant a, RoomOccupant b)
    {
        if (a.kind != b.kind) return false;
        const float tolerance = .025f;
        Bounds x = a.bounds, y = b.bounds;
        float overlapX = Mathf.Min(x.max.x,y.max.x) - Mathf.Max(x.min.x,y.min.x);
        float overlapZ = Mathf.Min(x.max.z,y.max.z) - Mathf.Max(x.min.z,y.min.z);
        if (a.kind == HouseBuilderPieceKind.Floor)
        {
            // Floors can differ in thickness, but must occupy the same elevation.
            return Mathf.Abs(x.min.y-y.min.y) < .1f && overlapX > tolerance && overlapZ > tolerance;
        }
        float overlapY = Mathf.Min(x.max.y,y.max.y) - Mathf.Max(x.min.y,y.min.y);
        if (overlapY <= tolerance) return false;
        if (a.kind == HouseBuilderPieceKind.Pillar)
            return overlapX > tolerance && overlapZ > tolerance;
        // Share collinear wall segments regardless of their facing direction.
        // Perpendicular walls meeting at a corner are still allowed.
        if (Mathf.Abs(Vector3.Dot(a.wallAxis,b.wallAxis)) < .98f) return false;
        Vector3 normal = Vector3.Cross(Vector3.up,a.wallAxis);
        Vector3 delta = y.center-x.center;
        float lineTolerance = Mathf.Max(.03f, Mathf.Min(.1f, Mathf.Min(Mathf.Min(x.size.x,x.size.z),Mathf.Min(y.size.x,y.size.z)) * .25f));
        if (Mathf.Abs(Vector3.Dot(delta,normal)) > lineTolerance) return false;
        float spanA = Mathf.Abs(a.wallAxis.x)*x.size.x + Mathf.Abs(a.wallAxis.z)*x.size.z;
        float spanB = Mathf.Abs(a.wallAxis.x)*y.size.x + Mathf.Abs(a.wallAxis.z)*y.size.z;
        return (spanA+spanB)*.5f - Mathf.Abs(Vector3.Dot(delta,a.wallAxis)) > tolerance;
    }

    Vector2 GetFloorTileSpacing()
    {
        Vector3 size = Vector3.Scale(selectedBounds.size, shownPrefabs[selectedIndex].transform.localScale);
        // Decorative mesh edges may overhang slightly. Snap the authored span to
        // the kit's half-module so adjacent panels do not develop tiny gaps.
        float x = Mathf.Max(KitHalfModule, Mathf.Round(Mathf.Abs(size.x) / KitHalfModule) * KitHalfModule);
        float z = Mathf.Max(KitHalfModule, Mathf.Round(Mathf.Abs(size.z) / KitHalfModule) * KitHalfModule);
        if (Mathf.RoundToInt(yaw / 90f) % 2 != 0) return new Vector2(z, x);
        return new Vector2(x, z);
    }

    bool CanFillWallPerimeter()
    {
        if (UsesPropPlacement || !floorRectangleFill || alignToSurface || selectedIndex < 0 || selectedIndex >= shownPrefabs.Count) return false;
        string name = shownPrefabs[selectedIndex].name;
        return name.IndexOf("_Wall_", StringComparison.OrdinalIgnoreCase) >= 0 &&
            !new[] { "corner", "round", "curved", "peak", "cap", "beam", "angle", "cut", "destroyed", "rubble", "slope", "block", "corbel", "drain", "archway" }
                .Any(s => name.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    float GetWallSpan()
    {
        Vector3 size = Vector3.Scale(selectedBounds.size, shownPrefabs[selectedIndex].transform.localScale);
        return Mathf.Max(KitHalfModule, Mathf.Round(Mathf.Max(Mathf.Abs(size.x), Mathf.Abs(size.z)) / KitHalfModule) * KitHalfModule);
    }

    void HandleFloorDrag(Event e)
    {
        if (!buildMode || UsesPropPlacement || emptyCursorMode)
        {
            CancelFloorDrag();
            return;
        }
        if (e.type == EventType.KeyDown)
        {
            if (e.keyCode == KeyCode.Escape) CancelFloorDrag();
            // Keep the stroke's prefab, elevation and rotation stable until release.
            if (!e.control && !e.command && !e.alt) e.Use();
            return;
        }
        if (e.type == EventType.MouseDrag || e.type == EventType.MouseUp || e.type == EventType.Repaint)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
            Plane plane = new Plane(Vector3.up, floorDragStart);
            if (plane.Raycast(ray, out float distance) && distance < 10000f)
            {
                Vector3 delta = ray.GetPoint(distance) - floorDragStart;
                floorDragX = Mathf.Clamp(Mathf.RoundToInt(delta.x / floorDragStep.x), -1023, 1023);
                floorDragZ = Mathf.Clamp(Mathf.RoundToInt(delta.z / floorDragStep.y), -1023, 1023);
            }
        }
        int columns = Mathf.Abs(floorDragX) + 1;
        int rows = Mathf.Abs(floorDragZ) + 1;
        if (roomRectangleDrag)
        {
            HandleRoomRectangle(e);
            return;
        }
        if (wallPerimeterDrag)
        {
            HandleWallPerimeter(e);
            return;
        }
        int count = columns * rows;
        if (e.type == EventType.Repaint)
        {
            Color previous = Handles.color;
            Handles.color = count <= MaxFloorFillTiles ? new Color(.15f, 1f, .55f, .9f) : Color.red;
            Vector3 pivotCenter = floorDragStart + new Vector3(floorDragX * floorDragStep.x * .5f, 0f, floorDragZ * floorDragStep.y * .5f);
            Quaternion rotation = Quaternion.Euler(0f, yaw, 0f);
            Vector3 meshOffset = rotation * Vector3.Scale(selectedBounds.center, shownPrefabs[selectedIndex].transform.localScale);
            Vector3 center = pivotCenter + new Vector3(meshOffset.x, 0f, meshOffset.z);
            float width = columns * floorDragStep.x;
            float depth = rows * floorDragStep.y;
            Handles.DrawWireCube(center, new Vector3(width, .01f, depth));
            if (count <= MaxFloorFillTiles)
            {
                for (int x = 1; x < columns; x++)
                {
                    float dx = -width * .5f + x * floorDragStep.x;
                    Handles.DrawLine(center + new Vector3(dx, 0f, -depth * .5f), center + new Vector3(dx, 0f, depth * .5f));
                }
                for (int z = 1; z < rows; z++)
                {
                    float dz = -depth * .5f + z * floorDragStep.y;
                    Handles.DrawLine(center + new Vector3(-width * .5f, 0f, dz), center + new Vector3(width * .5f, 0f, dz));
                }
            }
            Handles.Label(center, columns + " × " + rows + " = " + count + " floor tiles" +
                (count > MaxFloorFillTiles ? " — maximum 1024 per stroke" : " — release to place / Esc to cancel"));
            Handles.color = previous;
        }
        if (e.type == EventType.MouseUp && e.button == 0)
        {
            if (count <= MaxFloorFillTiles)
            {
                Undo.IncrementCurrentGroup();
                int undoGroup = Undo.GetCurrentGroup();
                Vector3 previousCursor = cursorPosition;
                for (int x = 0; x < columns; x++)
                    for (int z = 0; z < rows; z++)
                    {
                        cursorPosition = floorDragStart + new Vector3(x * Mathf.Sign(floorDragX) * floorDragStep.x, 0f,
                            z * Mathf.Sign(floorDragZ) * floorDragStep.y);
                        cursorValid = true;
                        surfaceFloor = null;
                        PlaceSelected(false);
                    }
                cursorPosition = previousCursor;
                if (floorIsolationActive) ApplyFloorIsolation();
                Undo.SetCurrentGroupName("Fill Floor Rectangle");
                Undo.CollapseUndoOperations(undoGroup);
            }
            else ShowNotification(new GUIContent("Rectangle too large; maximum 1024 tiles per stroke"));
            CancelFloorDrag();
            e.Use();
        }
        else if (e.type == EventType.MouseDrag)
        {
            SceneView.RepaintAll();
            e.Use();
        }
    }

    void CancelFloorDrag()
    {
        if (!floorDragActive) return;
        floorDragActive = false;
        GUIUtility.hotControl = 0;
        SceneView.RepaintAll();
    }

    List<Pose> GetWallPerimeterPlan()
    {
        var plan = new List<Pose>();
        int nx = Mathf.Abs(floorDragX);
        int nz = Mathf.Abs(floorDragZ);
        if (nx == 0 && nz == 0)
        {
            plan.Add(new Pose(floorDragStart, Quaternion.Euler(0f, yaw, 0f)));
            return plan;
        }
        float span = floorDragStep.x;
        Vector3 min = floorDragStart + new Vector3(Mathf.Min(0, floorDragX) * span, 0f, Mathf.Min(0, floorDragZ) * span);
        Vector3 size = Vector3.Scale(selectedBounds.size, shownPrefabs[selectedIndex].transform.localScale);
        float northYaw = Mathf.Abs(size.x) >= Mathf.Abs(size.z) ? 0f : -90f;
        Action<Vector3, float> add = (center, rotation) =>
        {
            Quaternion facing = Quaternion.Euler(0f, rotation, 0f);
            Vector3 offset = facing * Vector3.Scale(selectedBounds.center, shownPrefabs[selectedIndex].transform.localScale);
            // Line centers define the rectangle, independent of offset prefab pivots.
            Vector3 pivot = center - new Vector3(offset.x, 0f, offset.z);
            plan.Add(new Pose(pivot, facing));
        };
        for (int x = 0; x < nx; x++)
        {
            add(min + new Vector3((x + .5f) * span, 0f, 0f), northYaw + 180f);
            if (nz > 0) add(min + new Vector3((x + .5f) * span, 0f, nz * span), northYaw);
        }
        for (int z = 0; z < nz; z++)
        {
            add(min + new Vector3(0f, 0f, (z + .5f) * span), northYaw + 270f);
            if (nx > 0) add(min + new Vector3(nx * span, 0f, (z + .5f) * span), northYaw + 90f);
        }
        return plan;
    }

    void HandleWallPerimeter(Event e)
    {
        List<Pose> plan = GetWallPerimeterPlan();
        if (e.type == EventType.Repaint)
        {
            Color previous = Handles.color;
            Handles.color = plan.Count <= MaxFloorFillTiles ? new Color(.15f, 1f, .55f, .9f) : Color.red;
            if (plan.Count <= MaxFloorFillTiles)
            {
                Vector3 scale = shownPrefabs[selectedIndex].transform.localScale;
                foreach (Pose pose in plan)
                {
                    using (new Handles.DrawingScope(Matrix4x4.TRS(pose.position, pose.rotation, scale)))
                    {
                        Vector3 center = selectedBounds.center;
                        if (bottomAnchoredPlacement) center.y -= selectedBounds.min.y;
                        Handles.DrawWireCube(center, selectedBounds.size);
                    }
                }
            }
            Handles.Label(floorDragStart, plan.Count + " walls around perimeter — " +
                (plan.Count > MaxFloorFillTiles ? "maximum 1024 per stroke" : "release to place / Esc to cancel"));
            Handles.color = previous;
        }
        if (e.type == EventType.MouseUp && e.button == 0)
        {
            if (plan.Count <= MaxFloorFillTiles)
            {
                Undo.IncrementCurrentGroup();
                int group = Undo.GetCurrentGroup();
                Vector3 previousCursor = cursorPosition;
                float previousYaw = yaw;
                try
                {
                    foreach (Pose pose in plan)
                    {
                        cursorPosition = pose.position;
                        yaw = pose.rotation.eulerAngles.y;
                        cursorValid = true;
                        surfaceFloor = null;
                        PlaceSelected(false);
                    }
                }
                finally
                {
                    cursorPosition = previousCursor;
                    yaw = previousYaw;
                    Undo.SetCurrentGroupName("Create Wall Perimeter");
                    Undo.CollapseUndoOperations(group);
                }
                if (floorIsolationActive) ApplyFloorIsolation();
            }
            else ShowNotification(new GUIContent("Perimeter too large; maximum 1024 walls"));
            CancelFloorDrag();
            e.Use();
        }
        else if (e.type == EventType.MouseDrag)
        {
            SceneView.RepaintAll();
            e.Use();
        }
    }

    IEnumerable<PlacementSurfaceHit> GetPlacementSurfaceHits(Ray ray)
    {
        if (useActualMeshSurface)
        {
            if (TryGetMeshSurface(ray, out PlacementSurfaceHit meshHit)) yield return meshHit;
            yield break;
        }
        foreach (RaycastHit hit in Physics.RaycastAll(ray, 10000f, ~0, QueryTriggerInteraction.Ignore).OrderBy(h => h.distance))
            yield return new PlacementSurfaceHit { point = hit.point, normal = hit.normal,
                distance = hit.distance, gameObject = hit.collider.gameObject };
    }

    bool TryGetMeshSurface(Ray ray, out PlacementSurfaceHit closest)
    {
        closest = default;
        float nearest = 10000f;
        if (surfaceMeshTargetsDirty || surfaceMeshTargets == null)
        {
            surfaceMeshTargets = UnityEngine.Object.FindObjectsByType<MeshFilter>()
                .Where(f => f.sharedMesh && f.gameObject.scene.IsValid() && !EditorUtility.IsPersistent(f)).ToArray();
            surfaceMeshTargetsDirty = false;
        }
        foreach (MeshFilter filter in surfaceMeshTargets)
        {
            if (!filter || !filter.sharedMesh || !filter.gameObject.activeInHierarchy) continue;
            MeshRenderer renderer = filter.GetComponent<MeshRenderer>();
            if (!renderer || !renderer.enabled || renderer.forceRenderingOff) continue;
            if (SceneVisibilityManager.instance.IsHidden(filter.gameObject)) continue;
            if (previewInstance && filter.transform.IsChildOf(previewInstance.transform)) continue;
            if (SceneView.currentDrawingSceneView && SceneView.currentDrawingSceneView.camera &&
                (SceneView.currentDrawingSceneView.camera.cullingMask & (1 << filter.gameObject.layer)) == 0) continue;
            if (!renderer.bounds.IntersectRay(ray, out float boundDistance) || boundDistance > nearest) continue;
            Mesh mesh = filter.sharedMesh;
            if (!surfaceMeshCache.TryGetValue(mesh, out SurfaceMeshData data))
            {
                // Unity Editor exposes imported mesh data without changing Read/Write
                // import settings. Cache it once rather than allocating on every ray.
                try { data = new SurfaceMeshData { vertices = mesh.vertices, triangles = mesh.triangles }; }
                catch (UnityException) { data = new SurfaceMeshData { vertices = new Vector3[0], triangles = new int[0] }; }
                surfaceMeshCache.Add(mesh, data);
            }
            Matrix4x4 inverse = filter.transform.worldToLocalMatrix;
            Vector3 origin = inverse.MultiplyPoint3x4(ray.origin);
            // Do not normalize: t must remain in world-ray distance units even
            // when the target has rotated or nonuniformly scaled ancestors.
            Vector3 direction = inverse.MultiplyVector(ray.direction);
            Matrix4x4 normalMatrix = inverse.transpose;
            for (int i = 0; i + 2 < data.triangles.Length; i += 3)
            {
                Vector3 a = data.vertices[data.triangles[i]];
                Vector3 edge1 = data.vertices[data.triangles[i + 1]] - a;
                Vector3 edge2 = data.vertices[data.triangles[i + 2]] - a;
                Vector3 cross = Vector3.Cross(direction, edge2);
                float determinant = Vector3.Dot(edge1, cross);
                if (Mathf.Abs(determinant) < 1e-8f) continue;
                float invDet = 1f / determinant;
                Vector3 delta = origin - a;
                float u = Vector3.Dot(delta, cross) * invDet;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(delta, edge1);
                float v = Vector3.Dot(direction, q) * invDet;
                if (v < 0f || u + v > 1f) continue;
                float distance = Vector3.Dot(edge2, q) * invDet;
                if (distance < .0001f || distance >= nearest) continue;
                Vector3 normal = normalMatrix.MultiplyVector(Vector3.Cross(edge1, edge2)).normalized;
                if (Vector3.Dot(normal, ray.direction) >= 0f) continue;
                if (WallPlacement ? Mathf.Abs(normal.y) > .35f : !AlignPlacementToSurface && normal.y < .5f) continue;
                nearest = distance;
                closest = new PlacementSurfaceHit { point = ray.GetPoint(distance), normal = normal,
                    distance = distance, gameObject = filter.gameObject };
            }
        }
        return closest.gameObject != null;
    }

    void UpdateCursor(Vector2 mousePosition)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        cursorNormal = Vector3.up;
        surfaceFloor = null;
        cursorValid = false;
        if (SurfacePlacement)
        {
            foreach (PlacementSurfaceHit hit in GetPlacementSurfaceHits(ray))
            {
                if (SceneVisibilityManager.instance.IsHidden(hit.gameObject)) continue;
                if (previewInstance && hit.gameObject.transform.IsChildOf(previewInstance.transform)) continue;
                // Upright props need a supporting surface; do not jump onto wall faces.
                if (WallPlacement ? Mathf.Abs(hit.normal.y) > .35f : !AlignPlacementToSurface && hit.normal.y < .5f) continue;
                Vector3 pointOnSurface = hit.point;
                if (propSnapping && WallPlacement)
                {
                    Vector3 wallRight = Vector3.Cross(Vector3.up, hit.normal).normalized;
                    Vector3 wallUp = Vector3.Cross(hit.normal, wallRight).normalized;
                    float rightDistance = Vector3.Dot(pointOnSurface, wallRight);
                    float upDistance = Vector3.Dot(pointOnSurface, wallUp);
                    pointOnSurface += wallRight * (Mathf.Round(rightDistance / propGrid) * propGrid - rightDistance)
                                    + wallUp * (Mathf.Round(upDistance / propGrid) * propGrid - upDistance);
                }
                else if (propSnapping)
                {
                    float snappedX = Mathf.Round(pointOnSurface.x / propGrid) * propGrid;
                    float snappedZ = Mathf.Round(pointOnSurface.z / propGrid) * propGrid;
                    Vector3 adjusted = new Vector3(snappedX, pointOnSurface.y, snappedZ);
                    if (Mathf.Abs(hit.normal.y) > .01f)
                        adjusted.y -= Vector3.Dot(hit.normal, adjusted - pointOnSurface) / hit.normal.y;
                    pointOnSurface = adjusted;
                }
                cursorPosition = pointOnSurface + (WallPlacement ? hit.normal : Vector3.up) * propSurfaceOffset;
                cursorNormal = hit.normal;
                surfaceFloor = hit.gameObject.GetComponentInParent<HouseBuilderFloorMarker>();
                cursorValid = true;
                return;
            }
            if (WallPlacement) return;
        }
        // Always project onto the active placement floor. Taking X/Z from a wall,
        // roof, or elevated collider then forcing Y back to this floor produces a
        // large apparent cursor offset.
        Plane plane = new Plane(Vector3.up, new Vector3(0f, EffectiveLevel(), 0f));
        if (!plane.Raycast(ray, out float distance) || distance > 10000f) return;
        Vector3 point = ray.GetPoint(distance);
        if (AlignPlacementToSurface && !(UsesPropPlacement && useActualMeshSurface) && Physics.Raycast(ray, out RaycastHit orientationHit, 10000f, ~0, QueryTriggerInteraction.Ignore))
            cursorNormal = orientationHit.normal;
        float spacing = UsesPropPlacement ? (propSnapping ? propGrid : 0f) : gridSize;
        cursorPosition = new Vector3(spacing > 0f ? Mathf.Round(point.x / spacing) * spacing : point.x,
            EffectiveLevel() + (UsesPropPlacement ? propSurfaceOffset : 0f),
            spacing > 0f ? Mathf.Round(point.z / spacing) * spacing : point.z);
        cursorValid = true;
    }

    void DrawCursor(bool erasing, bool picking, bool empty)
    {
        if (!cursorValid) return;
        Color color = empty ? new Color(.15f,1f,.55f,.95f) : picking ? new Color(.2f,.75f,1f,.95f) : erasing ? new Color(1f,.2f,.15f,.95f) : new Color(.15f,1f,.55f,.95f);
        Handles.color = color;
        float radius = Mathf.Max(.15f, gridSize * .18f);
        Vector3 markerNormal = WallPlacement ? cursorNormal : Vector3.up;
        Vector3 markerRight = WallPlacement ? Vector3.Cross(Vector3.up, markerNormal).normalized : Vector3.right;
        Vector3 markerOther = Vector3.Cross(markerNormal, markerRight).normalized;
        Handles.DrawWireDisc(cursorPosition, markerNormal, radius);
        Handles.DrawLine(cursorPosition - markerRight * radius, cursorPosition + markerRight * radius);
        Handles.DrawLine(cursorPosition - markerOther * radius, cursorPosition + markerOther * radius);
        if (empty)
        {
            SceneView.RepaintAll();
            return;
        }
        if (roomTab)
        {
            Handles.Label(cursorPosition + Vector3.up * .25f, "ROOM — drag opposite corners / Esc to cancel", nameStyle);
            SceneView.RepaintAll();
            return;
        }
        if (picking)
        {
            Handles.Label(cursorPosition + Vector3.up * .25f, "EYEDROPPER — click a placed house piece", nameStyle);
        }
        else if (!erasing && selectedIndex >= 0 && selectedIndex < shownPrefabs.Count)
        {
            if (showNames) Handles.Label(cursorPosition + Vector3.up * .25f, CleanName(shownPrefabs[selectedIndex].name), nameStyle);
        }
        SceneView.RepaintAll();
    }

    void DrawFloorGuide()
    {
        int floor = CurrentFloorIndex();
        float hue = Mathf.Repeat(.46f + floor * .11f, 1f);
        Color strong = Color.HSVToRGB(hue, .72f, 1f);
        Color faint = new Color(strong.r, strong.g, strong.b, .22f);
        Color axis = new Color(strong.r, strong.g, strong.b, .75f);
        float spacing = Mathf.Max(.05f, gridSize);
        int cells = Mathf.Clamp(Mathf.CeilToInt(15f / spacing), 6, 40);
        float extent = cells * spacing;
        float centerX = Snap(cursorPosition.x);
        float centerZ = Snap(cursorPosition.z);
        Vector3 center = new Vector3(centerX, EffectiveLevel(), centerZ);

        Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;
        for (int i = -cells; i <= cells; i++)
        {
            float offset = i * spacing;
            Handles.color = i == 0 ? axis : faint;
            Handles.DrawLine(center + new Vector3(-extent,0,offset), center + new Vector3(extent,0,offset));
            Handles.DrawLine(center + new Vector3(offset,0,-extent), center + new Vector3(offset,0,extent));
        }
        Handles.color = strong;
        Handles.DrawWireCube(center, new Vector3(extent * 2f, .01f, extent * 2f));
        Handles.Label(cursorPosition + Vector3.up * .8f, CurrentFloorLabel(), nameStyle);
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
    }

    void DrawVerticalRuler(SceneView sceneView)
    {
        if (!cursorValid) return;
        float step = Mathf.Max(.05f, levelStep);
        int activeFloor = CurrentFloorIndex();
        int cursorFloor = Mathf.FloorToInt(cursorPosition.y / step);
        int minFloor = Mathf.Min(-1, Mathf.Min(activeFloor, cursorFloor) - 1);
        int maxFloor = Mathf.Max(1, Mathf.Max(activeFloor, cursorFloor) + 1);
        float bottom = minFloor * step;
        float top = maxFloor * step;

        Vector3 right = sceneView && sceneView.camera ? sceneView.camera.transform.right : Vector3.right;
        right.y = 0f;
        if (right.sqrMagnitude < .01f) right = Vector3.right;
        right.Normalize();
        // The ruler passes through the actual placement point, not metres to its side.
        Vector3 ruler = VerticalRulerAnchor(cursorPosition);
        float tickLength = Mathf.Max(.35f, gridSize * .45f);

        Color rulerColor = new Color(.95f,.95f,1f,.8f);
        Color activeColor = new Color(.15f,1f,.65f,1f);
        Color placementColor = new Color(1f,.75f,.1f,1f);
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = rulerColor;
        Handles.DrawAAPolyLine(3f, ruler + Vector3.up * bottom, ruler + Vector3.up * top);

        bool placementHasFloorTick = false;
        for (int floor = minFloor; floor <= maxFloor; floor++)
        {
            float y = floor * step;
            bool active = floor == activeFloor;
            bool placement = Mathf.Abs(y - cursorPosition.y) < .0001f;
            placementHasFloorTick |= placement;
            Handles.color = placement ? placementColor : active ? activeColor : rulerColor;
            float length = placement ? tickLength * 1.55f : active ? tickLength * 1.35f : tickLength;
            Vector3 tick = ruler + Vector3.up * y;
            Handles.DrawAAPolyLine(active || placement ? 5f : 2f, tick - right * length, tick + right * length);
            string floorName = floor == 0 ? "GROUND" : floor > 0 ? "FLOOR +" + floor : "BASEMENT " + floor;
            if (!placement)
                Handles.Label(tick + right * (length + .12f), floorName + "   " + y.ToString("0.###") + " m", nameStyle);
        }

        if (!placementHasFloorTick)
        {
            Vector3 placement = cursorPosition;
            Handles.color = placementColor;
            Handles.DrawAAPolyLine(5f, placement - right * tickLength * 1.55f, placement + right * tickLength * 1.55f);
        }

        Handles.color = activeColor;
        Handles.DrawAAPolyLine(2f, ruler, cursorPosition);
    }

    static Vector3 VerticalRulerAnchor(Vector3 placementPoint)
    {
        return new Vector3(placementPoint.x, 0f, placementPoint.z);
    }

    void PlaceSelected(bool updateIsolation = true)
    {
        if (!cursorValid || selectedIndex < 0 || selectedIndex >= shownPrefabs.Count) return;
        GameObject prefab = shownPrefabs[selectedIndex];
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (!instance) return;
        Undo.RegisterCreatedObjectUndo(instance, "Place House Piece");
        int targetFloor = surfaceFloor ? surfaceFloor.FloorIndex : CurrentFloorIndex();
        float targetBase = surfaceFloor ? surfaceFloor.BaseHeight : level;
        Transform targetParent = GetFloorRoot(targetFloor, true).transform;
        if (UsesPropPlacement)
        {
            string containerName = IsEnvironmentTab ? "Environment" : "Props";
            Transform propsRoot = targetParent.Find(containerName);
            if (!propsRoot)
            {
                GameObject container = new GameObject(containerName);
                Undo.RegisterCreatedObjectUndo(container, "Create Floor " + containerName + " Group");
                container.transform.SetParent(targetParent, false);
                propsRoot = container.transform;
            }
            targetParent = propsRoot;
        }
        instance.transform.SetParent(targetParent, true);
        instance.transform.SetPositionAndRotation(cursorPosition, PlacementRotation());
        instance.transform.localScale = prefab.transform.localScale;
        ApplyPlacementAnchor(instance);
        ApplyFloorMarker(instance, targetFloor, targetBase, cursorPosition.y - targetBase);
        if (updateIsolation && floorIsolationActive) ApplyFloorIsolation();
        Selection.activeGameObject = instance;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    void EraseAt(Vector2 mousePosition)
    {
        GameObject picked = HandleUtility.PickGameObject(mousePosition, false);
        if (!picked) return;
        GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(picked);
        if (!instanceRoot) return;
        GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(instanceRoot);
        if (!source) source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
        if (source && IsBuilderAssetPath(AssetDatabase.GetAssetPath(source)))
        {
            Transform previousParent = instanceRoot.transform.parent;
            Undo.DestroyObjectImmediate(instanceRoot);
            DeleteFloorGroupIfEmpty(previousParent);
        }
    }

    void PickPrefabAt(Vector2 mousePosition)
    {
        GameObject picked = HandleUtility.PickGameObject(mousePosition, false);
        if (!picked) return;
        GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(picked);
        if (!instanceRoot) return;
        GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(instanceRoot);
        if (!source) source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
        if (!source) return;
        string path = AssetDatabase.GetAssetPath(source);
        if (!IsBuilderAssetPath(path))
        {
            ShowNotification(new GUIContent("That object is not from a supported building, prop or environment folder"));
            return;
        }
        if (roomTab)
        {
            AssignRoomSlot(source);
            emptyCursorMode = false;
            pickerMode = false;
            Repaint();
            SceneView.RepaintAll();
            return;
        }
        categoryIndex = 0;
        search = "";
        EnableAssetSetForPath(path);
        ApplyFilter();
        int index = shownPrefabs.IndexOf(source);
        if (index < 0) index = shownPrefabs.FindIndex(p => AssetDatabase.GetAssetPath(p) == path);
        if (index >= 0)
        {
            SelectPrefab(index);
            pickerMode = false;
            ShowNotification(new GUIContent("Picked: " + CleanName(source.name)));
        }
    }

    void CyclePrefab(int direction)
    {
        if (shownPrefabs.Count == 0) return;
        int next = selectedIndex < 0 ? 0 : (selectedIndex + direction + shownPrefabs.Count) % shownPrefabs.Count;
        SelectPrefab(next);
    }

    void CycleSelectedObject(int direction)
    {
        if (!TryGetSelectedHousePiece(out GameObject instanceRoot, out GameObject source)) return;
        int index = shownPrefabs.IndexOf(source);
        if (index < 0)
        {
            categoryIndex = 0;
            search = "";
            EnableAssetSetForPath(AssetDatabase.GetAssetPath(source));
            ApplyFilter();
            index = shownPrefabs.FindIndex(p => AssetDatabase.GetAssetPath(p) == AssetDatabase.GetAssetPath(source));
        }
        if (index < 0) return;
        SelectPrefab(index);
        CyclePrefab(direction);
        Selection.activeGameObject = instanceRoot;
        ReplaceSelected();
        ShowNotification(new GUIContent("Swapped to: " + CleanName(shownPrefabs[selectedIndex].name)));
    }

    void RotateSelectedObjectAroundCenter(float degrees)
    {
        if (!TryGetSelectedHousePiece(out GameObject instanceRoot, out GameObject source)) return;
        Renderer[] renderers = instanceRoot.GetComponentsInChildren<Renderer>(true);
        Vector3 center = instanceRoot.transform.position;
        if (renderers.Length > 0)
        {
            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
            center = bounds.center;
        }
        Undo.RecordObject(instanceRoot.transform, "Rotate House Piece Around Center");
        instanceRoot.transform.RotateAround(center, Vector3.up, degrees);
        EditorUtility.SetDirty(instanceRoot.transform);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    bool TryGetSelectedHousePiece(out GameObject instanceRoot, out GameObject source)
    {
        instanceRoot = null;
        source = null;
        if (!Selection.activeGameObject) return false;
        instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(Selection.activeGameObject);
        if (!instanceRoot) return false;
        source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(instanceRoot);
        if (!source) source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
        if (!source) return false;
        string path = AssetDatabase.GetAssetPath(source);
        return IsBuilderAssetPath(path);
    }

    void ReplaceSelected()
    {
        if (selectedIndex < 0 || selectedIndex >= shownPrefabs.Count) return;
        GameObject[] roots = SelectedReplacementRoots();
        if (roots.Length == 0)
        {
            ShowNotification(new GUIContent("Select placed building, prop or environment prefabs"));
            return;
        }

        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        var replacements = new List<UnityEngine.Object>();
        try
        {
            foreach (GameObject root in roots)
            {
                if (!root) continue;
                GameObject replacement = ReplaceOnePiece(root);
                if (replacement) replacements.Add(replacement);
            }
        }
        finally
        {
            Undo.SetCurrentGroupName("Replace Selected Building Pieces");
            Undo.CollapseUndoOperations(group);
        }
        Selection.objects = replacements.ToArray();
        ShowNotification(new GUIContent(replacements.Count + " objects replaced"));
    }

    GameObject[] SelectedReplacementRoots()
    {
        var roots = new HashSet<GameObject>();
        foreach (GameObject selected in Selection.gameObjects)
        {
            if (!selected || EditorUtility.IsPersistent(selected) || !selected.scene.IsValid()) continue;
            GameObject root = PrefabUtility.GetNearestPrefabInstanceRoot(selected);
            if (!root) continue;
            GameObject source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(root);
            if (!source) source = PrefabUtility.GetCorrespondingObjectFromSource(root);
            if (source && IsBuilderAssetPath(AssetDatabase.GetAssetPath(source))) roots.Add(root);
        }
        // A selected child resolves to its module once; selected nested modules
        // are covered by a selected ancestor and must not be replaced twice.
        return roots.Where(root => !roots.Any(other => other != root && root.transform.IsChildOf(other.transform))).ToArray();
    }

    GameObject ReplaceOnePiece(GameObject oldRoot)
    {

        Transform oldTransform = oldRoot.transform;
        Transform parent = oldTransform.parent;
        int sibling = oldTransform.GetSiblingIndex();
        Vector3 localPosition = oldTransform.localPosition;
        Quaternion localRotation = oldTransform.localRotation;
        Vector3 localScale = oldTransform.localScale;
        HouseBuilderFloorMarker oldMarker = oldRoot.GetComponent<HouseBuilderFloorMarker>();
        int taggedFloor = oldMarker ? oldMarker.FloorIndex : CurrentFloorIndex();
        float taggedBase = oldMarker ? oldMarker.BaseHeight : level;
        float taggedOffset = oldMarker ? oldMarker.PlacementOffset : floorOffset;
        HouseBuilderPieceKind taggedKind = oldMarker ? oldMarker.PieceKind : HouseBuilderPieceKind.Unspecified;
        var scene = oldRoot.scene;
        GameObject replacement = PrefabUtility.InstantiatePrefab(shownPrefabs[selectedIndex], scene) as GameObject;
        if (!replacement) return null;
        Undo.RegisterCreatedObjectUndo(replacement, "Replace House Piece");
        replacement.transform.SetParent(parent, false);
        replacement.transform.SetSiblingIndex(sibling);
        replacement.transform.localPosition = localPosition;
        replacement.transform.localRotation = localRotation;
        replacement.transform.localScale = localScale;
        ApplyFloorMarker(replacement, taggedFloor, taggedBase, taggedOffset);
        replacement.GetComponent<HouseBuilderFloorMarker>().PieceKind = taggedKind;
        Undo.DestroyObjectImmediate(oldRoot);
        EditorSceneManager.MarkSceneDirty(scene);
        return replacement;
    }

    static void AnchorBottomAtHeight(GameObject root, float targetHeight)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true)
            .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        if (renderers.Length == 0) return;
        float lowest = renderers[0].bounds.min.y;
        for (int i = 1; i < renderers.Length; i++) lowest = Mathf.Min(lowest, renderers[i].bounds.min.y);
        root.transform.position += Vector3.up * (targetHeight - lowest);
    }

    void ChoosePrefabFromUI(int index)
    {
        if (roomTab)
        {
            AssignRoomSlot(shownPrefabs[index]);
            emptyCursorMode = false;
            SelectPrefab(index);
            return;
        }
        bool hasReplaceableSelection = !buildMode && SelectedReplacementRoots().Length > 0;
        emptyCursorMode = false;
        SelectPrefab(index);
        if (hasReplaceableSelection)
        {
            ReplaceSelected();
            ShowNotification(new GUIContent("Replaced with: " + CleanName(shownPrefabs[selectedIndex].name)));
        }
    }

    Quaternion PlacementRotation()
    {
        if (WallPlacement)
        {
            Vector3[] axes = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
            Quaternion facing = Quaternion.LookRotation(cursorNormal, Vector3.up)
                * Quaternion.Inverse(Quaternion.LookRotation(axes[Mathf.Clamp(propWallFacing, 0, 3)], Vector3.up));
            return Quaternion.AngleAxis(yaw, cursorNormal) * facing;
        }
        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        if (!AlignPlacementToSurface || cursorNormal.sqrMagnitude < .5f) return yawRotation;
        return Quaternion.FromToRotation(Vector3.up, cursorNormal) * yawRotation;
    }

    void RefreshAssets()
    {
        surfaceMeshCache.Clear();
        surfaceMeshTargetsDirty = true;
        allPrefabs.Clear();
        string[] guids = AssetDatabase.FindAssets("t:Prefab", AssetFolders);
        foreach (string guid in guids)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab) allPrefabs.Add(prefab);
        }
        allPrefabs.Sort((a,b) => string.Compare(a.name,b.name,StringComparison.OrdinalIgnoreCase));
        // The kit has more prefabs than Unity's normal preview cache. Without this,
        // requested mesh thumbnails are evicted and the palette stays on cube icons.
        AssetPreview.SetPreviewTextureCacheSize(Mathf.Max(512, allPrefabs.Count + 64));
        ApplyFilter();
    }

    void RepaintWhilePreviewsLoad()
    {
        for (int i = 0; i < shownPrefabs.Count; i++)
        {
            GameObject prefab = shownPrefabs[i];
            if (prefab && AssetPreview.IsLoadingAssetPreview(prefab.GetEntityId()))
            {
                Repaint();
                return;
            }
        }
    }

    void ApplyFilter()
    {
        GameObject previous = selectedIndex >= 0 && selectedIndex < shownPrefabs.Count ? shownPrefabs[selectedIndex] : null;
        string[] activeCategories = ActiveCategories;
        string wanted = activeCategories[Mathf.Clamp(categoryIndex,0,activeCategories.Length-1)];
        shownPrefabs.Clear();
        shownPrefabs.AddRange(allPrefabs.Where(p => IsAssetSetVisible(AssetDatabase.GetAssetPath(p)) &&
            (IsEnvironmentTab ? MatchesEnvironmentCategory(p.name, wanted) : propsTab ? MatchesPropCategory(p, wanted) : MatchesCategory(p.name,wanted)) &&
            (string.IsNullOrWhiteSpace(search) || p.name.IndexOf(search,StringComparison.OrdinalIgnoreCase) >= 0)));
        selectedIndex = previous ? shownPrefabs.IndexOf(previous) : -1;
        if (selectedIndex < 0 && shownPrefabs.Count > 0) SelectPrefab(0);
        if (shownPrefabs.Count == 0) DestroyPreview();
        Repaint();
    }

    static bool MatchesCategory(string name, string category)
    {
        if (category == "All") return true;
        string n = name.ToLowerInvariant();
        switch (category)
        {
            case "Base": return n.Contains("_base_");
            case "Floor": return n.Contains("_floor_") || n.Contains("flooring");
            case "Wall": return n.Contains("_wall_") && !n.Contains("door") && !n.Contains("window");
            case "Door & Window": return n.Contains("door") || n.Contains("window") || n.Contains("glass");
            case "Roof": return n.Contains("_roof_");
            case "Tower": return n.Contains("_tower_") || n.Contains("observatory");
            case "Stairs": return n.Contains("stair") || n.Contains("stonearch") || n.Contains("woodarch");
            case "Chimney": return n.Contains("chimney") || n.Contains("fireplace");
            case "Railing": return n.Contains("railing");
            case "Beam & Support": return n.Contains("beam") || n.Contains("support");
            case "Battlements": return n.Contains("battlement") || n.Contains("hoarding");
            case "Pillar": return n.Contains("pillar") || n.Contains("column");
            case "Gate & Bridge": return n.Contains("gate") || n.Contains("bridge") || n.Contains("drawbridge");
            case "Destroyed": return n.Contains("destroyed") || n.Contains("rubble");
            case "Other": return !Categories.Skip(1).Take(Categories.Length-2).Any(c => MatchesCategory(name,c));
            default: return true;
        }
    }

    static bool MatchesEnvironmentCategory(string name, string category)
    {
        if (category == "All") return true;
        string n = name.ToLowerInvariant();
        switch (category)
        {
            case "Trees": return n.Contains("tree") || n.Contains("stump");
            case "Plants": return new[] { "flower", "bush", "fern", "grass", "hedge", "ivy", "leaves", "lily", "reeds" }.Any(n.Contains);
            case "Rocks": return n.Contains("rock") || n.Contains("mountain");
            case "Ground": return n.Contains("ground") || n.Contains("riverbed");
            case "Paths & Structures": return new[] { "path", "bridge", "stonewall", "structure", "planter" }.Any(n.Contains);
            case "Path Bricks": return n.Contains("_path_brick_");
            case "Water": return n.Contains("water") || n.Contains("river") || n.Contains("fountain");
            case "Sky": return n.Contains("cloud");
            case "Other": return !EnvironmentCategories.Skip(1).Take(EnvironmentCategories.Length - 2).Any(c => MatchesEnvironmentCategory(name, c));
            default: return false;
        }
    }

    static bool MatchesPropCategory(GameObject prefab, string category)
    {
        if (category == "All") return true;
        string n = prefab.name.ToLowerInvariant();
        string path = AssetDatabase.GetAssetPath(prefab);
        bool furniture = path.Contains("/Furniture/") || new[] { "shelf", "workbench", "table", "chair", "bed", "wardrobe" }.Any(n.Contains);
        bool containers = new[] { "barrel", "crate", "basket", "sack", "chest", "bucket" }.Any(n.Contains);
        bool lighting = new[] { "torch", "candle", "lamp", "lantern", "chandelier", "brazier" }.Any(n.Contains);
        bool tabletop = path.StartsWith(ItemsFolder + "/", StringComparison.OrdinalIgnoreCase);
        bool decorations = new[] { "banner", "flag", "painting", "rug", "curtain", "sign", "statue", "vase", "book" }.Any(n.Contains);
        switch (category)
        {
            case "Furniture": return furniture;
            case "Containers": return containers;
            case "Lighting": return lighting;
            case "Tabletop": return tabletop;
            case "Decorations": return decorations;
            default: return !furniture && !containers && !lighting && !tabletop && !decorations;
        }
    }

    void SelectPrefab(int index)
    {
        selectedIndex = Mathf.Clamp(index,0,shownPrefabs.Count-1);
        selectedBounds = CalculateBounds(shownPrefabs[selectedIndex]);
        RebuildPreview();
        Repaint();
        SceneView.RepaintAll();
    }

    void UpdateMeshPreview(bool hidden)
    {
        if (hidden || !cursorValid)
        {
            if (previewInstance) previewInstance.SetActive(false);
            return;
        }
        if (selectedIndex < 0 || selectedIndex >= shownPrefabs.Count) { DestroyPreview(); return; }
        GameObject source = shownPrefabs[selectedIndex];
        if (!previewInstance || previewSource != source) RebuildPreview();
        if (!previewInstance) return;
        previewInstance.SetActive(true);
        previewInstance.transform.SetPositionAndRotation(cursorPosition, PlacementRotation());
        previewInstance.transform.localScale = source.transform.localScale;
        ApplyPlacementAnchor(previewInstance);
    }

    void ApplyPlacementAnchor(GameObject instance)
    {
        if (WallPlacement)
        {
            // Use the prefab bounds in its local frame, transformed into the wall
            // orientation. Align its backmost point and center to the cursor plane.
            Bounds bounds = selectedBounds;
            float nearest = float.PositiveInfinity;
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                    {
                        Vector3 corner = instance.transform.TransformPoint(bounds.center +
                            Vector3.Scale(bounds.extents, new Vector3(x,y,z)));
                        nearest = Mathf.Min(nearest, Vector3.Dot(corner, cursorNormal));
                    }
            Vector3 center = instance.transform.TransformPoint(bounds.center);
            instance.transform.position += Vector3.ProjectOnPlane(cursorPosition - center, cursorNormal)
                + cursorNormal * (Vector3.Dot(cursorPosition, cursorNormal) - nearest);
        }
        else if (UsesPropPlacement || bottomAnchoredPlacement) AnchorBottomAtHeight(instance, cursorPosition.y);
    }

    void RebuildPreview()
    {
        DestroyPreview();
        if (!buildMode || selectedIndex < 0 || selectedIndex >= shownPrefabs.Count) return;
        previewSource = shownPrefabs[selectedIndex];
        previewInstance = Instantiate(previewSource);
        previewInstance.name = "[House Builder Preview] " + previewSource.name;
        SetPreviewFlags(previewInstance.transform);
        foreach (Collider collider in previewInstance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        foreach (Behaviour behaviour in previewInstance.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
    }

    static void SetPreviewFlags(Transform root)
    {
        root.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSaveInEditor | HideFlags.NotEditable;
        foreach (Transform child in root) SetPreviewFlags(child);
    }

    void DestroyPreview()
    {
        if (previewInstance) DestroyImmediate(previewInstance);
        previewInstance = null;
        previewSource = null;
    }

    static Bounds CalculateBounds(GameObject prefab)
    {
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true)
            .Where(r => r is MeshRenderer || r is SkinnedMeshRenderer).ToArray();
        if (renderers.Length == 0) return new Bounds(Vector3.zero,Vector3.one);
        Matrix4x4 rootInv = prefab.transform.worldToLocalMatrix;
        bool initialized = false;
        Bounds result = new Bounds();
        foreach (Renderer renderer in renderers)
        {
            Bounds b = renderer.localBounds;
            Matrix4x4 m = rootInv * renderer.transform.localToWorldMatrix;
            for (int x=-1;x<=1;x+=2) for (int y=-1;y<=1;y+=2) for (int z=-1;z<=1;z+=2)
            {
                Vector3 corner = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents,new Vector3(x,y,z)));
                if (!initialized) { result = new Bounds(corner,Vector3.zero); initialized=true; } else result.Encapsulate(corner);
            }
        }
        return initialized ? result : new Bounds(Vector3.zero,Vector3.one);
    }

    GameObject GetBuildRoot(bool create)
    {
        // Keep placement and isolation inside the active scene when scenes are loaded additively.
        GameObject root = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(go => go.name == RootName);
        if (!root && create)
        {
            root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root,"Create House Build Root");
        }
        return root;
    }

    GameObject GetFloorRoot(int floorIndex, bool create)
    {
        GameObject buildRoot = GetBuildRoot(create);
        if (!buildRoot) return null;
        string wantedName = FloorGroupName(floorIndex);
        Transform existing = buildRoot.transform.Cast<Transform>().FirstOrDefault(t => t.name == wantedName);
        if (existing) return existing.gameObject;
        if (!create) return null;
        GameObject floorRoot = new GameObject(wantedName);
        Undo.RegisterCreatedObjectUndo(floorRoot, "Create Floor Group");
        floorRoot.transform.SetParent(buildRoot.transform, false);
        return floorRoot;
    }

    static string FloorGroupName(int floorIndex)
    {
        return floorIndex == 0 ? "FLOOR 0 — GROUND" : floorIndex > 0 ? "FLOOR +" + floorIndex : "FLOOR " + floorIndex + " — BASEMENT";
    }

    static void ApplyFloorMarker(GameObject target, int floorIndex, float baseHeight, float offset)
    {
        HouseBuilderFloorMarker marker = target.GetComponent<HouseBuilderFloorMarker>();
        if (!marker) marker = Undo.AddComponent<HouseBuilderFloorMarker>(target);
        Undo.RecordObject(marker, "Set House Piece Floor");
        marker.FloorIndex = floorIndex;
        marker.BaseHeight = baseHeight;
        marker.PlacementOffset = offset;
        EditorUtility.SetDirty(marker);
    }

    void TagSelectedToCurrentFloor()
    {
        if (!TryGetSelectedHousePiece(out GameObject instanceRoot, out GameObject source))
        {
            ShowNotification(new GUIContent("Select a supported building, prop or environment piece"));
            return;
        }
        Transform previousParent = instanceRoot.transform.parent;
        GameObject floorRoot = GetFloorRoot(CurrentFloorIndex(), true);
        Undo.SetTransformParent(instanceRoot.transform, floorRoot.transform, "Move Piece to Current Floor");
        ApplyFloorMarker(instanceRoot, CurrentFloorIndex(), level, floorOffset);
        DeleteFloorGroupIfEmpty(previousParent);
        Selection.activeGameObject = instanceRoot;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    void IsolateCurrentFloor()
    {
        floorIsolationActive = true;
        ApplyFloorIsolation();
        ShowNotification(new GUIContent("Isolated " + FloorGroupName(CurrentFloorIndex())));
    }

    void ApplyFloorIsolation()
    {
        GameObject buildRoot = GetBuildRoot(false);
        if (!buildRoot) return;
        int activeFloor = CurrentFloorIndex();
        SceneVisibilityManager visibility = SceneVisibilityManager.instance;
        visibility.Show(buildRoot, false);
        foreach (Transform child in buildRoot.transform)
        {
            bool show = child.name == FloorGroupName(activeFloor);
            HouseBuilderFloorMarker marker = child.GetComponent<HouseBuilderFloorMarker>();
            if (marker) show = marker.FloorIndex == activeFloor;
            if (show) visibility.Show(child.gameObject, true);
            else visibility.Hide(child.gameObject, true);
        }
        SceneView.RepaintAll();
    }

    void ShowAllFloors()
    {
        floorIsolationActive = false;
        GameObject buildRoot = GetBuildRoot(false);
        if (!buildRoot) return;
        SceneVisibilityManager.instance.Show(buildRoot, true);
        SceneView.RepaintAll();
        ShowNotification(new GUIContent("All floors visible"));
    }

    static void DeleteFloorGroupIfEmpty(Transform candidate)
    {
        if (!candidate || candidate.childCount != 0) return;
        if ((candidate.name == "Props" || candidate.name == "Environment") && candidate.parent && candidate.parent.parent && candidate.parent.parent.name == RootName)
        {
            Transform floorParent = candidate.parent;
            Undo.DestroyObjectImmediate(candidate.gameObject);
            DeleteFloorGroupIfEmpty(floorParent);
            return;
        }
        if (!candidate.name.StartsWith("FLOOR ", StringComparison.Ordinal)) return;
        Transform parent = candidate.parent;
        if (!parent || parent.name != RootName) return;
        Undo.DestroyObjectImmediate(candidate.gameObject);
    }

    void ScheduleEmptyFloorCleanup()
    {
        surfaceMeshTargetsDirty = true;
        if (floorCleanupScheduled) return;
        floorCleanupScheduled = true;
        EditorApplication.delayCall += CleanupEmptyFloorGroups;
    }

    void CleanupEmptyFloorGroups()
    {
        EditorApplication.delayCall -= CleanupEmptyFloorGroups;
        floorCleanupScheduled = false;
        GameObject buildRoot = GetBuildRoot(false);
        if (!buildRoot) return;
        foreach (Transform floor in buildRoot.transform.Cast<Transform>().ToArray())
        {
            foreach (string containerName in new[] { "Props", "Environment" })
            {
                Transform container = floor.Find(containerName);
                if (container && container.childCount == 0) DeleteFloorGroupIfEmpty(container);
                if (!floor) break;
            }
        }
        List<GameObject> emptyGroups = buildRoot.transform.Cast<Transform>()
            .Where(t => t.childCount == 0 && t.name.StartsWith("FLOOR ", StringComparison.Ordinal))
            .Select(t => t.gameObject).ToList();
        if (emptyGroups.Count == 0) return;
        int undoGroup = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Remove Empty Floor Group");
        foreach (GameObject emptyGroup in emptyGroups) Undo.DestroyObjectImmediate(emptyGroup);
        Undo.CollapseUndoOperations(undoGroup);
    }

    void FrameBuild()
    {
        GameObject root = GetBuildRoot(false);
        if (!root) return;
        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    void ChangeLevel(int direction)
    {
        level = Mathf.Round((level + direction * levelStep) * 1000f) / 1000f;
        if (floorIsolationActive) ApplyFloorIsolation();
        Repaint(); SceneView.RepaintAll();
    }

    void SyncUnityToolForBuildMode()
    {
        if (!buildMode)
        {
            RestoreUnityToolIfNeeded();
            return;
        }

        if (!builderOwnsUnityTool)
        {
            unityToolBeforeBuildMode = Tools.current;
            builderOwnsUnityTool = true;
        }

        // Build Mode owns Scene-view shortcuts, so Unity's transform tools must
        // not intercept them while the builder is active.
        if (Tools.current != Tool.None) Tools.current = Tool.None;
    }

    void RestoreUnityToolIfNeeded()
    {
        if (!builderOwnsUnityTool) return;
        if (Tools.current == Tool.None) Tools.current = unityToolBeforeBuildMode;
        builderOwnsUnityTool = false;
    }

    void RotateSelection(float degrees)
    {
        if (UsesPropPlacement) degrees = Mathf.Sign(degrees) * propRotationStep;
        yaw = Mathf.Repeat(yaw + degrees,360f);
        Repaint(); SceneView.RepaintAll();
    }

    float Snap(float value) { return Mathf.Round(value / gridSize) * gridSize; }
    float EffectiveLevel() { return level + floorOffset; }
    int CurrentFloorIndex() { return Mathf.RoundToInt(level / Mathf.Max(.05f, levelStep)); }
    string CurrentFloorLabel()
    {
        int floor = CurrentFloorIndex();
        string name = floor == 0 ? "GROUND FLOOR" : floor > 0 ? "FLOOR +" + floor : "BASEMENT " + floor;
        string offset = Mathf.Abs(floorOffset) < .0001f ? "" : "   •   Offset " + floorOffset.ToString("+0.###;-0.###") + " m";
        return name + "   •   Y = " + EffectiveLevel().ToString("0.###") + " m" + offset;
    }
    static bool IsBuilderAssetPath(string path)
    {
        return AssetFolders.Any(folder => path.StartsWith(folder + "/", StringComparison.OrdinalIgnoreCase));
    }

    bool IsAssetSetVisible(string path)
    {
        // Path bricks are shared between Props and Environment, not moved out of Props.
        if (IsPathBrickPath(path)) return propsTab || (IsEnvironmentTab && showEnvironmentAssets);
        if (IsEnvironmentPath(path))
            return IsEnvironmentTab && (path.StartsWith(EnvironmentFolder + "/", StringComparison.OrdinalIgnoreCase) ? showEnvironmentAssets : showGenericAssets);
        if (IsEnvironmentTab) return false;
        if (IsPropPath(path)) return propsTab;
        if (propsTab) return false;
        if (path.StartsWith(HouseFolder + "/", StringComparison.OrdinalIgnoreCase)) return showHouseAssets;
        if (path.StartsWith(CastleFolder + "/", StringComparison.OrdinalIgnoreCase)) return showCastleAssets;
        return false;
    }

    void EnableAssetSetForPath(string path)
    {
        roomTab = false;
        propsTab = IsPropPath(path) && !IsEnvironmentPath(path);
        environmentTab = IsEnvironmentPath(path);
        if (path.StartsWith(HouseFolder + "/", StringComparison.OrdinalIgnoreCase)) showHouseAssets = true;
        if (path.StartsWith(CastleFolder + "/", StringComparison.OrdinalIgnoreCase)) showCastleAssets = true;
        if (path.StartsWith(EnvironmentFolder + "/", StringComparison.OrdinalIgnoreCase) || IsPathBrickPath(path)) showEnvironmentAssets = true;
        if (path.StartsWith(GenericFolder + "/", StringComparison.OrdinalIgnoreCase)) showGenericAssets = true;
    }

    static string CleanName(string value)
    {
        return value.Replace("SM_Bld_House_","").Replace("SM_Bld_Castle_","").Replace("SM_Bld_","").Replace("SM_Prop_", "").Replace("SM_Item_", "").Replace("SM_Env_", "").Replace("SM_Generic_", "").Replace('_',' ');
    }

    static bool IsEnvironmentPath(string path)
    {
        return path.StartsWith(EnvironmentFolder + "/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(GenericFolder + "/", StringComparison.OrdinalIgnoreCase) || IsPathBrickPath(path);
    }

    static bool IsPathBrickPath(string path)
    {
        return path.StartsWith(PropPathsFolder + "/SM_Prop_Path_Brick_", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsPropPath(string path)
    {
        return path.StartsWith(PropsFolder + "/", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith(ItemsFolder + "/", StringComparison.OrdinalIgnoreCase);
    }

    void EnsureStyles()
    {
        if (tabButtonStyle == null)
        {
            tabButtonStyle = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
            tabButtonStyle.padding = new RectOffset(6, 4, tabButtonStyle.padding.top, tabButtonStyle.padding.bottom);
        }
        if (tabLabelStyle == null)
        {
            tabLabelStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 4, 0, 0),
                clipping = TextClipping.Clip,
                wordWrap = false
            };
        }
        if (selectedButton == null)
        {
            selectedButton = new GUIStyle(GUI.skin.button);
            selectedButton.normal.background = selectedButton.active.background;
            selectedButton.normal.textColor = Color.white;
        }
        if (nameStyle == null)
        {
            nameStyle = new GUIStyle(EditorStyles.helpBox) { normal = { textColor = Color.white }, fontStyle = FontStyle.Bold };
        }
    }
}
#endif
