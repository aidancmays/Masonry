using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEditor;
using UnityEngine;

namespace Masonry.Editor
{
    /// <summary>
    /// Window > Masonry > Workbench. Edit a Piece Set's rules on the left; the right side redraws a page of seeds as
    /// top-down maps every time a rule changes. Run a batch of seeds for statistics, and build any seed into the
    /// scene to look at it in 3D.
    /// </summary>
    sealed class WorkbenchWindow : EditorWindow
    {
        const string PreviewName = "Masonry Preview";
        const float CaptionHeight = 34f;
        const float Pad = 6f;

        [SerializeField] PieceSet set;
        [SerializeField] int firstSeed = 1;
        [SerializeField] int pageSize = 8;
        [SerializeField] float mapSize = 220f;
        [SerializeField] int selected = -1;
        [SerializeField] int batchSize = 200;
        [SerializeField] float rulesWidth = 380f;
        [SerializeField] int viewLevel;
        [SerializeField] string flagsText = "";

        UnityEditor.Editor setEditor;
        Vector2 rulesScroll;
        Vector2 mapsScroll;
        bool dirty = true;
        int lastDirtyCount = -1;
        string error;

        LayoutGenerator generator;
        List<PieceSet.Entry> entries = new();
        List<PieceTag> tags = new();
        readonly List<Layout> page = new();
        readonly Dictionary<Layout, List<(Vector3 a, Vector3 b, Color color)>> boundaryLines = new();
        List<(Vector3 position, float yaw, float length)>[] boundaryDefs = Array.Empty<List<(Vector3, float, float)>>();
        BatchStats stats;

        // Levels per seed on the page, and the level each shown layout belongs to.
        readonly List<List<LevelLayout>> pageLevels = new();
        readonly Dictionary<Layout, LevelLayout> levelOf = new();
        readonly Dictionary<PieceSet, List<PieceTag>> tagsOf = new();
        readonly Dictionary<PieceSet, List<(Vector3, float, float)>[]> boundaryDefsOf = new();
        PieceSet ruleSet;
        string[] levelNames = Array.Empty<string>();

        [MenuItem("Window/Masonry/Workbench")]
        static void ShowWindow() => Open(null);

        public static void Open(PieceSet set)
        {
            var window = GetWindow<WorkbenchWindow>("Masonry Workbench");
            window.minSize = new Vector2(640f, 360f);
            if (set != null)
                window.SetPieceSet(set);
            window.Show();
        }

        public static bool IsShowing(PieceSet set)
        {
            foreach (var w in Resources.FindObjectsOfTypeAll<WorkbenchWindow>())
                if (w.set == set)
                    return true;
            return false;
        }

        void OnEnable()
        {
            wantsMouseMove = true;
            Undo.undoRedoPerformed += MarkDirty;
            if (set == null && Selection.activeObject is PieceSet s)
                set = s;
        }

        void OnDisable()
        {
            Undo.undoRedoPerformed -= MarkDirty;
            if (setEditor != null)
                DestroyImmediate(setEditor);
        }

        void OnSelectionChange()
        {
            if (Selection.activeObject is PieceSet s && s != set)
            {
                SetPieceSet(s);
                Repaint();
            }
        }

        // Rules edited elsewhere (the inspector, a piece prefab) also refresh the maps.
        void OnInspectorUpdate()
        {
            if (set == null)
                return;
            int count = EditorUtility.GetDirtyCount(set);
            if (count != lastDirtyCount)
            {
                lastDirtyCount = count;
                MarkDirty();
            }
        }

        void SetPieceSet(PieceSet s)
        {
            set = s;
            viewLevel = 0;
            levelNames = Array.Empty<string>();
            selected = -1;
            stats = null;
            MarkDirty();
        }

        void MarkDirty()
        {
            dirty = true;
            Repaint();
        }

        // ---- GUI ----------------------------------------------------------------------------------------------

