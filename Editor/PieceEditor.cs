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

            foreach (var problem in Validate(piece))
                EditorGUILayout.HelpBox(problem, MessageType.Warning);
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
