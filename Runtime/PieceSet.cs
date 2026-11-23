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
            [Tooltip("Only use this piece when the build has this flag; '!flag' = only when it does not. Several, comma separated, must all hold. Empty = always. Every build has 'main' (the level the build starts from) or 'linked' (a level reached through a Level Link), plus any flags the game passes in.")]
            public string condition;

            /// <summary>Whether this entry takes part in a build with these flags.</summary>
            public bool Applies(ICollection<string> flags) => Flags.Match(condition, flags);
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
            [Tooltip("Pieces with the tag must be at least this many steps deeper or shallower than each other, so each one is further in than the last. 0 = no rule.")]
            [Min(0)] public int depthStep;
        }

        /// <summary>Keeps every piece carrying any of these tags apart from every other.</summary>
        [Serializable]
        public sealed class SpacingRuleEntry
        {
            [Tooltip("Pieces with any of these tags (e.g. Entrance, Objective, Exit).")]
            public List<PieceTag> tags = new();
            [Tooltip("Fewest steps between any two of them.")]
            [Min(0)] public int minSteps = 3;
        }

        /// <summary>Long dead ends must end in a piece with one of these tags.</summary>
        [Serializable]
        public sealed class DeadEndEntry
        {
            [Tooltip("Dead ends whose corridor (back to the nearest junction or start) is at least this many pieces long must end in a tagged piece. 0 = no rule.")]
            [Min(0)] public int minLength;
            [Tooltip("Tags that count as something worth reaching: an objective, a reward, a hazard, an exit...")]
            public List<PieceTag> tags = new();
        }

        /// <summary>The boundary between two module types, or between one and the edge of the layout.</summary>
        [Serializable]
        public sealed class BoundaryRule
        {
            public ModuleType a;
            [Tooltip("Empty = the edge of the layout (nothing on the other side).")]
            public ModuleType b;
            [Tooltip("Empty = no boundary: the two run into each other.")]
            public BoundaryStyle style;
        }

        [SerializeField] LayoutSettings settings = LayoutSettings.Default;
        [Tooltip("Boundary for meetings no rule covers. Empty = none.")]
        [SerializeField] BoundaryStyle defaultBoundary;
        [SerializeField] List<BoundaryRule> boundaryRules = new();
        [Tooltip("Cell size the pieces are designed on, in metres. Only used by the editor tools (footprints, socket placement). 0 = no grid.")]
        [SerializeField, Min(0f)] float gridSize;
        [SerializeField] List<Entry> pieces = new();
        [SerializeField] List<TagRuleEntry> tagRules = new();
        [SerializeField] List<SpacingRuleEntry> spacingRules = new();
        [SerializeField] DeadEndEntry deadEnds = new();

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
        public List<SpacingRuleEntry> SpacingRules => spacingRules;
        public DeadEndEntry DeadEnds => deadEnds ??= new DeadEndEntry();
        public List<BoundaryRule> BoundaryRules => boundaryRules;

        public BoundaryStyle DefaultBoundary
        {
            get => defaultBoundary;
            set => defaultBoundary = value;
        }

        /// <summary>
        /// The boundary between module types a and b; with <paramref name="outside"/>, between a and the edge of the
        /// layout. Rules match in either order; the first match wins; otherwise the default.
        /// </summary>
        public BoundaryStyle BoundaryFor(ModuleType a, ModuleType b, bool outside)
        {
            foreach (var r in boundaryRules)
            {
                if (r == null)
                    continue;
                if (outside)
                {
                    if (r.b == null && r.a == a)
                        return r.style;
                }
                else if ((r.a == a && r.b == b) || (r.a == b && r.b == a))
                {
                    return r.style;
                }
            }
            return defaultBoundary;
        }

        /// <summary>Makes a generator for this set. Entries without a piece are skipped.</summary>
        public LayoutGenerator CreateGenerator(out List<Entry> used) => CreateGenerator(out used, out _);

        /// <summary>Makes a generator for this set with only the entries whose condition holds for these flags.</summary>
        public LayoutGenerator CreateGenerator(out List<Entry> used, ICollection<string> flags) => CreateGenerator(out used, out _, flags);

        /// <summary>Makes a generator for this set, also returning the tag behind each tag id.</summary>
        /// <param name="overrides">Optional piece count and tag count overrides (the first level of a build).</param>
        public LayoutGenerator CreateGenerator(out List<Entry> used, out List<PieceTag> tags, ICollection<string> flags = null, BuildOptions overrides = null)
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
            var overridden = new HashSet<PieceTag>();
            if (overrides != null)
                foreach (var o in overrides.tagCounts)
                    if (o.tag != null)
                        overridden.Add(o.tag);
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
                    depthStep = r.depthStep,
                });
                if (overridden.Contains(r.tag))
                {
                    var o = overrides.tagCounts.Find(x => x.tag == r.tag);
                    var last = rules[rules.Count - 1];
                    last.minCount = o.minCount;
                    last.maxCount = o.maxCount;
                    rules[rules.Count - 1] = last;
                    overridden.Remove(r.tag);
                }
            }
            foreach (var tag in overridden)
            {
                var o = overrides.tagCounts.Find(x => x.tag == tag);
                rules.Add(new TagRule { tag = TagId(tag), minCount = o.minCount, maxCount = o.maxCount });
            }

            var layoutSettings = settings;
            if (overrides != null && overrides.minPieces > 0)
                layoutSettings.minPieces = overrides.minPieces;
            if (overrides != null && overrides.maxPieces > 0)
                layoutSettings.maxPieces = Mathf.Max(overrides.maxPieces, layoutSettings.minPieces);

            int[] TagIds(List<PieceTag> list)
            {
                var ids = new List<int>();
                if (list != null)
                    foreach (var t in list)
                        if (t != null && !ids.Contains(TagId(t)))
                            ids.Add(TagId(t));
                return ids.ToArray();
            }

            var spacing = new List<SpacingRule>();
            foreach (var r in spacingRules)
                if (r != null)
                    spacing.Add(new SpacingRule { tags = TagIds(r.tags), minSteps = r.minSteps });
            var deadEndRule = new DeadEndRule { minLength = DeadEnds.minLength, tags = TagIds(DeadEnds.tags) };

            foreach (var e in pieces)
            {
                if (e == null || e.piece == null || !e.Applies(flags))
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
            return new LayoutGenerator(definitions, layoutSettings, (a, b) => SocketType.CanJoin(types[a], types[b]), rules, spacing, deadEndRule);
        }
    }
}
