using System.Collections.Generic;
using UnityEngine;

namespace Masonry
{
    /// <summary>One level of a multi-level build: the set it came from and its layout, in the builder's space.</summary>
    public sealed class LevelLayout
    {
        public PieceSet Set { get; internal set; }
        /// <summary>The set's entries, indexed like <see cref="Layout.Pieces"/>.</summary>
        public List<PieceSet.Entry> Entries { get; internal set; }
        public LayoutGenerator Generator { get; internal set; }
        public Layout Layout { get; internal set; }
        /// <summary>0 for the level the build started from, 1 for levels it links to, and so on.</summary>
        public int Depth { get; internal set; }
        /// <summary>How many links led here (0 for the first level).</summary>
        public int Links { get; internal set; }
        /// <summary>Flags this level was built with (the build's flags plus "main" or "linked").</summary>
        public IReadOnlyCollection<string> Flags { get; internal set; }
    }

    /// <summary>
    /// Builds a level and every level its pieces link to (<see cref="LevelLink"/>), without instantiating anything.
    /// Each linked set is grown once from all links that point at it, with a seed derived from the main seed and its
    /// order, so the whole stack is still reproducible from one number.
    /// </summary>
    public static class LevelPlan
    {
        /// <param name="maxDepth">How many links deep to follow (guards against sets that link back to each other).</param>
        /// <param name="flags">Build flags for entry conditions (see <see cref="Masonry.Flags"/>). Every level also gets
        /// "main" or "linked".</param>
        public static List<LevelLayout> Generate(PieceSet set, int seed, IEnumerable<string> flags = null, int maxDepth = 4)
        {
            var levels = new List<LevelLayout>();
            if (set == null)
                return levels;

            var mainFlags = Masonry.Flags.With(flags, Masonry.Flags.Main);
            var linkedFlags = Masonry.Flags.With(flags, Masonry.Flags.Linked);

            var generator = set.CreateGenerator(out var entries, mainFlags);
            levels.Add(new LevelLayout
            {
                Set = set,
                Entries = entries,
                Generator = generator,
                Layout = generator.Generate(seed),
                Flags = mainFlags,
            });

            // Breadth-first: every level links onwards once it is built.
            var done = new HashSet<PieceSet> { set };
            for (int i = 0; i < levels.Count; i++)
            {
                var from = levels[i];
                if (from.Depth >= maxDepth)
                    continue;

                // Links in placement order, grouped by target in order of first appearance.
                var order = new List<PieceSet>();
                var starts = new Dictionary<PieceSet, List<(Vector3 position, float yaw, Piece arrival)>>();
                foreach (var placement in from.Layout.Placements)
                {
                    var prefab = from.Entries[placement.piece].piece;
                    foreach (var link in prefab.GetComponentsInChildren<LevelLink>(true))
                    {
                        if (link.Target == null || done.Contains(link.Target))
                            continue;
                        var t = link.transform;
                        var local = prefab.transform.InverseTransformPoint(t.position);
                        var forward = prefab.transform.InverseTransformDirection(t.forward);
                        float yaw = placement.yaw + Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
                        if (!starts.TryGetValue(link.Target, out var list))
                        {
                            starts[link.Target] = list = new List<(Vector3, float, Piece)>();
                            order.Add(link.Target);
                        }
                        list.Add((placement.position + placement.Rotation * local, yaw, link.Arrival));
                    }
                }

                foreach (var target in order)
                {
                    done.Add(target);
                    var targetGenerator = target.CreateGenerator(out var targetEntries, linkedFlags);
                    var layoutStarts = new List<LayoutStart>();
                    foreach (var (position, yaw, arrival) in starts[target])
                        layoutStarts.Add(new LayoutStart(position, yaw, arrival != null ? targetEntries.FindIndex(e => e.piece == arrival) : -1));

                    levels.Add(new LevelLayout
                    {
                        Set = target,
                        Entries = targetEntries,
                        Generator = targetGenerator,
                        Layout = targetGenerator.Generate(LevelSeed(seed, levels.Count), layoutStarts),
                        Depth = from.Depth + 1,
                        Links = layoutStarts.Count,
                        Flags = linkedFlags,
                    });
                }
            }
            return levels;
        }

        /// <summary>Seed of the n-th level of a build: the same main seed always gives the same stack.</summary>
        public static int LevelSeed(int seed, int level) => level == 0 ? seed : new Rng(seed ^ (level * 0x5BD1E995)).NextInt();
    }
}
