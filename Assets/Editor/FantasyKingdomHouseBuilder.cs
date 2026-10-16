#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class FantasyKingdomHouseBuilder : EditorWindow
{
    // Modular placement editor for POLYGON Fantasy Kingdom house and castle assets.
    const string HouseFolder = "Assets/PolygonFantasyKingdom/Prefabs/House";
    const string CastleFolder = "Assets/PolygonFantasyKingdom/Prefabs/Castle";
    static readonly string[] AssetFolders = { HouseFolder, CastleFolder };
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
    readonly List<GameObject> shownPrefabs = new List<GameObject>();
    Vector2 scroll;
    string search = "";
    int categoryIndex;
    int selectedIndex = -1;
    float gridSize = KitHalfModule;
    float level;
    float levelStep = KitStoreyHeight;
    float floorOffset;
    float yaw;
    bool buildMode = true;
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
    GameObject previewInstance;
    GameObject previewSource;
    bool floorCleanupScheduled;

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
        SceneView.duringSceneGui += DuringSceneGUI;
        EditorApplication.update += RepaintWhilePreviewsLoad;
        EditorApplication.hierarchyChanged += ScheduleEmptyFloorCleanup;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= DuringSceneGUI;
        EditorApplication.update -= RepaintWhilePreviewsLoad;
        EditorApplication.hierarchyChanged -= ScheduleEmptyFloorCleanup;
        EditorApplication.delayCall -= CleanupEmptyFloorGroups;
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
        EnsureStyles();
        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("POLYGON Fantasy Kingdom — House & Castle Builder", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox("P: SAMPLE UNDER CURSOR    U: TOGGLE EDITOR\nE: DELETE (BUILD ON) / UNITY ROTATE (BUILD OFF)\nA/S: ROTATE    Z/X: FLOORS    Q/W/R/T: UNITY TOOLS", MessageType.Info);

        using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
        {
            buildMode = GUILayout.Toggle(buildMode, buildMode ? "BUILD MODE ON" : "BUILD MODE OFF", EditorStyles.toolbarButton, GUILayout.Width(120));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(58))) RefreshAssets();
        }

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Measured kit module: 2.5 m full / 1.25 m half", EditorStyles.miniLabel);
            EditorGUILayout.HelpBox(CurrentFloorLabel(), MessageType.None);
            gridSize = Mathf.Max(.05f, EditorGUILayout.FloatField(new GUIContent("Grid Size", "XZ snap spacing"), gridSize));
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
                if (GUILayout.Button("↻ 90°", GUILayout.Width(62))) RotateSelection(90f);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("◀ Previous  [")) CyclePrefab(-1);
                if (GUILayout.Button("Next  ] ▶")) CyclePrefab(1);
            }
            if (GUILayout.Button("Replace Selected With Active Piece")) ReplaceSelected();
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

        EditorGUILayout.Space(3);
        search = EditorGUILayout.TextField("Search", search);
        using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
        {
            showHouseAssets = EditorGUILayout.ToggleLeft("House Assets", showHouseAssets, GUILayout.MinWidth(110f));
            showCastleAssets = EditorGUILayout.ToggleLeft("Castle Assets", showCastleAssets, GUILayout.MinWidth(110f));
        }
        int nextCategory = GUILayout.SelectionGrid(categoryIndex, Categories, 3);
        if (nextCategory != categoryIndex) { categoryIndex = nextCategory; ApplyFilter(); }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField(shownPrefabs.Count + " building pieces", EditorStyles.miniBoldLabel);
        scroll = EditorGUILayout.BeginScrollView(scroll);
        DrawPalette();
        EditorGUILayout.EndScrollView();

        if (GUI.changed)
        {
            ApplyFilter();
            SceneView.RepaintAll();
        }
    }

    void DrawPalette()
    {
        const float tileWidth = 96f;
        int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 28f) / tileWidth));
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

    void DuringSceneGUI(SceneView sceneView)
    {
        Event e = Event.current;
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
        if (shownPrefabs.Count == 0) { DestroyPreview(); return; }
        bool temporaryErase = e.shift;
        bool erasing = eraseMode || temporaryErase;

        UpdateCursor(e.mousePosition);
        UpdateMeshPreview(erasing || pickerMode);
        if (showFloorGuide)
        {
            DrawFloorGuide();
            DrawVerticalRuler(sceneView);
        }
        DrawCursor(erasing, pickerMode);

        if (e.type == EventType.KeyDown)
        {
            bool plainShortcut = !e.control && !e.command && !e.alt;
            if (plainShortcut && e.keyCode == KeyCode.P)
            {
                PickPrefabAt(e.mousePosition); e.Use();
                return;
            }
            if (plainShortcut && e.keyCode == KeyCode.E)
            {
                EraseAt(e.mousePosition); e.Use();
                return;
            }
            if (plainShortcut && e.keyCode == KeyCode.U)
            {
                buildMode = false;
                pickerMode = false;
                eraseMode = false;
                DestroyPreview();
                Repaint(); SceneView.RepaintAll(); e.Use();
                return;
            }
            if (plainShortcut && (e.keyCode == KeyCode.Q || e.keyCode == KeyCode.W ||
                                  e.keyCode == KeyCode.R || e.keyCode == KeyCode.T))
            {
                buildMode = false;
                pickerMode = false;
                eraseMode = false;
                DestroyPreview();
                Repaint();
                SceneView.RepaintAll();
                // Do not consume the event: Unity should still activate its Q/W/R/T tool.
                return;
            }
            if (plainShortcut && e.keyCode == KeyCode.A) { RotateSelection(-90f); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.S) { RotateSelection(90f); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.Z) { ChangeLevel(-1); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.X) { ChangeLevel(1); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.PageUp) { ChangeLevel(1); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.PageDown) { ChangeLevel(-1); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.LeftBracket) { CyclePrefab(-1); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.RightBracket) { CyclePrefab(1); e.Use(); }
            else if (plainShortcut && e.keyCode == KeyCode.Escape) { buildMode = false; pickerMode = false; eraseMode = false; Repaint(); e.Use(); }
        }

        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
        if (e.type == EventType.MouseDown && e.button == 0 && !e.alt)
        {
            if (pickerMode) PickPrefabAt(e.mousePosition);
            else if (erasing) EraseAt(e.mousePosition);
            else PlaceSelected();
            e.Use();
        }
    }

    void UpdateCursor(Vector2 mousePosition)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(mousePosition);
        cursorNormal = Vector3.up;
        Vector3 point;
        if (Physics.Raycast(ray, out RaycastHit hit, 10000f, ~0, QueryTriggerInteraction.Ignore))
        {
            point = hit.point;
            cursorNormal = hit.normal;
        }
        else
        {
            Plane plane = new Plane(Vector3.up, new Vector3(0f, EffectiveLevel(), 0f));
            point = plane.Raycast(ray, out float distance) ? ray.GetPoint(distance) : Vector3.zero;
        }
        cursorPosition = new Vector3(Snap(point.x), EffectiveLevel(), Snap(point.z));
    }

    void DrawCursor(bool erasing, bool picking)
    {
        Color color = picking ? new Color(.2f,.75f,1f,.95f) : erasing ? new Color(1f,.2f,.15f,.95f) : new Color(.15f,1f,.55f,.95f);
        Handles.color = color;
        float radius = Mathf.Max(.15f, gridSize * .18f);
        Handles.DrawWireDisc(cursorPosition, Vector3.up, radius);
        Handles.DrawLine(cursorPosition - Vector3.right * radius, cursorPosition + Vector3.right * radius);
        Handles.DrawLine(cursorPosition - Vector3.forward * radius, cursorPosition + Vector3.forward * radius);
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
        float step = Mathf.Max(.05f, levelStep);
        int activeFloor = CurrentFloorIndex();
        int minFloor = Mathf.Min(-1, activeFloor - 1);
        int maxFloor = Mathf.Max(1, activeFloor + 1);
        float bottom = minFloor * step;
        float top = maxFloor * step;

        Vector3 right = sceneView && sceneView.camera ? sceneView.camera.transform.right : Vector3.right;
        right.y = 0f;
        if (right.sqrMagnitude < .01f) right = Vector3.right;
        right.Normalize();
        Vector3 ruler = new Vector3(cursorPosition.x, 0f, cursorPosition.z) + right * Mathf.Max(2f, gridSize * 2f);
        float tickLength = Mathf.Max(.35f, gridSize * .45f);

        Color rulerColor = new Color(.95f,.95f,1f,.8f);
        Color activeColor = new Color(.15f,1f,.65f,1f);
        Color placementColor = new Color(1f,.75f,.1f,1f);
        Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
        Handles.color = rulerColor;
        Handles.DrawAAPolyLine(3f, ruler + Vector3.up * bottom, ruler + Vector3.up * top);

        for (int floor = minFloor; floor <= maxFloor; floor++)
        {
            float y = floor * step;
            bool active = floor == activeFloor;
            Handles.color = active ? activeColor : rulerColor;
            float length = active ? tickLength * 1.35f : tickLength;
            Vector3 tick = ruler + Vector3.up * y;
            Handles.DrawAAPolyLine(active ? 5f : 2f, tick - right * length, tick + right * length);
            string floorName = floor == 0 ? "GROUND" : floor > 0 ? "FLOOR +" + floor : "BASEMENT " + floor;
            Handles.Label(tick + right * (length + .12f), floorName + "   " + y.ToString("0.###") + " m", nameStyle);
        }

        if (Mathf.Abs(floorOffset) > .0001f)
        {
            float placementY = EffectiveLevel();
            Vector3 placement = ruler + Vector3.up * placementY;
            Handles.color = placementColor;
            Handles.DrawAAPolyLine(5f, placement - right * tickLength * 1.55f, placement + right * tickLength * 1.55f);
            Handles.Label(placement + right * (tickLength * 1.7f),
                "PLACEMENT   " + placementY.ToString("0.###") + " m   (" + floorOffset.ToString("+0.###;-0.###") + ")", nameStyle);
        }

        Handles.color = activeColor;
        Handles.DrawAAPolyLine(2f, ruler + Vector3.up * (activeFloor * step), cursorPosition);
    }

    void PlaceSelected()
    {
        if (selectedIndex < 0 || selectedIndex >= shownPrefabs.Count) return;
        GameObject prefab = shownPrefabs[selectedIndex];
        GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (!instance) return;
        Undo.RegisterCreatedObjectUndo(instance, "Place House Piece");
        instance.transform.SetParent(GetFloorRoot(CurrentFloorIndex(), true).transform, true);
        instance.transform.SetPositionAndRotation(cursorPosition, PlacementRotation());
        instance.transform.localScale = prefab.transform.localScale;
        if (bottomAnchoredPlacement) AnchorBottomAtHeight(instance, EffectiveLevel());
        ApplyFloorMarker(instance, CurrentFloorIndex(), level, floorOffset);
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
            ShowNotification(new GUIContent("That object is not from the House or Castle kit"));
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
        if (selectedIndex < 0 || selectedIndex >= shownPrefabs.Count || !Selection.activeGameObject) return;
        if (!TryGetSelectedHousePiece(out GameObject oldRoot, out GameObject oldSource))
        {
            ShowNotification(new GUIContent("Select a placed House-kit prefab"));
            return;
        }

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
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Replace House Piece");
        GameObject replacement = PrefabUtility.InstantiatePrefab(shownPrefabs[selectedIndex]) as GameObject;
        if (!replacement) return;
        Undo.RegisterCreatedObjectUndo(replacement, "Replace House Piece");
        replacement.transform.SetParent(parent, false);
        replacement.transform.SetSiblingIndex(sibling);
        replacement.transform.localPosition = localPosition;
        replacement.transform.localRotation = localRotation;
        replacement.transform.localScale = localScale;
        ApplyFloorMarker(replacement, taggedFloor, taggedBase, taggedOffset);
        Undo.DestroyObjectImmediate(oldRoot);
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = replacement;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    static void AnchorBottomAtHeight(GameObject root, float targetHeight)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;
        float lowest = renderers[0].bounds.min.y;
        for (int i = 1; i < renderers.Length; i++) lowest = Mathf.Min(lowest, renderers[i].bounds.min.y);
        root.transform.position += Vector3.up * (targetHeight - lowest);
    }

    void ChoosePrefabFromUI(int index)
    {
        GameObject oldRoot = null;
        GameObject oldSource = null;
        bool hasReplaceableSelection = !buildMode && TryGetSelectedHousePiece(out oldRoot, out oldSource);
        SelectPrefab(index);
        if (hasReplaceableSelection)
        {
            Selection.activeGameObject = oldRoot;
            ReplaceSelected();
            ShowNotification(new GUIContent("Replaced with: " + CleanName(shownPrefabs[selectedIndex].name)));
        }
    }

    Quaternion PlacementRotation()
    {
        Quaternion yawRotation = Quaternion.Euler(0f, yaw, 0f);
        if (!alignToSurface || cursorNormal.sqrMagnitude < .5f) return yawRotation;
        return Quaternion.FromToRotation(Vector3.up, cursorNormal) * yawRotation;
    }

    void RefreshAssets()
    {
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
        string wanted = Categories[Mathf.Clamp(categoryIndex,0,Categories.Length-1)];
        shownPrefabs.Clear();
        shownPrefabs.AddRange(allPrefabs.Where(p => IsAssetSetVisible(AssetDatabase.GetAssetPath(p)) && MatchesCategory(p.name,wanted) &&
            (string.IsNullOrWhiteSpace(search) || p.name.IndexOf(search,StringComparison.OrdinalIgnoreCase) >= 0)));
        selectedIndex = previous ? shownPrefabs.IndexOf(previous) : -1;
        if (selectedIndex < 0 && shownPrefabs.Count > 0) SelectPrefab(0);
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
        if (hidden)
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
        if (bottomAnchoredPlacement) AnchorBottomAtHeight(previewInstance, EffectiveLevel());
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
        Renderer[] renderers = prefab.GetComponentsInChildren<Renderer>(true);
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
        GameObject root = GameObject.Find(RootName);
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
            ShowNotification(new GUIContent("Select a House or Castle piece"));
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
        ShowNotification(new GUIContent("Isolated " + FloorGroupName(activeFloor)));
    }

    void ShowAllFloors()
    {
        GameObject buildRoot = GetBuildRoot(false);
        if (!buildRoot) return;
        SceneVisibilityManager.instance.Show(buildRoot, true);
        SceneView.RepaintAll();
        ShowNotification(new GUIContent("All floors visible"));
    }

    static void DeleteFloorGroupIfEmpty(Transform candidate)
    {
        if (!candidate || candidate.childCount != 0) return;
        if (!candidate.name.StartsWith("FLOOR ", StringComparison.Ordinal)) return;
        Transform parent = candidate.parent;
        if (!parent || parent.name != RootName) return;
        Undo.DestroyObjectImmediate(candidate.gameObject);
    }

    void ScheduleEmptyFloorCleanup()
    {
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
        Repaint(); SceneView.RepaintAll();
    }

    void RotateSelection(float degrees)
    {
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
        if (path.StartsWith(HouseFolder + "/", StringComparison.OrdinalIgnoreCase)) return showHouseAssets;
        if (path.StartsWith(CastleFolder + "/", StringComparison.OrdinalIgnoreCase)) return showCastleAssets;
        return false;
    }

    void EnableAssetSetForPath(string path)
    {
        if (path.StartsWith(HouseFolder + "/", StringComparison.OrdinalIgnoreCase)) showHouseAssets = true;
        if (path.StartsWith(CastleFolder + "/", StringComparison.OrdinalIgnoreCase)) showCastleAssets = true;
    }

    static string CleanName(string value)
    {
        return value.Replace("SM_Bld_House_","").Replace("SM_Bld_Castle_","").Replace("SM_Bld_","").Replace('_',' ');
    }

    void EnsureStyles()
    {
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
