using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Masonry.Tests
{
    sealed class LevelTests
    {
        const float Half = 5f;
        static SocketDefinition North => new(new Vector3(0f, 0f, Half), 0f);
        static SocketDefinition South => new(new Vector3(0f, 0f, -Half), 180f);
        static SocketDefinition East => new(new Vector3(Half, 0f, 0f), 90f);
        static SocketDefinition West => new(new Vector3(-Half, 0f, 0f), 270f);

        static PieceDefinition Cell(string name, PieceRole role, params SocketDefinition[] sockets) => new()
        {
            name = name,
            role = role,
            bounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(10f, 4f, 10f)),
            sockets = sockets,
        };

        static LayoutSettings Settings => new() { minPieces = 8, maxPieces = 12, attempts = 20, overlapTolerance = 0.05f, joinDistance = 0.05f };

        [Test]
        public void SeveralStartsEachGetAnArrivalPiece()
        {
            var pieces = new List<PieceDefinition>
            {
                Cell("Landing", PieceRole.Start, North),
                Cell("Stair Foot", PieceRole.Arrival, North),
                Cell("Corridor", PieceRole.Normal, North, South),
                Cell("Junction", PieceRole.Normal, North, South, East, West),
            };
            var starts = new[]
            {
                new LayoutStart(new Vector3(0f, -6f, 0f), 0f),
                new LayoutStart(new Vector3(60f, -6f, 0f), 90f),
            };
            var layout = new LayoutGenerator(pieces, Settings).Generate(5, starts);

            Assert.AreEqual(1, layout.Placements[0].piece, "arrival piece, not the standalone start");
            Assert.AreEqual(1, layout.Placements[1].piece);
            Assert.AreEqual(new Vector3(0f, -6f, 0f), layout.Placements[0].position);
            Assert.AreEqual(new Vector3(60f, -6f, 0f), layout.Placements[1].position);
            Assert.AreEqual(90f, layout.Placements[1].yaw);
            Assert.AreEqual(0, layout.CountOf(0), "no standalone start when the level is entered from links");

            var standalone = new LayoutGenerator(pieces, Settings).Generate(5);
            Assert.AreEqual(0, standalone.Placements[0].piece, "on its own the level starts from its Start piece");
            Assert.AreEqual(0, standalone.CountOf(1), "arrival pieces only appear at links");
        }

        [Test]
        public void StartsAreDeterministic()
        {
            var pieces = new List<PieceDefinition>
            {
                Cell("Stair Foot", PieceRole.Arrival, North),
                Cell("Corridor", PieceRole.Normal, North, South),
                Cell("Junction", PieceRole.Normal, North, South, East, West),
            };
            var starts = new[] { new LayoutStart(Vector3.zero, 0f), new LayoutStart(new Vector3(40f, 0f, 40f), 180f) };
            var a = new LayoutGenerator(pieces, Settings).Generate(11, starts);
            var b = new LayoutGenerator(pieces, Settings).Generate(11, starts);
            Assert.AreEqual(a.Placements.Count, b.Placements.Count);
            for (int i = 0; i < a.Placements.Count; i++)
                Assert.AreEqual(a.Placements[i].position, b.Placements[i].position);
        }

        // ---- LevelPlan with real components ---------------------------------------------------------------------

        readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                Object.DestroyImmediate(o);
            created.Clear();
        }

        Piece Piece(string name, params (Vector3 position, float yaw)[] sockets)
        {
            var go = new GameObject(name);
            created.Add(go);
            var piece = go.AddComponent<Piece>();
            piece.Bounds = new Bounds(new Vector3(0f, 2f, 0f), new Vector3(10f, 4f, 10f));
            foreach (var (position, yaw) in sockets)
            {
                var s = new GameObject("Socket");
                s.transform.SetParent(go.transform, false);
                s.transform.SetLocalPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
                s.AddComponent<Socket>();
            }
            return piece;
        }

        PieceSet Set(string name, int min, int max, params (Piece piece, PieceRole role, int minCount, int maxCount)[] entries)
        {
            var set = ScriptableObject.CreateInstance<PieceSet>();
            set.name = name;
            created.Add(set);
            var settings = Settings;
            settings.minPieces = min;
            settings.maxPieces = max;
            set.Settings = settings;
            foreach (var (piece, role, minCount, maxCount) in entries)
                set.Pieces.Add(new PieceSet.Entry { piece = piece, role = role, weight = 1f, minCount = minCount, maxCount = maxCount });
            return set;
        }

        [Test]
        public void LinksBuildTheLevelBelow()
        {
            var n = (new Vector3(0f, 0f, Half), 0f);
            var s = (new Vector3(0f, 0f, -Half), 180f);

            // Crypt level: a stair foot to arrive on, corridors to grow.
            var stairFoot = Piece("Stair Foot", n);
            var corridor = Piece("Corridor", n, s);
            var crypt = Set("Crypt", 4, 6, (stairFoot, PieceRole.Arrival, 0, 0), (corridor, PieceRole.Normal, 0, 0));

            // Surface: a crypt entrance whose link sits 6 m below it.
            var gate = Piece("Gate", n);
            var path = Piece("Path", n, s);
            var entrance = Piece("Entrance", s);
            var link = new GameObject("Link");
            link.transform.SetParent(entrance.transform, false);
            link.transform.localPosition = new Vector3(0f, -6f, 0f);
            link.AddComponent<LevelLink>().Target = crypt;
            var surface = Set("Surface", 4, 5, (gate, PieceRole.Start, 0, 0), (path, PieceRole.Normal, 0, 0), (entrance, PieceRole.Normal, 1, 1));

            var levels = LevelPlan.Generate(surface, 3);
            Assert.AreEqual(2, levels.Count);
            Assert.AreSame(crypt, levels[1].Set);
            Assert.AreEqual(1, levels[1].Depth);

            var top = levels[0].Layout;
            int e = levels[0].Entries.FindIndex(x => x.piece == entrance);
            Placement entrancePlacement = default;
            foreach (var p in top.Placements)
                if (p.piece == e)
                    entrancePlacement = p;

            var arrival = levels[1].Layout.Placements[0];
            Assert.AreSame(stairFoot, levels[1].Entries[arrival.piece].piece);
            Assert.That(Vector3.Distance(entrancePlacement.position + Vector3.down * 6f, arrival.position), Is.LessThan(0.01f));
            Assert.That(Mathf.DeltaAngle(entrancePlacement.yaw, arrival.yaw), Is.EqualTo(0f).Within(0.01f));

            var again = LevelPlan.Generate(surface, 3);
            Assert.AreEqual(levels[1].Layout.Placements.Count, again[1].Layout.Placements.Count, "the whole stack comes from one seed");
        }

        [Test]
        public void SetsThatLinkBackDoNotLoop()
        {
            var n = (new Vector3(0f, 0f, Half), 0f);
            var a = Piece("A", n);
            var b = Piece("B", n);
            var setA = Set("A", 1, 1, (a, PieceRole.Start, 0, 0));
            var setB = Set("B", 1, 1, (b, PieceRole.Start, 0, 0));
            AddLink(a, setB);
            AddLink(b, setA);
            Assert.AreEqual(2, LevelPlan.Generate(setA, 1).Count);
        }

        void AddLink(Piece piece, PieceSet target)
        {
            var go = new GameObject("Link");
            go.transform.SetParent(piece.transform, false);
            go.transform.localPosition = Vector3.down * 6f;
            go.AddComponent<LevelLink>().Target = target;
        }
    }
}
