using System.Collections.Generic;
using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// Decides every <see cref="Boundary"/> of a built layout: pairs up boundaries that face each other, asks the
    /// Piece Set which style goes between the two module types, and lets only one side draw it.
    /// <see cref="LayoutBuilder"/> runs it after every build; call it yourself if you place pieces another way.
    /// </summary>
    public static class BoundaryResolver
    {
        const float MatchDistance = 0.05f;
        const float MatchFacing = -0.99f;

        public static void Resolve(IReadOnlyList<Piece> pieces, PieceSet rules)
        {
            var all = new List<(Boundary boundary, Piece piece, int index)>();
            for (int i = 0; i < pieces.Count; i++)
            {
                if (pieces[i] == null)
                    continue;
                foreach (var b in pieces[i].Boundaries)
                {
                    b.Neighbour = null;
                    all.Add((b, pieces[i], i));
                }
            }

            var done = new HashSet<Boundary>();
            for (int i = 0; i < all.Count; i++)
            {
                var (b, piece, index) = all[i];
                if (done.Contains(b))
                    continue;
                done.Add(b);

                int match = -1;
                for (int k = i + 1; k < all.Count; k++)
                {
                    var other = all[k];
                    if (other.piece == piece || done.Contains(other.boundary))
                        continue;
                    if (Facing(b.transform, other.boundary.transform))
                    {
                        match = k;
                        break;
                    }
                }

                if (match < 0)
                {
                    var style = rules != null ? rules.BoundaryFor(piece.ModuleType, null, outside: true) : null;
                    b.Apply(style, true);
                    continue;
                }

                var (ob, opiece, oindex) = all[match];
                done.Add(ob);
                b.Neighbour = ob;
                ob.Neighbour = b;

                var shared = rules != null ? rules.BoundaryFor(piece.ModuleType, opiece.ModuleType, outside: false) : null;
                bool here = DrawsHere(piece, index, opiece, oindex, b, ob, shared);
                b.Apply(shared, here);
                ob.Apply(shared, !here);
            }
        }

        // A side with art for the style beats one without; then higher module priority; then the earlier piece.
        static bool DrawsHere(Piece a, int ai, Piece b, int bi, Boundary ab, Boundary bb, BoundaryStyle style)
        {
            bool ha = HasArt(ab, style), hb = HasArt(bb, style);
            if (ha != hb)
                return ha;
            int pa = a.ModuleType != null ? a.ModuleType.Priority : 0;
            int pb = b.ModuleType != null ? b.ModuleType.Priority : 0;
            if (pa != pb)
                return pa > pb;
            return ai <= bi;
        }

        static bool HasArt(Boundary b, BoundaryStyle style)
        {
            foreach (var v in b.Variants)
                if (v.style == style && v.art != null)
                    return true;
            return false;
        }

        static bool Facing(Transform a, Transform b)
        {
            var d = a.position - b.position;
            d.y = 0f;
            if (d.sqrMagnitude > MatchDistance * MatchDistance)
                return false;
            var fa = a.forward;
            var fb = b.forward;
            fa.y = 0f;
            fb.y = 0f;
            return Vector3.Dot(fa.normalized, fb.normalized) < MatchFacing;
        }
    }
}
