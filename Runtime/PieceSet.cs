using System;
using System.Collections.Generic;
using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// The rules for one kind of level: which pieces it is built from, how often each is used, how many of each kind
    /// of piece it must have, and how big it is. Edit it in the inspector or in Window > Masonry > Workbench, which
    /// previews many seeds at once.
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

        /// <summary>Counts for every piece carrying a tag, whichever piece it is.</summary>
        [Serializable]
        public sealed class TagRuleEntry
        {
            public PieceTag tag;
            [Tooltip("Every layout has at least this many pieces with the tag. Set Min and Max equal for an exact number.")]
            [Min(0)] public int minCount;
            [Tooltip("Most pieces with the tag in one layout. 0 = no limit.")]
            [Min(0)] public int maxCount;
            [Tooltip("Fewest steps from the start piece.")]
            [Min(0)] public int minDepth;
            [Tooltip("Fewest steps between two pieces with the tag. 0 or 1 = they may touch.")]
            [Min(0)] public int minSpacing;
        }

        [SerializeField] LayoutSettings settings = LayoutSettings.Default;
        [Tooltip("Cell size the pieces are designed on, in metres. Only used by the editor tools (footprints, socket placement). 0 = no grid.")]
        [SerializeField, Min(0f)] float gridSize;
        [SerializeField] List<Entry> pieces = new();
        [SerializeField] List<TagRuleEntry> tagRules = new();

        public LayoutSettings Settings
        {
            get => settings;
            set => settings = value;
        }

        public float GridSize
        {
            get => gridSize;
            set => gridSize = value;
        }

        public List<Entry> Pieces => pieces;
        public List<TagRuleEntry> TagRules => tagRules;

        /// <summary>Makes a generator for this set. Entries without a piece are skipped.</summary>
        public LayoutGenerator CreateGenerator(out List<Entry> used) => CreateGenerator(out used, out _);

        /// <summary>Makes a generator for this set, also returning the tag behind each tag id.</summary>
        public LayoutGenerator CreateGenerator(out List<Entry> used, out List<PieceTag> tags)
        {
            used = new List<Entry>();
            var types = new List<SocketType>();
            var tagList = new List<PieceTag>();
            var definitions = new List<PieceDefinition>();

            int TypeId(SocketType t)
            {
                int i = types.IndexOf(t);
                if (i >= 0)
                    return i;
                types.Add(t);
                return types.Count - 1;
            }

            int TagId(PieceTag t)
            {
                int i = tagList.IndexOf(t);
                if (i >= 0)
                    return i;
                tagList.Add(t);
                return tagList.Count - 1;
            }

            var rules = new List<TagRule>();
            foreach (var r in tagRules)
            {
                if (r?.tag == null)
                    continue;
                rules.Add(new TagRule
                {
                    tag = TagId(r.tag),
                    minCount = r.minCount,
                    maxCount = r.maxCount,
                    minDepth = r.minDepth,
                    minSpacing = r.minSpacing,
                });
            }

            foreach (var e in pieces)
            {
                if (e == null || e.piece == null)
                    continue;
                var d = e.piece.ToDefinition(TypeId, TagId);
                d.role = e.role;
                d.weight = e.weight;
                d.minCount = e.minCount;
                d.maxCount = e.maxCount;
                d.minDepth = e.minDepth;
                definitions.Add(d);
                used.Add(e);
            }

            tags = tagList;
            return new LayoutGenerator(definitions, settings, (a, b) => SocketType.CanJoin(types[a], types[b]), rules);
        }
    }
}
