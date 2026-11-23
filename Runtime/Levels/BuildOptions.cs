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

        [Serializable]
        public struct TagCount
        {
            public PieceTag tag;
            [Min(0)] public int minCount;
            [Tooltip("0 = no limit.")]
            [Min(0)] public int maxCount;

            public TagCount(PieceTag tag, int minCount, int maxCount)
            {
                this.tag = tag;
                this.minCount = minCount;
                this.maxCount = maxCount;
            }
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
