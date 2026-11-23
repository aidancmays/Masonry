using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Masonry.Tests
{
    sealed class BoundaryResolverTests
    {
        readonly List<Object> created = new();

        ModuleType graveyard, mausoleum;
        BoundaryStyle fence, wall;
        PieceSet rules;

        [SetUp]
        public void SetUp()
        {
            graveyard = Make<ModuleType>("Graveyard");
            mausoleum = Make<ModuleType>("Mausoleum");
            fence = Make<BoundaryStyle>("Fence");
            wall = Make<BoundaryStyle>("Wall");
            rules = Make<PieceSet>("Rules");
            rules.DefaultBoundary = fence;
            rules.BoundaryRules.Add(new PieceSet.BoundaryRule { a = graveyard, b = graveyard, style = null });
            rules.BoundaryRules.Add(new PieceSet.BoundaryRule { a = mausoleum, b = graveyard, style = fence });
            rules.BoundaryRules.Add(new PieceSet.BoundaryRule { a = mausoleum, b = null, style = wall });
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                Object.DestroyImmediate(o);
            created.Clear();
        }

        T Make<T>(string name) where T : ScriptableObject
        {
            var o = ScriptableObject.CreateInstance<T>();
            o.name = name;
            created.Add(o);
            return o;
        }

        // A 10 m square piece at x with one boundary per side, each holding Fence and Wall art.
        Piece Square(ModuleType type, float x)
        {
            var go = new GameObject($"{type.name} {x}");
            created.Add(go);
            go.transform.position = new Vector3(x, 0f, 0f);
            var piece = go.AddComponent<Piece>();
            piece.ModuleType = type;
            foreach (float yaw in new[] { 0f, 90f, 180f, 270f })
            {
                var edge = new GameObject($"Edge {yaw}");
                edge.transform.SetParent(go.transform, false);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                edge.transform.SetLocalPositionAndRotation(rot * Vector3.forward * 5f, rot);
                var b = edge.AddComponent<Boundary>();
                foreach (var style in new[] { fence, wall })
                {
                    var art = new GameObject(style.name);
                    art.transform.SetParent(edge.transform, false);
                    b.Variants.Add(new Boundary.Variant { style = style, art = art });
                }
            }
            return piece;
        }

        static Boundary Edge(Piece piece, float yaw) => piece.transform.Find($"Edge {yaw}").GetComponent<Boundary>();

        static bool Shows(Boundary b, BoundaryStyle style)
        {
            foreach (var v in b.Variants)
                if (v.style == style)
                    return v.art.activeSelf;
            return false;
        }

        [Test]
        public void SameModulesRunIntoEachOther()
        {
            var a = Square(graveyard, 0f);
            var b = Square(graveyard, 10f);
            BoundaryResolver.Resolve(new[] { a, b }, rules);

            var shared = Edge(a, 90f);
            Assert.AreSame(Edge(b, 270f), shared.Neighbour);
            Assert.IsNull(shared.Style);
            Assert.IsFalse(Shows(shared, fence) || Shows(shared.Neighbour, fence));
            Assert.IsTrue(Shows(Edge(a, 0f), fence), "outer edges use the default");
        }

        [Test]
        public void DifferentModulesGetOneFence()
        {
            var a = Square(graveyard, 0f);
            var b = Square(mausoleum, 10f);
            BoundaryResolver.Resolve(new[] { a, b }, rules);

            var left = Edge(a, 90f);
            var right = Edge(b, 270f);
            Assert.AreSame(fence, left.Style);
            Assert.AreNotEqual(Shows(left, fence), Shows(right, fence), "exactly one side draws it");
            Assert.IsFalse(Shows(left, wall) || Shows(right, wall));
            Assert.IsTrue(Shows(Edge(b, 0f), wall), "mausoleum's outside rule");
        }

        [Test]
        public void HigherPriorityDrawsTheSharedEdge()
        {
            var priority = typeof(ModuleType).GetField("priority", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            priority.SetValue(mausoleum, 5);
            var a = Square(graveyard, 0f);
            var b = Square(mausoleum, 10f);
            BoundaryResolver.Resolve(new[] { a, b }, rules);
            Assert.IsTrue(Edge(b, 270f).Drawn);
            Assert.IsFalse(Edge(a, 90f).Drawn);
        }

        [Test]
        public void ASideWithoutArtLetsTheOtherDraw()
        {
            var a = Square(graveyard, 0f);
            var b = Square(mausoleum, 10f);
            Edge(a, 90f).Variants.Clear();
            BoundaryResolver.Resolve(new[] { a, b }, rules);
            Assert.IsTrue(Shows(Edge(b, 270f), fence));
        }
    }
}
