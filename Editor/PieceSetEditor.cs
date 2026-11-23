using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Masonry.Editor
{
    /// <summary>Compact table view of a Piece Set's rules. Also drawn inside the Workbench.</summary>
    [CustomEditor(typeof(PieceSet))]
    sealed class PieceSetEditor : UnityEditor.Editor
    {
        static readonly string[] PieceColumns = { "Piece", "Role", "Weight", "Min", "Max", "Depth" };
        static readonly float[] PieceWidths = { 0.38f, 0.17f, 0.12f, 0.11f, 0.11f, 0.11f };
        static readonly string[] TagColumns = { "Tag", "Min", "Max", "Depth", "Spacing" };
        static readonly float[] TagWidths = { 0.4f, 0.15f, 0.15f, 0.15f, 0.15f };

        static readonly string[] BoundaryColumns = { "Module", "Meets", "Boundary" };
        static readonly float[] BoundaryWidths = { 0.34f, 0.34f, 0.32f };
        static readonly string[] BoundaryTips =
        {
            "A module type.",
            "The module type on the other side. Empty = the edge of the layout.",
            "Boundary style drawn between them. Empty = none: they run into each other.",
        };

        static readonly string[] PieceTips =
        {
            "The piece prefab.",
            "Start: the first piece. Normal: grows the layout. Cap: only closes sockets left open at the end. Arrival: placed only where another level links into this one.",
            "Relative chance among the pieces that fit. 0 = only when a minimum needs it.",
            "At least this many in every layout.",
            "At most this many. 0 = no limit.",
            "Fewest steps from the start piece.",
        };

        static readonly string[] TagTips =
        {
            "Counts every piece carrying this tag.",
            "At least this many pieces with the tag. Min = Max for an exact number.",
            "At most this many. 0 = no limit.",
            "Fewest steps from the start piece.",
            "Fewest steps between two pieces with the tag. 0 or 1 = may touch.",
        };

        ReorderableList pieceList;
        ReorderableList tagList;
        ReorderableList boundaryList;

        void OnEnable()
        {
            pieceList = new ReorderableList(serializedObject, serializedObject.FindProperty("pieces"), true, true, true, true)
            {
                drawHeaderCallback = r => Header(r, PieceColumns, PieceWidths, PieceTips, 14f),
                drawElementCallback = (r, i, _, _) => Row(r, pieceList.serializedProperty.GetArrayElementAtIndex(i),
                    new[] { "piece", "role", "weight", "minCount", "maxCount", "minDepth" }, PieceWidths),
                elementHeight = EditorGUIUtility.singleLineHeight + 4f,
            };
            tagList = new ReorderableList(serializedObject, serializedObject.FindProperty("tagRules"), true, true, true, true)
            {
                drawHeaderCallback = r => Header(r, TagColumns, TagWidths, TagTips, 14f),
                drawElementCallback = (r, i, _, _) => Row(r, tagList.serializedProperty.GetArrayElementAtIndex(i),
                    new[] { "tag", "minCount", "maxCount", "minDepth", "minSpacing" }, TagWidths),
                elementHeight = EditorGUIUtility.singleLineHeight + 4f,
            };
            boundaryList = new ReorderableList(serializedObject, serializedObject.FindProperty("boundaryRules"), true, true, true, true)
            {
                drawHeaderCallback = r => Header(r, BoundaryColumns, BoundaryWidths, BoundaryTips, 14f),
                drawElementCallback = (r, i, _, _) => Row(r, boundaryList.serializedProperty.GetArrayElementAtIndex(i),
                    new[] { "a", "b", "style" }, BoundaryWidths),
                elementHeight = EditorGUIUtility.singleLineHeight + 4f,
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField("Layout", EditorStyles.boldLabel);
            var settings = serializedObject.FindProperty("settings");
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.PrefixLabel(new GUIContent("Pieces", "Fewest and most pieces, counting the start piece but not caps."));
                var min = settings.FindPropertyRelative("minPieces");
                var max = settings.FindPropertyRelative("maxPieces");
                min.intValue = Mathf.Max(1, EditorGUILayout.IntField(min.intValue));
                GUILayout.Label("to", GUILayout.Width(16));
                max.intValue = Mathf.Max(min.intValue, EditorGUILayout.IntField(max.intValue));
            }
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("attempts"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("gridSize"));
            using (new EditorGUI.IndentLevelScope())
            {
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("overlapTolerance"));
                EditorGUILayout.PropertyField(settings.FindPropertyRelative("joinDistance"));
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Pieces", EditorStyles.boldLabel);
            pieceList.DoLayoutList();

            EditorGUILayout.LabelField("Tag Rules", EditorStyles.boldLabel);
            tagList.DoLayoutList();

            EditorGUILayout.LabelField(new GUIContent("Boundaries", "Which boundary goes where two module types meet. Rules match either way round; the first match wins."), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("defaultBoundary"), new GUIContent("Default", "Boundary for meetings no rule covers. Empty = none."));
            boundaryList.DoLayoutList();

            serializedObject.ApplyModifiedProperties();

            if (target is PieceSet set && !(EditorWindow.HasOpenInstances<WorkbenchWindow>() && WorkbenchWindow.IsShowing(set)))
            {
                if (GUILayout.Button("Open in Workbench"))
                    WorkbenchWindow.Open(set);
            }
        }

        static void Header(Rect rect, string[] columns, float[] widths, string[] tips, float indent)
        {
            rect.xMin += indent;
            float x = rect.x;
            for (int i = 0; i < columns.Length; i++)
            {
                float w = rect.width * widths[i];
                EditorGUI.LabelField(new Rect(x, rect.y, w, rect.height), new GUIContent(columns[i], tips[i]));
                x += w;
            }
        }

        static void Row(Rect rect, SerializedProperty element, string[] fields, float[] widths)
        {
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;
            float x = rect.x;
            for (int i = 0; i < fields.Length; i++)
            {
                float w = rect.width * widths[i];
                var cell = new Rect(x, rect.y, w - 4f, rect.height);
                var property = element.FindPropertyRelative(fields[i]);
                EditorGUI.PropertyField(cell, property, GUIContent.none);
                if (property.propertyType == SerializedPropertyType.Integer)
                    property.intValue = Mathf.Max(0, property.intValue);
                else if (property.propertyType == SerializedPropertyType.Float)
                    property.floatValue = Mathf.Max(0f, property.floatValue);
                x += w;
            }
        }
    }
}
