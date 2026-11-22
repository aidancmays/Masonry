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

        /// <summary>The last layout built, or null.</summary>
        public Layout Layout { get; private set; }

        /// <summary>Pieces from the last build, indexed like <see cref="Masonry.Layout.Placements"/>.</summary>
        public IReadOnlyList<Piece> Pieces => built;

        /// <summary>Raised after a build, once every piece is placed and every socket knows if it was joined.</summary>
        public event Action<LayoutBuilder> Built;

        /// <summary>Raised after the pieces of a build are removed.</summary>
        public event Action<LayoutBuilder> Cleared;

        /// <summary>Removes any previous build and builds the layout for this seed.</summary>
        public Layout Build(int seed) => Build(seed, preview: false);

        /// <summary>Like <see cref="Build"/>, but the pieces are never saved with the scene. Used by the inspector.</summary>
        public Layout BuildPreview(int seed) => Build(seed, preview: true);

        Layout Build(int seed, bool preview)
        {
            Clear();
            if (pieceSet == null)
            {
                Debug.LogError($"[Masonry] {name} has no Piece Set.", this);
                return null;
            }

            var generator = pieceSet.CreateGenerator(out var entries);
            var layout = generator.Generate(seed);
            if (!layout.Valid)
                Debug.LogWarning($"[Masonry] {name}: seed {seed} could not meet the Piece Set's minimum counts in {pieceSet.Settings.attempts} tries; built the closest layout.", this);

            foreach (var placement in layout.Placements)
            {
                var prefab = entries[placement.piece].piece;
                var instance = Instantiate(prefab, transform);
                instance.transform.SetLocalPositionAndRotation(placement.position, placement.Rotation);
                instance.name = $"{prefab.name} ({built.Count})";
                instance.PlacementIndex = built.Count;
                instance.Depth = placement.depth;
                if (preview)
                    SetHideFlags(instance.transform, HideFlags.DontSave);
                built.Add(instance);
            }

            foreach (var piece in built)
                foreach (var socket in piece.Sockets)
                    socket.SetJoined(null);
            foreach (var c in layout.Connections)
            {
                var a = SocketAt(c.a);
                var b = SocketAt(c.b);
                a.SetJoined(b);
                b.SetJoined(a);
            }

            Layout = layout;
            Built?.Invoke(this);
            return layout;
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
            Layout = null;
            if (had)
                Cleared?.Invoke(this);
        }

        Socket SocketAt(SocketRef r) => built[r.placement].Sockets[r.socket];

        static void SetHideFlags(Transform t, HideFlags flags)
        {
            t.gameObject.hideFlags = flags;
            for (int i = 0; i < t.childCount; i++)
                SetHideFlags(t.GetChild(i), flags);
        }
    }
}