        void OnGUI()
        {
            // Before anything is laid out, so the toolbar's level picker matches between Layout and Repaint.
            if (set != null && dirty && Event.current.type == EventType.Layout)
                Regenerate();

            Toolbar();
            if (set == null)
            {
                EditorGUILayout.HelpBox("Pick a Piece Set (Create > Masonry > Piece Set) to edit its rules and preview layouts.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (var scroll = new EditorGUILayout.ScrollViewScope(rulesScroll, GUILayout.Width(rulesWidth)))
                {
                    rulesScroll = scroll.scrollPosition;
                    UnityEditor.Editor.CreateCachedEditor(set, typeof(PieceSetEditor), ref setEditor);
                    EditorGUI.BeginChangeCheck();
                    setEditor.OnInspectorGUI();
                    if (EditorGUI.EndChangeCheck())
                    {
                        stats = null;
                        MarkDirty();
                    }
                }

                Splitter();

                using (var scroll = new EditorGUILayout.ScrollViewScope(mapsScroll))
                {
                    mapsScroll = scroll.scrollPosition;
                    if (!string.IsNullOrEmpty(error))
                        EditorGUILayout.HelpBox(error, MessageType.Error);
                    Maps();
                    Details();
                    Batch();
                }
            }
        }

        void Toolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUI.BeginChangeCheck();
                var s = (PieceSet)EditorGUILayout.ObjectField(set, typeof(PieceSet), false, GUILayout.Width(220f));
                if (EditorGUI.EndChangeCheck())
                    SetPieceSet(s);

                GUILayout.Space(8f);
                GUILayout.Label("Seeds", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                if (GUILayout.Button("◀", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                    ShiftSeeds(-pageSize);
                EditorGUI.BeginChangeCheck();
                firstSeed = EditorGUILayout.IntField(firstSeed, EditorStyles.toolbarTextField, GUILayout.Width(90f));
                GUILayout.Label($"to {firstSeed + pageSize - 1}", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                if (EditorGUI.EndChangeCheck())
                    MarkDirty();
                if (GUILayout.Button("▶", EditorStyles.toolbarButton, GUILayout.Width(24f)))
                    ShiftSeeds(pageSize);
                if (GUILayout.Button("Random", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                {
                    firstSeed = UnityEngine.Random.Range(0, int.MaxValue - 100);
                    selected = -1;
                    MarkDirty();
                }

                GUILayout.Space(8f);
                GUILayout.Label("Count", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                EditorGUI.BeginChangeCheck();
                pageSize = EditorGUILayout.IntSlider(pageSize, 1, 24, GUILayout.Width(140f));
                if (EditorGUI.EndChangeCheck())
                    MarkDirty();
                GUILayout.Label("Size", EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                mapSize = GUILayout.HorizontalSlider(mapSize, 120f, 520f, GUILayout.Width(80f));

                GUILayout.Space(8f);
                GUILayout.Label(new GUIContent("Flags", "Build flags for entry conditions, comma separated, as the game would pass them (e.g. exit-gate)."), EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
                EditorGUI.BeginChangeCheck();
                flagsText = EditorGUILayout.DelayedTextField(flagsText, EditorStyles.toolbarTextField, GUILayout.Width(120f));
                if (EditorGUI.EndChangeCheck())
                {
                    selected = -1;
                    stats = null;
                    MarkDirty();
                }

                GUILayout.FlexibleSpace();
                if (levelNames.Length > 1)
                {
                    EditorGUI.BeginChangeCheck();
                    viewLevel = EditorGUILayout.Popup(viewLevel, levelNames, EditorStyles.toolbarPopup, GUILayout.Width(170f));
                    if (EditorGUI.EndChangeCheck())
                    {
                        selected = -1;
                        MarkDirty();
                    }
                    var shown = pageLevels.Count > 0 && viewLevel < pageLevels[0].Count ? pageLevels[0][viewLevel].Set : null;
                    using (new EditorGUI.DisabledScope(shown == null || shown == set))
                        if (GUILayout.Button(new GUIContent("Edit Its Rules", "Open this level's Piece Set in the Workbench."), EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                            SetPieceSet(shown);
                }
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.ExpandWidth(false)))
                    MarkDirty();
            }
        }

        void ShiftSeeds(int by)
        {
            firstSeed += by;
            selected = -1;
            MarkDirty();
        }

        void Splitter()
        {
            var rect = GUILayoutUtility.GetRect(4f, 4f, GUILayout.ExpandHeight(true), GUILayout.Width(4f));
            EditorGUI.DrawRect(new Rect(rect.x + 1f, rect.y, 1f, rect.height), new Color(0f, 0f, 0f, 0.3f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);
            int id = GUIUtility.GetControlID(FocusType.Passive);
            var e = Event.current;
            switch (e.GetTypeForControl(id))
            {
                case EventType.MouseDown when rect.Contains(e.mousePosition):
                    GUIUtility.hotControl = id;
                    e.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == id:
                    rulesWidth = Mathf.Clamp(rulesWidth + e.delta.x, 260f, position.width - 260f);
                    e.Use();
                    Repaint();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == id:
                    GUIUtility.hotControl = 0;
                    e.Use();
                    break;
            }
        }

        // ---- Generation ---------------------------------------------------------------------------------------

        void Regenerate()
        {
            dirty = false;
            error = null;
            page.Clear();
            pageLevels.Clear();
            levelOf.Clear();
            tagsOf.Clear();
            boundaryDefsOf.Clear();
            boundaryLines.Clear();
            try
            {
                if (set.Pieces.Count == 0)
                {
                    error = "The set has no pieces yet.";
                    return;
                }
                for (int i = 0; i < pageSize; i++)
                {
                    var levels = LevelPlan.Generate(set, firstSeed + i, Flags.Parse(flagsText));
                    pageLevels.Add(levels);
                    foreach (var level in levels)
                        levelOf[level.Layout] = level;
                }

                // Level names come from the first seed; seeds that never reach a level show their first level.
                var first = pageLevels[0];
                levelNames = new string[first.Count];
                for (int l = 0; l < first.Count; l++)
                    levelNames[l] = $"Level {l + 1}: {first[l].Set.name}";
                viewLevel = Mathf.Clamp(viewLevel, 0, Mathf.Max(0, first.Count - 1));

                foreach (var levels in pageLevels)
                {
                    var level = viewLevel < levels.Count ? levels[viewLevel] : levels[0];
                    Use(level);
                    page.Add(level.Layout);
                    boundaryLines[level.Layout] = BoundaryLines(level.Layout);
                }
                Use(pageLevels[0][0]);
            }
            catch (Exception ex)
            {
                error = ex.Message;
                UnityEngine.Debug.LogException(ex);
            }
        }

        // Points the drawing helpers at one level's set, pieces and generator.
        void Use(LevelLayout level)
        {
            ruleSet = level.Set;
            generator = level.Generator;
            entries = level.Entries;
            if (!tagsOf.TryGetValue(level.Set, out tags))
            {
                level.Set.CreateGenerator(out _, out tags, level.Flags as ICollection<string>);
                tagsOf[level.Set] = tags;
            }
            if (!boundaryDefsOf.TryGetValue(level.Set, out boundaryDefs))
            {
                ReadBoundaries();
                boundaryDefsOf[level.Set] = boundaryDefs;
            }
        }

        void Use(Layout layout)
        {
            if (levelOf.TryGetValue(layout, out var level))
                Use(level);
        }

        // ---- Boundaries on the maps ---------------------------------------------------------------------------

        // Boundary positions of each piece prefab, in piece space.
        void ReadBoundaries()
        {
            boundaryDefs = new List<(Vector3, float, float)>[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                var piece = entries[i].piece;
                piece.RefreshSockets();
                var list = new List<(Vector3, float, float)>();
                foreach (var b in piece.Boundaries)
                {
                    var t = b.transform;
                    var forward = piece.transform.InverseTransformDirection(t.forward);
                    list.Add((piece.transform.InverseTransformPoint(t.position), Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, b.Length));
                }
                boundaryDefs[i] = list;
            }
        }

        // Same pairing and rules as BoundaryResolver, drawn as coloured edges. Edges with no boundary are left out.
        List<(Vector3, Vector3, Color)> BoundaryLines(Layout layout)
        {
            var edges = new List<(Vector3 position, Vector3 forward, Vector3 right, int placement)>();
            for (int i = 0; i < layout.Placements.Count; i++)
            {
                var p = layout.Placements[i];
                foreach (var (position, yaw, length) in boundaryDefs[p.piece])
                {
                    var rot = Quaternion.Euler(0f, p.yaw + yaw, 0f);
                    edges.Add((p.position + p.Rotation * position, rot * Vector3.forward, rot * Vector3.right * (length / 2f), i));
                }
            }

            var lines = new List<(Vector3, Vector3, Color)>();
            var done = new bool[edges.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                if (done[i])
                    continue;
                done[i] = true;
                var e = edges[i];
                var type = entries[layout.Placements[e.placement].piece].piece.ModuleType;
                int match = -1;
                for (int k = i + 1; k < edges.Count && match < 0; k++)
                {
                    var o = edges[k];
                    if (done[k] || o.placement == e.placement)
                        continue;
                    var d = o.position - e.position;
                    d.y = 0f;
                    if (d.sqrMagnitude < 0.0025f && Vector3.Dot(o.forward, e.forward) < -0.99f)
                        match = k;
                }

                BoundaryStyle style;
                if (match < 0)
                {
                    style = ruleSet.BoundaryFor(type, null, outside: true);
                }
                else
                {
                    done[match] = true;
                    var otherType = entries[layout.Placements[edges[match].placement].piece].piece.ModuleType;
                    style = ruleSet.BoundaryFor(type, otherType, outside: false);
                }
                if (style != null)
                    lines.Add((e.position - e.right, e.position + e.right, style.Color));
            }
            return lines;
        }

        // ---- Maps ---------------------------------------------------------------------------------------------

        void Maps()
        {
            if (page.Count == 0)
                return;

            float available = position.width - rulesWidth - 24f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(available / (mapSize + Pad)));
            int rows = Mathf.CeilToInt(page.Count / (float)columns);
            var area = GUILayoutUtility.GetRect(columns * (mapSize + Pad), rows * (mapSize + CaptionHeight + Pad));

            for (int i = 0; i < page.Count; i++)
            {
                int col = i % columns, row = i / columns;
                var cell = new Rect(area.x + col * (mapSize + Pad), area.y + row * (mapSize + CaptionHeight + Pad), mapSize, mapSize + CaptionHeight);
                var mapRect = new Rect(cell.x, cell.y, mapSize, mapSize);
                var layout = page[i];
                Use(layout);

                DrawMap(mapRect, layout, i == selected);

                var caption = new Rect(cell.x, cell.y + mapSize + 2f, mapSize, CaptionHeight - 2f);
                string status = layout.Valid ? "" : layout.DeadEndViolations > 0 ? $"  ✗ {layout.DeadEndViolations} empty dead end(s)" : "  ✗ rules not met";
                GUI.Label(caption, $"Seed {firstSeed + i}{status}\n{Grown(layout)} pieces, {Count(layout, PieceRole.Cap)} caps, {layout.OpenSockets.Count} open", EditorStyles.miniLabel);

                var e = Event.current;
                if (e.type == EventType.MouseDown && cell.Contains(e.mousePosition))
                {
                    selected = i;
                    if (e.clickCount == 2)
                        BuildInScene(firstSeed + i);
                    e.Use();
                    Repaint();
                }
                if (mapRect.Contains(e.mousePosition))
                    HoverLabel(mapRect, layout);
            }
            Use(page[0]);
            Legend();
        }

        void DrawMap(Rect rect, Layout layout, bool isSelected)
        {
            EditorGUI.DrawRect(rect, new Color(0.13f, 0.13f, 0.14f));
            if (isSelected)
                DrawOutline(rect, new Color(0.3f, 0.6f, 1f), 2f);
            if (Event.current.type != EventType.Repaint || layout.Placements.Count == 0)
                return;

            var toScreen = MapTransform(rect, layout);
            var corners = new Vector3[4];
            var outline = new Vector3[5];

            for (int i = 0; i < layout.Placements.Count; i++)
            {
                var p = layout.Placements[i];
                Corners(layout.Pieces[p.piece].bounds, p, corners);
                for (int k = 0; k < 4; k++)
                    corners[k] = toScreen(corners[k]);
                var color = PieceColor(layout, p);
                Handles.color = color;
                Handles.DrawAAConvexPolygon(corners);
                for (int k = 0; k < 4; k++)
                    outline[k] = corners[k];
                outline[4] = corners[0];
                Handles.color = new Color(0f, 0f, 0f, 0.6f);
                Handles.DrawAAPolyLine(1.5f, outline);
            }

            if (boundaryLines.TryGetValue(layout, out var lines))
            {
                foreach (var (a, b, color) in lines)
                {
                    Handles.color = color;
                    Handles.DrawAAPolyLine(3f, toScreen(a), toScreen(b));
                }
            }

            Handles.color = new Color(1f, 1f, 1f, 0.55f);
            foreach (var c in layout.Connections)
            {
                var a = toScreen(Center(layout, c.a.placement));
                var b = toScreen(Center(layout, c.b.placement));
                Handles.DrawAAPolyLine(2f, a, b);
            }

            Handles.color = new Color(1f, 0.25f, 0.2f);
            foreach (var o in layout.OpenSockets)
            {
                generator.GetSocketPose(layout.Placements[o.placement], o.socket, out var pos, out _);
                var s = toScreen(pos);
                Handles.DrawAAConvexPolygon(s + new Vector3(-3, -3), s + new Vector3(3, -3), s + new Vector3(3, 3), s + new Vector3(-3, 3));
            }

            var start = toScreen(Center(layout, 0));
            Handles.color = Color.white;
            Handles.DrawWireDisc(start, Vector3.forward, 5f);

            // Long dead ends with nothing at the end.
            int minLength = generator.DeadEndRule.minLength;
            if (minLength > 0)
            {
                Handles.color = new Color(1f, 0.25f, 0.2f);
                foreach (var d in layout.DeadEnds)
                    if (!d.hasContent && d.length >= minLength)
                        Handles.DrawWireDisc(toScreen(Center(layout, d.placement)), Vector3.forward, 7f, 2f);
            }
        }

        void HoverLabel(Rect rect, Layout layout)
        {
            if (Event.current.type == EventType.MouseMove)
            {
                Repaint();
                return;
            }
            if (Event.current.type != EventType.Repaint || layout.Placements.Count == 0)
                return;

            var toWorld = InverseMapTransform(rect, layout);
            var world = toWorld(Event.current.mousePosition);
            for (int i = layout.Placements.Count - 1; i >= 0; i--)
            {
                var p = layout.Placements[i];
                var local = Quaternion.Inverse(p.Rotation) * (world - p.position);
                var b = layout.Pieces[p.piece].bounds;
                if (local.x < b.min.x || local.x > b.max.x || local.z < b.min.z || local.z > b.max.z)
                    continue;
                string text = $"{layout.Pieces[p.piece].name}  (depth {p.depth})";
                var size = EditorStyles.helpBox.CalcSize(new GUIContent(text));
                var at = Event.current.mousePosition + new Vector2(12f, 4f);
                at.x = Mathf.Min(at.x, rect.xMax - size.x);
                GUI.Label(new Rect(at, size), text, EditorStyles.helpBox);
                return;
            }
        }

        void Legend()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                Swatch(StartColor, "Start");
                Swatch(RequiredColor, "Required piece");
                foreach (var tag in tags)
                    if (tag != null)
                        Swatch(tag.Color, tag.name);
                Swatch(CapColor, "Cap");
                foreach (var style in BoundaryStyles())
                    Swatch(style.Color, style.name);
                Swatch(new Color(1f, 0.25f, 0.2f), "Open socket / empty dead end");
                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.LabelField("Click a map to select it, double-click to build it in the scene. Lines join connected pieces.", EditorStyles.miniLabel);
        }

        IEnumerable<BoundaryStyle> BoundaryStyles()
        {
            var seen = new HashSet<BoundaryStyle>();
            if (ruleSet == null)
                yield break;
            if (ruleSet.DefaultBoundary != null && seen.Add(ruleSet.DefaultBoundary))
                yield return ruleSet.DefaultBoundary;
            foreach (var r in ruleSet.BoundaryRules)
                if (r?.style != null && seen.Add(r.style))
                    yield return r.style;
        }

        static void Swatch(Color color, string label)
        {
            var r = GUILayoutUtility.GetRect(12f, 12f, GUILayout.Width(12f));
            r.y += 3f;
            r.height = 10f;
            EditorGUI.DrawRect(r, color);
            GUILayout.Label(label, EditorStyles.miniLabel, GUILayout.ExpandWidth(false));
            GUILayout.Space(6f);
        }

        // ---- Selected layout ----------------------------------------------------------------------------------

        void Details()
        {
            if (selected < 0 || selected >= page.Count)
                return;
            var layout = page[selected];
            Use(layout);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Seed {firstSeed + selected}", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(layout.Valid
                ? $"Met every rule on try {layout.Attempt + 1}."
                : $"No try met every rule; this is the closest of {generator.Settings.attempts} (short by {generator.Deficit(layout)} pieces).");

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                    if (GUILayout.Button("Build in Scene", GUILayout.Width(120f)))
                        BuildInScene(firstSeed + selected);
                if (GUILayout.Button("Copy Seed", GUILayout.Width(90f)))
                    EditorGUIUtility.systemCopyBuffer = (firstSeed + selected).ToString();
                if (GUILayout.Button("Clear Scene Preview", GUILayout.Width(140f)))
                    ClearScenePreview();
            }

            foreach (var rule in generator.TagRules)
            {
                var tag = tags[rule.tag];
                EditorGUILayout.LabelField(tag != null ? tag.name : "(missing tag)", $"{layout.CountWithTag(rule.tag)}  (rule {RuleRange(rule.minCount, rule.maxCount)})");
            }
            for (int p = 0; p < layout.Pieces.Count; p++)
            {
                int n = layout.CountOf(p);
                if (n > 0)
                    EditorGUILayout.LabelField(layout.Pieces[p].name, n.ToString());
            }
        }

        static string RuleRange(int min, int max)
        {
            if (max > 0 && min == max)
                return $"exactly {min}";
            if (max > 0)
                return $"{min} to {max}";
            return $"at least {min}";
        }

        void BuildInScene(int seed)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;

            LayoutBuilder builder = null;
            foreach (var b in SceneBuilders())
                if (b.PieceSet == set && (builder == null || (b.gameObject.hideFlags & HideFlags.DontSave) == 0))
                    builder = b;

            if (builder == null)
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags(PreviewName, HideFlags.DontSave, typeof(LayoutBuilder));
                builder = go.GetComponent<LayoutBuilder>();
                builder.PieceSet = set;
            }

            builder.BuildPreview(seed, Flags.Parse(flagsText));
            Selection.activeGameObject = builder.gameObject;

            var bounds = new Bounds(builder.transform.position, Vector3.one);
            foreach (var r in builder.GetComponentsInChildren<Renderer>())
                bounds.Encapsulate(r.bounds);
            var view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                view.Frame(bounds, false);
                view.Repaint();
            }
        }

        /// <summary>Builders in loaded scenes, including Workbench preview roots (which FindObjectsByType skips).</summary>
        internal static List<LayoutBuilder> SceneBuilders()
        {
            var found = new List<LayoutBuilder>();
            foreach (var b in Resources.FindObjectsOfTypeAll<LayoutBuilder>())
                if (b != null && !EditorUtility.IsPersistent(b) && b.gameObject.scene.IsValid())
                    found.Add(b);
            return found;
        }

        void ClearScenePreview()
        {
            foreach (var b in SceneBuilders())
            {
                if (b.PieceSet != set)
                    continue;
                if ((b.gameObject.hideFlags & HideFlags.DontSave) != 0)
                    DestroyImmediate(b.gameObject);
                else
                    b.Clear();
            }
        }

        // ---- Batch statistics ---------------------------------------------------------------------------------

        sealed class BatchStats
        {
            public int seeds, valid, minPieces = int.MaxValue, maxPieces, caps, open, attempts;
            public long totalPieces;
            public double milliseconds;
            public int[] pieceTotals, pieceLayouts;
            public int[] tagTotals, tagMin, tagMax;
        }

        void Batch()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Batch", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                batchSize = Mathf.Clamp(EditorGUILayout.IntField("Seeds", batchSize), 10, 5000);
                if (GUILayout.Button($"Run {batchSize} Seeds", GUILayout.Width(130f)) && generator != null)
                    RunBatch();
            }
            if (stats == null)
                return;

            int n = stats.seeds;
            EditorGUILayout.LabelField("Met every rule", $"{stats.valid} / {n}  ({100f * stats.valid / n:0.#}%)");
            EditorGUILayout.LabelField("Pieces", $"{(double)stats.totalPieces / n:0.0} average, {stats.minPieces} to {stats.maxPieces}");
            EditorGUILayout.LabelField("Caps, open sockets", $"{(float)stats.caps / n:0.0}, {(float)stats.open / n:0.0} average");
            EditorGUILayout.LabelField("Tries per layout", $"{(float)stats.attempts / n:0.0} average");
            EditorGUILayout.LabelField("Time", $"{stats.milliseconds / n:0.00} ms per layout");

            if (stats.tagTotals.Length > 0)
            {
                EditorGUILayout.LabelField("Tags", EditorStyles.miniBoldLabel);
                for (int r = 0; r < stats.tagTotals.Length; r++)
                {
                    var tag = tags[generator.TagRules[r].tag];
                    EditorGUILayout.LabelField(tag != null ? tag.name : "(missing tag)", $"{(float)stats.tagTotals[r] / n:0.0} average, {stats.tagMin[r]} to {stats.tagMax[r]}");
                }
            }

            EditorGUILayout.LabelField("Pieces (average per layout, share of layouts with one)", EditorStyles.miniBoldLabel);
            for (int p = 0; p < stats.pieceTotals.Length; p++)
                EditorGUILayout.LabelField(generator.Pieces[p].name, $"{(float)stats.pieceTotals[p] / n:0.00}   {100f * stats.pieceLayouts[p] / n:0}%");
        }

