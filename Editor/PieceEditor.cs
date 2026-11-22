using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace Masonry.Editor
{
    [CustomEditor(typeof(Piece))]
    sealed class PieceEditor : UnityEditor.Editor
    {
        const float EdgeTolerance = 0.05f;

        readonly BoxBoundsHandle handle = new();
        bool editBounds;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("bounds"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("tags"));
            serializedObject.ApplyModifiedProperties();

            var piece = (Piece)target;

            using (new EditorGUILayout.HorizontalScope())
            {
                editBounds = GUILayout.Toggle(editBounds, "Edit Bounds", "Button");
                if (GUILayout.Button("Fit To Renderers"))
                {
                    Undo.RecordObject(piece, "Fit Piece Bounds");
                    piece.FitBoundsToRenderers();
                    EditorUtility.SetDirty(piece);
                }
                if (GUILayout.Button("Add Socket"))
                    AddSocket(piece);
            }

            EditorGUILayout.Space();
            GridGUI(piece);

            foreach (var problem in Validate(piece))
                EditorGUILayout.HelpBox(problem, MessageType.Warning);
        }

        // ---- Grid footprint ------------------------------------------------------------------------------------

        const string CellKey = "Masonry.CellSize";
        const string SocketTypeKey = "Masonry.NewSocketType";
        const float SlotSize = 22f;

        static bool showGrid = true;

        internal static float CellSize
        {
            get => EditorPrefs.GetFloat(CellKey, 12f);
            set => EditorPrefs.SetFloat(CellKey, Mathf.Max(0.1f, value));
        }

        static SocketType NewSocketType
        {
            get => AssetDatabase.LoadAssetAtPath<SocketType>(AssetDatabase.GUIDToAssetPath(EditorPrefs.GetString(SocketTypeKey, "")));
            set => EditorPrefs.SetString(SocketTypeKey, value == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(value)));
        }

        enum Side { North, East, South, West }

        static void GridGUI(Piece piece)
        {
            showGrid = EditorGUILayout.Foldout(showGrid, "Grid Footprint", true);
            if (!showGrid)
                return;

            CellSize = EditorGUILayout.FloatField(new GUIContent("Cell Size", "One grid cell in metres. Pieces of different sizes line up when they share a cell size."), CellSize);
            NewSocketType = (SocketType)EditorGUILayout.ObjectField(new GUIContent("New Socket Type", "Type given to sockets added from the grid below."), NewSocketType, typeof(SocketType), false);

            var b = piece.Bounds;
            float cell = CellSize;
            int width = Mathf.Max(1, Mathf.RoundToInt(b.size.x / cell));
            int depth = Mathf.Max(1, Mathf.RoundToInt(b.size.z / cell));

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Footprint", "Width (X) and depth (Z) in cells, then height in metres."));
                int newWidth = Mathf.Clamp(EditorGUILayout.IntField(width, GUILayout.Width(36)), 1, 16);
                GUILayout.Label("x", GUILayout.Width(10));
                int newDepth = Mathf.Clamp(EditorGUILayout.IntField(depth, GUILayout.Width(36)), 1, 16);
                GUILayout.Label("h", GUILayout.Width(10));
                float height = Mathf.Max(0.1f, EditorGUILayout.FloatField(b.size.y, GUILayout.Width(40)));
                if (GUILayout.Button("Set Bounds"))
                {
                    Undo.RecordObject(piece, "Set Piece Footprint");
                    piece.Bounds = new Bounds(new Vector3(0f, height / 2f, 0f), new Vector3(newWidth * cell, height, newDepth * cell));
                    EditorUtility.SetDirty(piece);
                    GUIUtility.ExitGUI();
                }
            }

            bool onGrid = Mathf.Abs(b.size.x - width * cell) < 0.01f && Mathf.Abs(b.size.z - depth * cell) < 0.01f;
            if (!onGrid)
            {
                EditorGUILayout.HelpBox("The bounds are not a whole number of cells. Set Bounds snaps them to the footprint above.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("Click an edge cell to add or remove a socket there. North (top) is +Z.", EditorStyles.miniLabel);
            var rect = GUILayoutUtility.GetRect((width + 2) * SlotSize, (depth + 2) * SlotSize, GUILayout.ExpandWidth(false));

            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < depth; z++)
                {
                    var r = Slot(rect, x + 1, depth - z);
                    EditorGUI.DrawRect(new Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2), new Color(0.5f, 0.5f, 0.5f, 0.35f));
                }
                SlotButton(piece, Slot(rect, x + 1, 0), Side.North, x);
                SlotButton(piece, Slot(rect, x + 1, depth + 1), Side.South, x);
            }
            for (int z = 0; z < depth; z++)
            {
                SlotButton(piece, Slot(rect, width + 1, depth - z), Side.East, z);
                SlotButton(piece, Slot(rect, 0, depth - z), Side.West, z);
            }
        }

        static Rect Slot(Rect area, int column, int row) => new(area.x + column * SlotSize, area.y + row * SlotSize, SlotSize, SlotSize);

        static void SlotButton(Piece piece, Rect rect, Side side, int index)
        {
            EdgeSlot(piece.Bounds, side, index, out var position, out float yaw);
            var existing = SocketAt(piece, position);
            string arrow = side switch { Side.North => "↑", Side.East => "→", Side.South => "↓", _ => "←" };
            var label = new GUIContent(existing != null ? arrow : "", existing != null ? $"{existing.name}: click to remove" : "Click to add a socket here");
            bool on = GUI.Toggle(rect, existing != null, label, EditorStyles.miniButton);
            if (on == (existing != null))
                return;

            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
            }
            else
            {
                var go = new GameObject($"Socket {side} {index + 1}");
                Undo.RegisterCreatedObjectUndo(go, "Add Socket");
                go.transform.SetParent(piece.transform, false);
                go.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                go.AddComponent<Socket>().Type = NewSocketType;
            }
            piece.RefreshSockets();
            EditorUtility.SetDirty(piece);
            GUIUtility.ExitGUI();
        }

        static void EdgeSlot(Bounds b, Side side, int index, out Vector3 position, out float yaw)
        {
            float cell = CellSize;
            float y = b.min.y;
            switch (side)
            {
                case Side.North:
                    position = new Vector3(b.min.x + (index + 0.5f) * cell, y, b.max.z);
                    yaw = 0f;
                    break;
                case Side.South:
                    position = new Vector3(b.min.x + (index + 0.5f) * cell, y, b.min.z);
                    yaw = 180f;
                    break;
                case Side.East:
                    position = new Vector3(b.max.x, y, b.min.z + (index + 0.5f) * cell);
                    yaw = 90f;
                    break;
                default:
                    position = new Vector3(b.min.x, y, b.min.z + (index + 0.5f) * cell);
                    yaw = 270f;
                    break;
            }
        }

        static Socket SocketAt(Piece piece, Vector3 localPosition)
        {
            foreach (var s in piece.GetComponentsInChildren<Socket>(true))
            {
                var p = piece.transform.InverseTransformPoint(s.transform.position);
                if (new Vector2(p.x - localPosition.x, p.z - localPosition.z).sqrMagnitude < 0.01f)
                    return s;
            }
            return null;
        }

        void OnSceneGUI()
        {
            if (!editBounds)
                return;
            var piece = (Piece)target;
            using (new Handles.DrawingScope(piece.transform.localToWorldMatrix))
            {
                var b = piece.Bounds;
                handle.center = b.center;
                handle.size = b.size;
                EditorGUI.BeginChangeCheck();
                handle.DrawHandle();
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(piece, "Edit Piece Bounds");
                    piece.Bounds = new Bounds(handle.center, handle.size);
                    EditorUtility.SetDirty(piece);
                }
            }
        }

        static void AddSocket(Piece piece)
        {
            var go = new GameObject("Socket");
            Undo.RegisterCreatedObjectUndo(go, "Add Socket");
            go.transform.SetParent(piece.transform, false);
            var b = piece.Bounds;
            go.transform.localPosition = new Vector3(b.center.x, b.min.y, b.max.z);
            go.AddComponent<Socket>();
            piece.RefreshSockets();
            Selection.activeGameObject = go;
        }

        /// <summary>Problems that would make the piece join badly or not at all.</summary>
        internal static List<string> Validate(Piece piece)
        {
            var problems = new List<string>();
            var sockets = piece.GetComponentsInChildren<Socket>(true);
            if (sockets.Length == 0)
                problems.Add("No sockets: this piece can only be a lone start piece.");

            var root = piece.transform;
            var b = piece.Bounds;
            for (int i = 0; i < sockets.Length; i++)
            {
                var s = sockets[i];
                var forward = root.InverseTransformDirection(s.transform.forward);
                var up = root.InverseTransformDirection(s.transform.up);
                if (Mathf.Abs(forward.y) > 0.01f || up.y < 0.999f)
                    problems.Add($"{s.name}: tilted. Sockets must face sideways with their up axis straight up; pieces only turn about Y.");

                if (s.Type == null)
                    problems.Add($"{s.name}: no Socket Type. Untyped sockets only join other untyped sockets.");

                var p = root.InverseTransformPoint(s.transform.position);
                bool onEdge = Mathf.Abs(p.x - b.min.x) < EdgeTolerance || Mathf.Abs(p.x - b.max.x) < EdgeTolerance
                    || Mathf.Abs(p.z - b.min.z) < EdgeTolerance || Mathf.Abs(p.z - b.max.z) < EdgeTolerance;
                if (!onEdge)
                    problems.Add($"{s.name}: not on a side of the bounds, so the joined piece will overlap this one or leave a gap.");

                var outward = new Vector3(forward.x, 0f, forward.z);
                var toSocket = new Vector3(p.x - b.center.x, 0f, p.z - b.center.z);
                if (outward.sqrMagnitude > 0f && toSocket.sqrMagnitude > 0f && Vector3.Dot(outward, toSocket) <= 0f)
                    problems.Add($"{s.name}: faces into the piece. Its blue axis should point out.");

                for (int j = 0; j < i; j++)
                    if ((root.InverseTransformPoint(sockets[j].transform.position) - p).sqrMagnitude < 0.0025f)
                        problems.Add($"{s.name} and {sockets[j].name} are in the same place.");
            }
            return problems;
        }
    }
}
