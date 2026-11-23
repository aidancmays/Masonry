using System;
using System.Collections.Generic;
using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// Builds a layout from a <see cref="PieceSet"/> under this transform. Every machine that calls
    /// <see cref="Build"/> with the same set and seed gets the same pieces in the same places, so a multiplayer game
    /// only needs to share the seed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LayoutBuilder : MonoBehaviour
    {
        [SerializeField] PieceSet pieceSet;

        readonly List<Piece> built = new();

        public PieceSet PieceSet
        {
            get => pieceSet;
            set => pieceSet = value;
        }

        /// <summary>The last layout built for this builder's own set (the first level), or null.</summary>
        public Layout Layout { get; private set; }

        /// <summary>Every level of the last build: this builder's set first, then the levels its pieces link to.</summary>
        public IReadOnlyList<LevelLayout> Levels => levels;

        /// <summary>Pieces from the last build, every level, in order. <see cref="Piece.Level"/> says which level.</summary>
        public IReadOnlyList<Piece> Pieces => built;

        /// <summary>Raised after a build, once every piece is placed and every socket knows if it was joined.</summary>
        public event Action<LayoutBuilder> Built;

        /// <summary>Raised after the pieces of a build are removed.</summary>
        public event Action<LayoutBuilder> Cleared;

        readonly List<LevelLayout> levels = new();

        /// <summary>Flags of the last build (see <see cref="Masonry.Flags"/>).</summary>
        public IReadOnlyCollection<string> Flags { get; private set; } = Array.Empty<string>();

        /// <summary>Removes any previous build and builds the layout for this seed, with every linked level.</summary>
        public Layout Build(int seed) => Build(seed, null, preview: false);

        /// <summary>
        /// Like <see cref="Build(int)"/>, with flags that Piece Set entries can require in their condition, such as
        /// which exit this floor uses. Every machine must pass the same flags to get the same layout.
        /// </summary>
        public Layout Build(int seed, IEnumerable<string> flags) => Build(seed, flags, preview: false);

        /// <summary>Like <see cref="Build(int)"/>, but the pieces are never saved with the scene. Used by the editor.</summary>
        public Layout BuildPreview(int seed, IEnumerable<string> flags = null) => Build(seed, flags, preview: true);

        Layout Build(int seed, IEnumerable<string> flags, bool preview)
        {
            Clear();
            if (pieceSet == null)
            {
                Debug.LogError($"[Masonry] {name} has no Piece Set.", this);
                return null;
            }

            Flags = Masonry.Flags.With(flags, null);
            levels.AddRange(LevelPlan.Generate(pieceSet, seed, flags));
            for (int l = 0; l < levels.Count; l++)
            {
                var level = levels[l];
                var layout = level.Layout;
                if (!layout.Valid)
                    Debug.LogWarning($"[Masonry] {name}: {level.Set.name} (seed {layout.Seed}) could not meet its rules in {level.Set.Settings.attempts} tries; built the closest layout.", this);

                var pieces = new List<Piece>();
                foreach (var placement in layout.Placements)
                {
                    var prefab = level.Entries[placement.piece].piece;
                    var instance = Instantiate(prefab, transform);
                    instance.transform.SetLocalPositionAndRotation(placement.position, placement.Rotation);
                    instance.name = l == 0 ? $"{prefab.name} ({pieces.Count})" : $"{level.Set.name} / {prefab.name} ({pieces.Count})";
                    instance.PlacementIndex = pieces.Count;
                    instance.Depth = placement.depth;
                    instance.Level = l;
                    if (preview)
                        SetHideFlags(instance.transform, HideFlags.DontSave);
                    pieces.Add(instance);
                }

                foreach (var piece in pieces)
                    foreach (var socket in piece.Sockets)
                        socket.SetJoined(null);
                foreach (var c in layout.Connections)
                {
                    var a = pieces[c.a.placement].Sockets[c.a.socket];
                    var b = pieces[c.b.placement].Sockets[c.b.socket];
                    a.SetJoined(b);
                    b.SetJoined(a);
                }

                BoundaryResolver.Resolve(pieces, level.Set);
                built.AddRange(pieces);
            }

            Layout = levels[0].Layout;
            Built?.Invoke(this);
            return Layout;
        }

        /// <summary>Removes every piece under this transform, including previews.</summary>
        public void Clear()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (!child.TryGetComponent<Piece>(out _))
                    continue;
                // Deactivate first so OnDisable runs now, not at the end of the frame (e.g. spawn point registries).
                child.gameObject.SetActive(false);
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }

            bool had = built.Count > 0 || Layout != null;
            built.Clear();
            levels.Clear();
            Layout = null;
            if (had)
                Cleared?.Invoke(this);
        }

        static void SetHideFlags(Transform t, HideFlags flags)
        {
            t.gameObject.hideFlags = flags;
            for (int i = 0; i < t.childCount; i++)
                SetHideFlags(t.GetChild(i), flags);
        }
    }
}