        void RunBatch()
        {
            Use(pageLevels[0][0]);
            var s = new BatchStats
            {
                seeds = batchSize,
                pieceTotals = new int[generator.Pieces.Count],
                pieceLayouts = new int[generator.Pieces.Count],
                tagTotals = new int[generator.TagRules.Count],
                tagMin = new int[generator.TagRules.Count],
                tagMax = new int[generator.TagRules.Count],
            };
            for (int r = 0; r < s.tagMin.Length; r++)
                s.tagMin[r] = int.MaxValue;

            var watch = Stopwatch.StartNew();
            for (int i = 0; i < batchSize; i++)
            {
                if (i % 50 == 0 && EditorUtility.DisplayCancelableProgressBar("Masonry", $"Seed {i} of {batchSize}", i / (float)batchSize))
                {
                    s.seeds = Mathf.Max(1, i);
                    break;
                }
                var layout = generator.Generate(firstSeed + i);
                if (layout.Valid)
                    s.valid++;
                int grown = Grown(layout);
                s.totalPieces += grown;
                s.minPieces = Mathf.Min(s.minPieces, grown);
                s.maxPieces = Mathf.Max(s.maxPieces, grown);
                s.caps += Count(layout, PieceRole.Cap);
                s.open += layout.OpenSockets.Count;
                s.attempts += layout.Attempt + 1;
                for (int p = 0; p < s.pieceTotals.Length; p++)
                {
                    int c = layout.CountOf(p);
                    s.pieceTotals[p] += c;
                    if (c > 0)
                        s.pieceLayouts[p]++;
                }
                for (int r = 0; r < s.tagTotals.Length; r++)
                {
                    int c = layout.CountWithTag(generator.TagRules[r].tag);
                    s.tagTotals[r] += c;
                    s.tagMin[r] = Mathf.Min(s.tagMin[r], c);
                    s.tagMax[r] = Mathf.Max(s.tagMax[r], c);
                }
            }
            EditorUtility.ClearProgressBar();
            s.milliseconds = watch.Elapsed.TotalMilliseconds;
            stats = s;
        }

