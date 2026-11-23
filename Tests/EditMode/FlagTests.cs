using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace Masonry.Tests
{
    sealed class FlagTests
    {
        [Test]
        public void ConditionsMatchFlags()
        {
            var flags = Flags.Parse("exit-gate, Main");
            Assert.IsTrue(Flags.Match("", flags));
            Assert.IsTrue(Flags.Match(null, flags));
            Assert.IsTrue(Flags.Match("exit-gate", flags));
            Assert.IsTrue(Flags.Match("EXIT-GATE", flags), "case does not matter");
            Assert.IsTrue(Flags.Match("exit-gate, main", flags), "every part must hold");
            Assert.IsFalse(Flags.Match("exit-gate, linked", flags));
            Assert.IsFalse(Flags.Match("exit-crypt", flags));
            Assert.IsTrue(Flags.Match("!exit-crypt", flags));
            Assert.IsFalse(Flags.Match("!exit-gate", flags));
            Assert.IsFalse(Flags.Match("exit-gate", null));
            Assert.IsTrue(Flags.Match("!exit-gate", null));
        }

        readonly List<Object> created = new();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in created)
                Object.DestroyImmediate(o);
            created.Clear();
        }

        Piece Piece(string name)
        {
            var go = new GameObject(name);
            created.Add(go);
            var piece = go.AddComponent<Piece>();
            var s = new GameObject("Socket");
            s.transform.SetParent(go.transform, false);
            s.transform.localPosition = new Vector3(0f, 0f, 5f);
            s.AddComponent<Socket>();
            return piece;
        }

        [Test]
        public void EntriesFollowTheirConditions()
        {
            var set = ScriptableObject.CreateInstance<PieceSet>();
            created.Add(set);
            set.Pieces.Add(new PieceSet.Entry { piece = Piece("Always") });
            set.Pieces.Add(new PieceSet.Entry { piece = Piece("Gate Exit"), condition = "exit-gate" });
            set.Pieces.Add(new PieceSet.Entry { piece = Piece("Crypt Exit"), condition = "exit-crypt" });
            set.Pieces.Add(new PieceSet.Entry { piece = Piece("Descent"), condition = "main" });

            set.CreateGenerator(out var used, Flags.With(new[] { "exit-gate" }, Flags.Main));
            CollectionAssert.AreEquivalent(new[] { "Always", "Gate Exit", "Descent" }, used.ConvertAll(e => e.piece.name));

            set.CreateGenerator(out used, Flags.With(new[] { "exit-crypt" }, Flags.Linked));
            CollectionAssert.AreEquivalent(new[] { "Always", "Crypt Exit" }, used.ConvertAll(e => e.piece.name));
        }
    }
}
