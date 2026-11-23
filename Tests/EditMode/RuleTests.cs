using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Masonry.Tests
{
    sealed class RuleTests
    {
        const float Half = 5f;
        const int Objective = 0, Reward = 1, Exit = 2, Entrance = 3;

        static SocketDefinition North => new(new Vector3(0f, 0f, Half), 0f);
        static SocketDefinition South => new(new Vector3(0f, 0f, -Half), 180f);
        static SocketDefinition East => new(new Vector3(Half, 0f, 0f), 90f);
        static SocketDefinition West => new(new Vector3(-Half, 0f, 0f), 270f);

        static PieceDefinition Cell(string name, PieceRole role, float weight, int[] tags, params SocketDefinition[] sockets) => new()
        {
            name = name,
            role = role,
            weight = weight,
            tags = tags ?? System.Array.Empty<int>(),
            bounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(10f, 4f, 10f)),
            sockets = sockets,
        };

        static List<PieceDefinition> Pieces() => new()
        {
            Cell("Gate", PieceRole.Start, 1f, new[] { Entrance }, North),
            Cell("Path", PieceRole.Normal, 3f, null, North, South),
            Cell("Corner", PieceRole.Normal, 2f, null, South, East),
            Cell("Tee", PieceRole.Normal, 1.5f, null, South, East, West),
            Cell("Objective", PieceRole.Normal, 1f, new[] { Objective }, North, South),
            new PieceDefinition
            {
                name = "Exit",
                role = PieceRole.Normal,
                weight = 0.3f,
                minCount = 1,
                maxCount = 1,
                minDepth = 4,
                tags = new[] { Exit },
                bounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(10f, 4f, 10f)),
                sockets = new[] { South },
            },
            Cell("Plain End", PieceRole.Cap, 1f, null, South),
            Cell("Treasure", PieceRole.Cap, 1f, new[] { Reward }, South),
        };

        static LayoutSettings Settings => new() { minPieces = 14, maxPieces = 20, attempts = 30, overlapTolerance = 0.05f, joinDistance = 0.05f };
        static readonly TagRule[] Objectives = { new() { tag = Objective, minCount = 3, maxCount = 3, minDepth = 2, depthStep = 2 } };
        static readonly SpacingRule[] KeyTiles = { new() { tags = new[] { Objective, Exit, Entrance }, minSteps = 3 } };
        static readonly DeadEndRule DeadEnds = new() { minLength = 2, tags = new[] { Objective, Reward, Exit, Entrance } };

        static LayoutGenerator Generator() => new(Pieces(), Settings, null, Objectives, KeyTiles, DeadEnds);

        [Test]
        public void LongDeadEndsEndInSomething()
        {
            int valid = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                var layout = Generator().Generate(seed);
                if (!layout.Valid)
                    continue;
                valid++;
                Assert.AreEqual(0, layout.DeadEndViolations);
                foreach (var d in layout.DeadEnds)
                    if (d.length >= 2)
                        Assert.IsTrue(d.hasContent, $"seed {seed}: dead end {d.placement} ({d.length} long) is empty");
            }
            Assert.Greater(valid, 50);
        }

        [Test]
        public void KeyTilesKeepTheirDistance()
        {
            var pieces = Pieces();
            for (int seed = 0; seed < 40; seed++)
            {
                var layout = Generator().Generate(seed);
                var key = new List<int>();
                for (int i = 0; i < layout.Placements.Count; i++)
                    if (pieces[layout.Placements[i].piece].HasAnyTag(KeyTiles[0].tags))
                        key.Add(i);
                var adjacency = Adjacency(layout);
                foreach (int a in key)
                {
                    var steps = Steps(adjacency, a);
                    foreach (int b in key)
                        if (a != b)
                            Assert.GreaterOrEqual(steps[b], 3, $"seed {seed}: {pieces[layout.Placements[a].piece].name} and {pieces[layout.Placements[b].piece].name}");
                }
            }
        }

        [Test]
        public void ObjectivesAreStaggeredInDepth()
        {
            var pieces = Pieces();
            for (int seed = 0; seed < 40; seed++)
            {
                var layout = Generator().Generate(seed);
                var depths = new List<int>();
                foreach (var p in layout.Placements)
                    if (pieces[p.piece].HasTag(Objective))
                        depths.Add(p.depth);
                for (int i = 0; i < depths.Count; i++)
                    for (int k = i + 1; k < depths.Count; k++)
                        Assert.GreaterOrEqual(Mathf.Abs(depths[i] - depths[k]), 2, $"seed {seed}");
            }
        }

        static List<List<int>> Adjacency(Layout layout)
        {
            var adjacency = new List<List<int>>();
            for (int i = 0; i < layout.Placements.Count; i++)
                adjacency.Add(new List<int>());
            foreach (var c in layout.Connections)
            {
                adjacency[c.a.placement].Add(c.b.placement);
                adjacency[c.b.placement].Add(c.a.placement);
            }
            return adjacency;
        }

        static int[] Steps(List<List<int>> adjacency, int from)
        {
            var distance = new int[adjacency.Count];
            for (int i = 0; i < distance.Length; i++)
                distance[i] = int.MaxValue;
            distance[from] = 0;
            var queue = new Queue<int>();
            queue.Enqueue(from);
            while (queue.Count > 0)
            {
                int at = queue.Dequeue();
                foreach (int n in adjacency[at])
                    if (distance[n] == int.MaxValue)
                    {
                        distance[n] = distance[at] + 1;
                        queue.Enqueue(n);
                    }
            }
            return distance;
        }
    }
}