        // ---- Helpers ------------------------------------------------------------------------------------------

        static readonly Color StartColor = new(0.35f, 0.75f, 0.4f);
        static readonly Color CapColor = new(0.45f, 0.45f, 0.47f);
        static readonly Color RequiredColor = new(0.95f, 0.75f, 0.25f);

        Color PieceColor(Layout layout, Placement p)
        {
            var d = layout.Pieces[p.piece];
            if (d.role == PieceRole.Start || d.role == PieceRole.Arrival)
                return StartColor;
            foreach (int t in d.tags)
                if (t < tags.Count && tags[t] != null)
                    return tags[t].Color;
            if (d.minCount > 0)
                return RequiredColor;
            if (d.role == PieceRole.Cap)
                return CapColor;
            var module = p.piece < entries.Count ? entries[p.piece].piece.ModuleType : null;
            if (module != null)
                return module.Color;
            // Normal pieces: muted blues, a slightly different shade per piece so repeats are visible.
            float hue = 0.55f + (p.piece * 0.07f % 0.12f);
            return Color.HSVToRGB(hue, 0.3f, 0.55f + (p.piece % 3) * 0.08f);
        }

        static int Grown(Layout layout)
        {
            int n = 0;
            foreach (var p in layout.Placements)
                if (layout.Pieces[p.piece].role != PieceRole.Cap)
                    n++;
            return n;
        }

