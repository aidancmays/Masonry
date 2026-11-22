using UnityEditor;
using UnityEngine;

namespace Masonry.Editor
{
    [CustomEditor(typeof(LayoutBuilder))]
    sealed class LayoutBuilderEditor : UnityEditor.Editor
    {
        string SeedKey => $"Masonry.PreviewSeed.{target.GetInstanceID()}";

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var builder = (LayoutBuilder)target;
            var set = builder.PieceSet;
            if (set != null)
            {
                foreach (var e in set.Pieces)
                {
                    if (e?.piece == null)
                        continue;
                    var problems = PieceEditor.Validate(e.piece);
                    if (problems.Count > 0)
                        EditorGUILayout.HelpBox($"{e.piece.name}: {problems[0]}" + (problems.Count > 1 ? $" (+{problems.Count - 1} more)" : ""), MessageType.Warning);
                }
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);

            int seed = SessionState.GetInt(SeedKey, 1);
            seed = EditorGUILayout.IntField("Seed", seed);
            SessionState.SetInt(SeedKey, seed);

            using (new EditorGUI.DisabledScope(set == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Build"))
                    builder.BuildPreview(seed);
                if (GUILayout.Button("Next Seed"))
                {
                    seed++;
                    SessionState.SetInt(SeedKey, seed);
                    builder.BuildPreview(seed);
                }
                if (GUILayout.Button("Random Seed"))
                {
                    seed = Random.Range(int.MinValue, int.MaxValue);
                    SessionState.SetInt(SeedKey, seed);
                    builder.BuildPreview(seed);
                }
                if (GUILayout.Button("Clear"))
                    builder.Clear();
            }

            var layout = builder.Layout;
            if (layout != null)
            {
                string message = $"Seed {layout.Seed}, try {layout.Attempt + 1}: {layout.Placements.Count} pieces, " +
                    $"{layout.Connections.Count} joins, {layout.OpenSockets.Count} open sockets.";
                EditorGUILayout.HelpBox(layout.Valid ? message : message + " Minimum counts not met.",
                    layout.Valid ? MessageType.Info : MessageType.Warning);
            }
            if (!Application.isPlaying)
                EditorGUILayout.HelpBox("Previews are not saved with the scene and are cleared when entering Play mode.", MessageType.None);
        }
    }

    /// <summary>Removes edit-mode previews before Play mode, so they never mix with a real build.</summary>
    [InitializeOnLoad]
    static class LayoutPreviewCleanup
    {
        static LayoutPreviewCleanup() => EditorApplication.playModeStateChanged += OnPlayModeChanged;

        static void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingEditMode)
                return;
            foreach (var builder in Object.FindObjectsByType<LayoutBuilder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                builder.Clear();
        }
    }
}
