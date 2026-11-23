using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Masonry.Editor
{
    /// <summary>Compact table view of a Piece Set's rules. Also drawn inside the Workbench.</summary>
    [CustomEditor(typeof(PieceSet))]
    sealed class PieceSetEditor : UnityEditor.Editor
    {
        static readonly string[] PieceColumns = { "Piece", "Role", "Weight", "Min", "Max", "Depth", "Only If" };
        static readonly float[] PieceWidths = { 0.29f, 0.15f, 0.1f, 0.08f, 0.08f, 0.09f, 0.21f };
        static readonly string[] TagColumns = { "Tag", "Min", "Max", "Depth", "Spacing", "Step" };
        static readonly float[] TagWidths = { 0.35f, 0.13f, 0.13f, 0.13f, 0.13f, 0.13f };

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
            "Build flags this piece needs, comma separated; '!flag' = only without it. Empty = always. Builds always have 'main' or 'linked'.",
        };

        static readonly string[] TagTips =
        {
            "Counts every piece carrying this tag.",
            "At least this many pieces with the tag. Min = Max for an exact number.",
            "At most this many. 0 = no limit.",
            "Fewest steps from the start piece.",
            "Fewest steps between two pieces with the tag. 0 or 1 = may touch.",
            "Each piece with the tag at least this many steps deeper or shallower than the others, so they lead further in. 0 = no rule.",
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
                    new[] { "piece", "role", "weight", "minCount", "maxCount", "minDepth", "condition" }, PieceWidths),
                elementHeight = EditorGUIUtility.singleLineHeight + 4f,
            };
            tagList = new ReorderableList(serializedObject, serializedObject.FindProperty("tagRules"), true, true, true, true)
            {
                drawHeaderCallback = r => Header(r, TagColumns, TagWidths, TagTips, 14f),
                drawElementCallback = (r, i, _, _) => Row(r, tagList.serializedProperty.GetArrayElementAtIndex(i),
                    new[] { "tag", "minCount", "maxCount", "minDepth", "minSpacing", "depthStep" }, TagWidths),
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
            EditorGUILayout.PropertyField(settings.FindPropertyRelative("depthBias"));
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

            EditorGUILayout.LabelField(new GUIContent("Spacing", "Keeps every piece with any of a rule's tags a number of steps from every other, e.g. entrances, objectives and exits."), EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("spacingRules"), new GUIContent("Spacing Rules"), true);

            EditorGUILayout.LabelField(new GUIContent("Dead Ends", "Long dead ends must end in a piece worth reaching."), EditorStyles.boldLabel);
            var deadEnds = serializedObject.FindProperty("deadEnds");
            EditorGUILayout.PropertyField(deadEnds.FindPropertyRelative("minLength"), new GUIContent("Min Length", "Dead ends this many pieces long or more need one of the tags below. 0 = no rule."));
            EditorGUILayout.PropertyField(deadEnds.FindPropertyRelative("tags"), new GUIContent("Must End In"), true);

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