        static int Count(Layout layout, PieceRole role)
        {
            int n = 0;
            foreach (var p in layout.Placements)
                if (layout.Pieces[p.piece].role == role)
                    n++;
            return n;
        }

        static void Corners(Bounds b, Placement p, Vector3[] into)
        {
            var r = p.Rotation;
            into[0] = p.position + r * new Vector3(b.min.x, 0f, b.min.z);
            into[1] = p.position + r * new Vector3(b.max.x, 0f, b.min.z);
            into[2] = p.position + r * new Vector3(b.max.x, 0f, b.max.z);
            into[3] = p.position + r * new Vector3(b.min.x, 0f, b.max.z);
        }

        static Vector3 Center(Layout layout, int placement)
        {
            var p = layout.Placements[placement];
            return p.position + p.Rotation * layout.Pieces[p.piece].bounds.center;
        }

        static void WorldExtent(Layout layout, out Vector2 center, out float scaleBasis)
        {
            var corners = new Vector3[4];
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in layout.Placements)
            {
                Corners(layout.Pieces[p.piece].bounds, p, corners);
                foreach (var c in corners)
                {
                    minX = Mathf.Min(minX, c.x);
                    maxX = Mathf.Max(maxX, c.x);
                    minZ = Mathf.Min(minZ, c.z);
                    maxZ = Mathf.Max(maxZ, c.z);
                }
            }
            center = new Vector2((minX + maxX) / 2f, (minZ + maxZ) / 2f);
            scaleBasis = Mathf.Max(maxX - minX, maxZ - minZ, 1f);
        }

