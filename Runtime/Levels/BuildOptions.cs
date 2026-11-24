using System;
using System.Collections.Generic;
using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// Per-build choices layered on top of a Piece Set's own rules, for games that reuse one set across many levels: build
    /// flags, how many linked levels deep to go, and overrides for the first level's size and tag counts. Every
    /// machine must use the same options (and seed) to get the same layout.
    /// </summary>
    [Serializable]
    public sealed class BuildOptions
    {
        [Tooltip("Build flags for entry conditions, e.g. exit-gate.")]
        public List<string> flags = new();

        [Tooltip("How many linked levels deep to build: 0 = none, 1 = the levels the first one links to, and so on. Levels that may still link further get the 'links' flag, so link pieces can require it.")]
        [Range(0, 8)] public int maxLinkDepth = 4;

        [Tooltip("Piece count for the first level. 0 = the set's own.")]
        [Min(0)] public int minPieces;
        [Min(0)] public int maxPieces;

        [Tooltip("Depth bias for the first level (see LayoutSettings). Negative = the set's own.")]
        [Range(-1f, 1f)] public float depthBias = -1f;
        [Tooltip("Tries for the first level. 0 = the set's own.")]
        [Min(0)] public int attempts;

        [Tooltip("Exact or ranged counts for tags on the first level, replacing the set's rule for that tag.")]
        public List<TagCount> tagCounts = new();

        [Tooltip("Tag counts for linked levels built from a given set (objectives in the crypt below, say), replacing that set's rule for those tags.")]
        public List<LinkedTagCounts> linkedTagCounts = new();

        [Serializable]
        public struct TagCount
        {
            public PieceTag tag;
            [Min(0)] public int minCount;
            [Tooltip("0 = no limit, -1 = none at all.")]
            [Min(-1)] public int maxCount;

            public TagCount(PieceTag tag, int minCount, int maxCount)
            {
                this.tag = tag;
                this.minCount = minCount;
                this.maxCount = maxCount;
            }

            /// <summary>Exactly <paramref name="count"/> pieces with the tag (0 = none).</summary>
            public static TagCount Exactly(PieceTag tag, int count) => new(tag, Mathf.Max(0, count), count > 0 ? count : -1);
        }

        /// <summary>Tag counts for every linked level built from one set.</summary>
        [Serializable]
        public struct LinkedTagCounts
        {
            public PieceSet set;
            public List<TagCount> tagCounts;

            public LinkedTagCounts(PieceSet set, List<TagCount> tagCounts)
            {
                this.set = set;
                this.tagCounts = tagCounts;
            }
        }

        /// <summary>Overrides for a linked level built from <paramref name="set"/> (tag counts only), or null.</summary>
        public BuildOptions ForLinked(PieceSet set)
        {
            BuildOptions linked = null;
            foreach (var l in linkedTagCounts)
                if (l.set == set && l.tagCounts != null)
                {
                    linked ??= new BuildOptions();
                    linked.tagCounts.AddRange(l.tagCounts);
                }
            return linked;
        }

        public BuildOptions() { }

        public BuildOptions(IEnumerable<string> flags, int maxLinkDepth = 4)
        {
            if (flags != null)
                this.flags.AddRange(flags);
            this.maxLinkDepth = maxLinkDepth;
        }
    }
}
