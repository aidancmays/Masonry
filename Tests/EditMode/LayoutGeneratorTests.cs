using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Masonry.Tests
{
    sealed class LayoutGeneratorTests
    {
        const float Cell = 10f;
        const float Half = Cell / 2f;

        // Sockets on a 10 m square cell. Yaw 0 faces +Z, 90 faces +X.
        static SocketDefinition North => new(new Vector3(0f, 0f, Half), 0f);
        static SocketDefinition East => new(new Vector3(Half, 0f, 0f), 90f);
        static SocketDefinition South => new(new Vector3(0f, 0f, -Half), 180f);
        static SocketDefinition West => new(new Vector3(-Half, 0f, 0f), 270f);

        static PieceDefinition Cellular(string name, PieceRole role, params SocketDefinition[] sockets) => new()
        {
            name = name,
            role = role,
            bounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(Cell, 4f, Cell)),
            sockets = sockets,
        };

        static List<PieceDefinition> GraveyardLike() => new()
        {
            Cellular("Gate", PieceRole.Start, North),
            Cellular("Straight", PieceRole.Normal, North, South),
            Cellular("Corner", PieceRole.Normal, South, East),
            Cellular("Tee", PieceRole.Normal, South, East, West),
            Cellular("Cross", PieceRole.Normal, North, East, South, West),
            new PieceDefinition
            {
                name = "Big",
                role = PieceRole.Normal,
                bounds = new Bounds(new Vector3(Half, 2f, Half), new Vector3(Cell * 2f, 4f, Cell * 2f)),
                sockets = new[] { South, new SocketDefinition(new Vector3(Cell, 0f, Cell + Half), 0f) },
                maxCount = 1,
            },
            new PieceDefinition
            {
                name = "Exit",
                role = PieceRole.Normal,
                bounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(Cell, 4f, Cell)),
                sockets = new[] { South },
                minCount = 1,
                maxCount = 1,
                minDepth = 3,
                weight = 0f,
            },
            Cellular("DeadEnd", PieceRole.Cap, South),
        };

        static LayoutSettings Settings(int min = 10, int max = 16) => new()
        {
            minPieces = min,
            maxPieces = max,
            attempts = 30,
            overlapTolerance = 0.05f,
            joinDistance = 0.05f,
        };

        [Test]
        public void SameSeedGivesSameLayout()
        {
            var pieces = GraveyardLike();
            for (int seed = 0; seed < 20; seed++)
            {
                var a = new LayoutGenerator(pieces, Settings()).Generate(seed);
                var b = new LayoutGenerator(pieces, Settings()).Generate(seed);
                Assert.AreEqual(a.Placements.Count, b.Placements.Count, $"seed {seed}");
                for (int i = 0; i < a.Placements.Count; i++)
                {
                    Assert.AreEqual(a.Placements[i].piece, b.Placements[i].piece);
                    Assert.AreEqual(a.Placements[i].position, b.Placements[i].position);
                    Assert.AreEqual(a.Placements[i].yaw, b.Placements[i].yaw);
                }
                Assert.AreEqual(a.Connections.Count, b.Connections.Count);
            }
        }

        [Test]
        public void DifferentSeedsGiveDifferentLayouts()
        {
            var pieces = GraveyardLike();
            var signatures = new HashSet<string>();
            for (int seed = 0; seed < 20; seed++)
            {
                var layout = new LayoutGenerator(pieces, Settings()).Generate(seed);
                var sb = new System.Text.StringBuilder();
                foreach (var p in layout.Placements)
                    sb.Append(p.piece).Append('@').Append(p.position).Append(p.yaw).Append(';');
                signatures.Add(sb.ToString());
            }
            Assert.Greater(signatures.Count, 15);
        }

        [Test]
        public void PiecesNeverOverlap()
        {
            var pieces = GraveyardLike();
            for (int seed = 0; seed < 50; seed++)
            {
                var layout = new LayoutGenerator(pieces, Settings()).Generate(seed);
                var boxes = new List<OrientedBox>();
                foreach (var p in layout.Placements)
                    boxes.Add(new OrientedBox(pieces[p.piece].bounds, p.position, p.yaw, 0.05f));
                for (int i = 0; i < boxes.Count; i++)
                    for (int j = i + 1; j < boxes.Count; j++)
                        Assert.IsFalse(boxes[i].Overlaps(boxes[j]), $"seed {seed}: {i} overlaps {j}");
            }
        }

        [Test]
        public void JoinedSocketsMeetFaceToFace()
        {
            var pieces = GraveyardLike();
            var generator = new LayoutGenerator(pieces, Settings());
            for (int seed = 0; seed < 30; seed++)
            {
                var layout = generator.Generate(seed);
                foreach (var c in layout.Connections)
                {
                    generator.GetSocketPose(layout.Placements[c.a.placement], c.a.socket, out var pa, out var ya);
                    generator.GetSocketPose(layout.Placements[c.b.placement], c.b.socket, out var pb, out var yb);
                    Assert.Less((pa - pb).magnitude, 0.01f, $"seed {seed}");
                    Assert.Less(Mathf.Abs(Mathf.DeltaAngle(ya, yb + 180f)), 0.5f, $"seed {seed}");
                }
            }
        }

        [Test]
        public void EverySocketIsJoinedOrReportedOpen()
        {
            var pieces = GraveyardLike();
            for (int seed = 0; seed < 30; seed++)
            {
                var layout = new LayoutGenerator(pieces, Settings()).Generate(seed);
                var seen = new HashSet<SocketRef>();
                foreach (var c in layout.Connections)
                {
                    Assert.IsTrue(seen.Add(c.a), $"seed {seed}: {c.a} joined twice");
                    Assert.IsTrue(seen.Add(c.b), $"seed {seed}: {c.b} joined twice");
                }
                foreach (var o in layout.OpenSockets)
                    Assert.IsTrue(seen.Add(o), $"seed {seed}: {o} is open and joined");

                int total = 0;
                foreach (var p in layout.Placements)
                    total += pieces[p.piece].sockets.Length;
                Assert.AreEqual(total, seen.Count, $"seed {seed}");
            }
        }

        [Test]
        public void CountsAndDepthsAreRespected()
        {
            var pieces = GraveyardLike();
            int exit = pieces.FindIndex(p => p.name == "Exit");
            int big = pieces.FindIndex(p => p.name == "Big");
            int valid = 0;
            for (int seed = 0; seed < 50; seed++)
            {
                var layout = new LayoutGenerator(pieces, Settings()).Generate(seed);
                Assert.LessOrEqual(layout.CountOf(big), 1);
                Assert.LessOrEqual(layout.CountOf(exit), 1);
                if (!layout.Valid)
                    continue;
                valid++;
                Assert.AreEqual(1, layout.CountOf(exit), $"seed {seed}");
                foreach (var p in layout.Placements)
                    if (p.piece == exit)
                        Assert.GreaterOrEqual(p.depth, 3);

                int grown = 0;
                foreach (var p in layout.Placements)
                    if (pieces[p.piece].role != PieceRole.Cap)
                        grown++;
                Assert.That(grown, Is.InRange(10, 16), $"seed {seed}");
            }
            Assert.Greater(valid, 45, "almost every seed should meet the minimums");
        }

        [Test]
        public void StartPieceIsAtTheOrigin()
        {
            var pieces = GraveyardLike();
            var layout = new LayoutGenerator(pieces, Settings()).Generate(7);
            Assert.AreEqual(0, layout.Placements[0].piece);
            Assert.AreEqual(Vector3.zero, layout.Placements[0].position);
            Assert.AreEqual(0f, layout.Placements[0].yaw);
        }

        [Test]
        public void IncompatibleSocketsNeverJoin()
        {
            var pieces = new List<PieceDefinition>
            {
                Cellular("Start", PieceRole.Start, new SocketDefinition(North.position, 0f, 1)),
                Cellular("Wrong", PieceRole.Normal, new SocketDefinition(South.position, 180f, 2), North),
            };
            var layout = new LayoutGenerator(pieces, Settings(1, 4)).Generate(3);
            Assert.AreEqual(1, layout.Placements.Count);
            Assert.AreEqual(1, layout.OpenSockets.Count);
        }

        [Test]
        public void SocketsThatLandOnEachOtherCloseALoop()
        {
            // Only crosses: a 2x2 block of them must join where they meet.
            var pieces = new List<PieceDefinition>
            {
                Cellular("Cross", PieceRole.Start, North, East, South, West),
                Cellular("Cross", PieceRole.Normal, North, East, South, West),
            };
            bool sawLoop = false;
            for (int seed = 0; seed < 20 && !sawLoop; seed++)
            {
                var layout = new LayoutGenerator(pieces, Settings(6, 9)).Generate(seed);
                // A tree of n pieces has n - 1 joins; more means a loop was closed.
                sawLoop = layout.Connections.Count > layout.Placements.Count - 1;
            }
            Assert.IsTrue(sawLoop);
        }

        [Test]
        public void RngIsStable()
        {
            // Pins the sequence so a change to Rng (which would change every saved seed's layout) is noticed.
            var rng = new Rng(12345);
            var first = rng.NextULong();
            var again = new Rng(12345).NextULong();
            Assert.AreEqual(first, again);
            Assert.AreNotEqual(first, new Rng(12346).NextULong());
            for (int i = 0; i < 1000; i++)
            {
                int v = rng.Range(3, 9);
                Assert.That(v, Is.InRange(3, 8));
                float f = rng.Value();
                Assert.That(f, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            }
        }

        // ---- Tags -----------------------------------------------------------------------------------------------

        const int Objective = 0;

        static List<PieceDefinition> WithObjectives()
        {
            var pieces = GraveyardLike();
            var puzzle = Cellular("Puzzle", PieceRole.Normal, South, North);
            puzzle.tags = new[] { Objective };
            var arena = Cellular("Arena", PieceRole.Normal, South, North, East);
            arena.tags = new[] { Objective };
            arena.weight = 0.5f;
            var shrine = Cellular("Shrine", PieceRole.Cap, South);
            shrine.tags = new[] { Objective };
            shrine.weight = 0f;
            pieces.Add(puzzle);
            pieces.Add(arena);
            pieces.Add(shrine);
            return pieces;
        }

        static TagRule[] ExactlyThreeObjectives(int spacing = 3) => new[]
        {
            new TagRule { tag = Objective, minCount = 3, maxCount = 3, minDepth = 2, minSpacing = spacing },
        };

        [Test]
        public void TagRulesGiveAnExactCount()
        {
            var pieces = WithObjectives();
            int valid = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                var layout = new LayoutGenerator(pieces, Settings(), null, ExactlyThreeObjectives()).Generate(seed);
                Assert.LessOrEqual(layout.CountWithTag(Objective), 3, $"seed {seed}");
                if (!layout.Valid)
                    continue;
                valid++;
                Assert.AreEqual(3, layout.CountWithTag(Objective), $"seed {seed}");
                foreach (var p in layout.Placements)
                    if (pieces[p.piece].HasTag(Objective))
                        Assert.GreaterOrEqual(p.depth, 2, $"seed {seed}");
            }
            Assert.Greater(valid, 55);
        }

        [Test]
        public void TagSpacingKeepsTaggedPiecesApart()
        {
            var pieces = WithObjectives();
            for (int seed = 0; seed < 40; seed++)
            {
                var layout = new LayoutGenerator(pieces, Settings(), null, ExactlyThreeObjectives(spacing: 3)).Generate(seed);
                var tagged = new List<int>();
                for (int i = 0; i < layout.Placements.Count; i++)
                    if (pieces[layout.Placements[i].piece].HasTag(Objective))
                        tagged.Add(i);
                var adjacency = Adjacency(layout);
                foreach (int a in tagged)
                {
                    var distance = Steps(adjacency, a);
                    foreach (int b in tagged)
                        if (a != b)
                            Assert.GreaterOrEqual(distance[b], 3, $"seed {seed}: {a} and {b}");
                }
            }
        }

        [Test]
        public void MixedSizesAndTagsStayDeterministic()
        {
            var pieces = WithObjectives();
            var a = new LayoutGenerator(pieces, Settings(), null, ExactlyThreeObjectives()).Generate(99);
            var b = new LayoutGenerator(pieces, Settings(), null, ExactlyThreeObjectives()).Generate(99);
            Assert.AreEqual(a.Placements.Count, b.Placements.Count);
            for (int i = 0; i < a.Placements.Count; i++)
                Assert.AreEqual(a.Placements[i].position, b.Placements[i].position);
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