        static Func<Vector3, Vector3> MapTransform(Rect rect, Layout layout)
        {
            WorldExtent(layout, out var c, out float extent);
            float scale = (Mathf.Min(rect.width, rect.height) - 12f) / extent;
            var mid = rect.center;
            return w => new Vector3(mid.x + (w.x - c.x) * scale, mid.y - (w.z - c.y) * scale, 0f);
        }

        static Func<Vector2, Vector3> InverseMapTransform(Rect rect, Layout layout)
        {
            WorldExtent(layout, out var c, out float extent);
            float scale = (Mathf.Min(rect.width, rect.height) - 12f) / extent;
            var mid = rect.center;
            return s => new Vector3(c.x + (s.x - mid.x) / scale, 0f, c.y - (s.y - mid.y) / scale);
        }

        static void DrawOutline(Rect r, Color color, float width)
        {
            EditorGUI.DrawRect(new Rect(r.x - width, r.y - width, r.width + width * 2, width), color);
            EditorGUI.DrawRect(new Rect(r.x - width, r.yMax, r.width + width * 2, width), color);
            EditorGUI.DrawRect(new Rect(r.x - width, r.y, width, r.height), color);
            EditorGUI.DrawRect(new Rect(r.xMax, r.y, width, r.height), color);
        }
    }
}
