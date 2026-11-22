using System;
using System.Collections.Generic;
using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// The pieces one kind of level is built from, how often each is used, and how big a layout is.
    /// </summary>
    [CreateAssetMenu(menuName = "Masonry/Piece Set", fileName = "Piece Set", order = 301)]
    public sealed class PieceSet : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public Piece piece;
            public PieceRole role = PieceRole.Normal;
            [Tooltip("Relative chance of being picked among the pieces that fit.")]
            [Min(0f)] public float weight = 1f;
            [Tooltip("Every layout has at least this many. Layouts that miss it are retried.")]
            [Min(0)] public int minCount;
            [Tooltip("Most of this piece in one layout. 0 = no limit.")]
            [Min(0)] public int maxCount;
            [Tooltip("Fewest steps from the start piece before this piece may appear. Use it to keep an exit far from the entrance.")]
            [Min(0)] public int minDepth;
        }

        [SerializeField] LayoutSettings settings = LayoutSettings.Default;
        [SerializeField] List<Entry> pieces = new();

        public LayoutSettings Settings
        {
            get => settings;
            set => settings = value;
        }

        public List<Entry> Pieces => pieces;

        /// <summary>Makes a generator for this set. Entries without a piece are skipped.</summary>
        public LayoutGenerator CreateGenerator(out List<Entry> used)
        {
            used = new List<Entry>();
            var types = new List<SocketType>();
            var definitions = new List<PieceDefinition>();

            int TypeId(SocketType t)
            {
                int i = types.IndexOf(t);
                if (i >= 0)
                    return i;
                types.Add(t);
                return types.Count - 1;
            }

            foreach (var e in pieces)
            {
                if (e == null || e.piece == null)
                    continue;
                var d = e.piece.ToDefinition(TypeId);
                d.role = e.role;
                d.weight = e.weight;
                d.minCount = e.minCount;
                d.maxCount = e.maxCount;
                d.minDepth = e.minDepth;
                definitions.Add(d);
                used.Add(e);
            }

            return new LayoutGenerator(definitions, settings, (a, b) => SocketType.CanJoin(types[a], types[b]));
        }
    }
}
