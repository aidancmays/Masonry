using System;
using System.Collections.Generic;
using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// Grows a layout from a start piece by attaching pieces to open sockets, then closes what is left with caps.
    /// Pure and engine-scene-free: it works on <see cref="PieceDefinition"/>s, so it can run on a server, in tests,
    /// or ahead of time. The same pieces, settings and seed always give the same layout.
    /// </summary>
    /// <remarks>
    /// Pieces only ever turn about Y. A piece joins an open socket through one of its own sockets: the two face each
    /// other and share a position. Pieces must not overlap (beyond <see cref="LayoutSettings.overlapTolerance"/>).
    /// Any other socket of a new piece that lands on an open socket is joined too, which makes loops.
    /// If a try misses a minimum count it is thrown away and the next seeded try runs, up to
    /// <see cref="LayoutSettings.attempts"/>; the best try is returned with <see cref="Layout.Valid"/> false.
    /// </remarks>
    public sealed class LayoutGenerator
    {
        /// <summary>Whether sockets of type a and type b may join.</summary>
        public delegate bool Compatibility(int a, int b);

        const float FacingToleranceDegrees = 1f;

        readonly IReadOnlyList<PieceDefinition> pieces;
        readonly Compatibility compatible;
        readonly LayoutSettings settings;

        Rng rng;
        Layout layout;
        int[] counts;
        int grown;
        readonly List<OrientedBox> boxes = new();
        readonly List<SocketRef> open = new();
        readonly HashSet<SocketRef> stuck = new();
        readonly List<int> candidates = new();
        readonly List<int> socketOrder = new();
        readonly List<int> free = new();

        public LayoutGenerator(IReadOnlyList<PieceDefinition> pieces, LayoutSettings settings, Compatibility compatible = null)
        {
            this.pieces = pieces ?? throw new ArgumentNullException(nameof(pieces));
            this.settings = settings;
            this.compatible = compatible ?? ((a, b) => a == b);
        }

        public IReadOnlyList<PieceDefinition> Pieces => pieces;

        /// <summary>Builds a layout. Never returns null; check <see cref="Layout.Valid"/>.</summary>
        public Layout Generate(int seed)
        {
            var seeds = new Rng(seed);
            Layout best = null;
            int bestScore = int.MinValue;
            int attempts = Mathf.Max(1, settings.attempts);

            for (int attempt = 0; attempt < attempts; attempt++)
            {
                var result = Build(seeds.NextInt());
                result.Seed = seed;
                result.Attempt = attempt;
                if (result.Valid)
                    return result;

                int score = Score(result);
                if (score > bestScore)
                {
                    best = result;
                    bestScore = score;
                }
            }
            return best;
        }

        /// <summary>World pose of a socket of a placed piece, relative to the layout origin.</summary>
        public void GetSocketPose(Placement placement, int socket, out Vector3 position, out float yaw)
        {
            var s = pieces[placement.piece].sockets[socket];
            position = placement.position + placement.Rotation * s.position;
            yaw = placement.yaw + s.yaw;
        }

        // ---- One try ------------------------------------------------------------------------------------------

        Layout Build(int attemptSeed)
        {
            rng = new Rng(attemptSeed);
            layout = new Layout();
            counts = new int[pieces.Count];
            grown = 0;
            boxes.Clear();
            open.Clear();
            stuck.Clear();

            int start = PickStart();
            if (start < 0)
            {
                Debug.LogWarning("[Masonry] The piece set has no start piece and no normal piece to start from.");
                return layout;
            }

            Place(start, Vector3.zero, 0f, 0, default, -1);

            int min = Mathf.Max(1, settings.minPieces);
            int max = Mathf.Max(min, settings.maxPieces);
            int target = rng.Range(min, max + 1);

            while (grown < target)
            {
                free.Clear();
                for (int i = 0; i < open.Count; i++)
                    if (!stuck.Contains(open[i]))
                        free.Add(i);
                if (free.Count == 0)
                    break;

                var socket = open[free[rng.Range(0, free.Count)]];
                if (!TryGrow(socket, target - grown))
                    stuck.Add(socket);
            }

            Cap();

            layout.OpenSockets.AddRange(open);
            layout.Valid = grown >= min && MissingRequired(null) == 0;
            return layout;
        }

        int PickStart()
        {
            candidates.Clear();
            for (int p = 0; p < pieces.Count; p++)
                if (pieces[p].role == PieceRole.Start)
                    candidates.Add(p);
            if (candidates.Count == 0)
                for (int p = 0; p < pieces.Count; p++)
                    if (pieces[p].role == PieceRole.Normal)
                        candidates.Add(p);
            return candidates.Count == 0 ? -1 : TakeWeighted(candidates);
        }

        bool TryGrow(SocketRef from, int remaining)
        {
            var parent = layout.Placements[from.placement];
            int type = pieces[parent.piece].sockets[from.socket].type;
            int depth = parent.depth + 1;

            // Once the budget left is only enough for the pieces still owed, place only those.
            int missing = MissingRequired(PieceRole.Normal);
            bool onlyRequired = missing > 0 && remaining <= missing;

            candidates.Clear();
            for (int p = 0; p < pieces.Count; p++)
            {
                var d = pieces[p];
                if (d.role != PieceRole.Normal || !Allowed(p, depth))
                    continue;
                if (onlyRequired && counts[p] >= d.minCount)
                    continue;
                if (Weight(p) <= 0f || !HasCompatibleSocket(d, type))
                    continue;
                candidates.Add(p);
            }

            while (candidates.Count > 0)
            {
                int p = TakeWeighted(candidates);
                if (TryAttach(p, from, depth))
                    return true;
            }
            return false;
        }

        void Cap()
        {
            // Required caps first, then the rest; repeat while something was placed (a cap can have sockets too).
            bool placed = true;
            for (int pass = 0; placed && pass < 8; pass++)
            {
                placed = false;
                for (int i = 0; i < open.Count; i++)
                {
                    var from = open[i];
                    var parent = layout.Placements[from.placement];
                    int type = pieces[parent.piece].sockets[from.socket].type;
                    int depth = parent.depth + 1;

                    if (TryCap(from, type, depth, requiredOnly: true) || TryCap(from, type, depth, requiredOnly: false))
                    {
                        placed = true;
                        i = -1; // the open list changed; start over from the top
                    }
                }
            }
        }

        bool TryCap(SocketRef from, int type, int depth, bool requiredOnly)
        {
            candidates.Clear();
            for (int p = 0; p < pieces.Count; p++)
            {
                var d = pieces[p];
                if (d.role != PieceRole.Cap || !Allowed(p, depth) || !HasCompatibleSocket(d, type))
                    continue;
                if (requiredOnly && counts[p] >= d.minCount)
                    continue;
                if (Weight(p) <= 0f)
                    continue;
                candidates.Add(p);
            }

            while (candidates.Count > 0)
            {
                int p = TakeWeighted(candidates);
                if (TryAttach(p, from, depth))
                    return true;
            }
            return false;
        }

        bool TryAttach(int piece, SocketRef from, int depth)
        {
            var d = pieces[piece];
            GetSocketPose(layout.Placements[from.placement], from.socket, out var targetPosition, out var targetYaw);
            int type = pieces[layout.Placements[from.placement].piece].sockets[from.socket].type;

            socketOrder.Clear();
            for (int j = 0; j < d.sockets.Length; j++)
                if (compatible(type, d.sockets[j].type))
                    socketOrder.Add(j);
            Shuffle(socketOrder);

            foreach (int j in socketOrder)
            {
                var s = d.sockets[j];
                float yaw = SnapYaw(targetYaw + 180f - s.yaw);
                var position = Snap(targetPosition - Quaternion.Euler(0f, yaw, 0f) * s.position);
                var box = new OrientedBox(d.bounds, position, yaw, settings.overlapTolerance);
                if (Collides(box))
                    continue;

                Place(piece, position, yaw, depth, from, j, box);
                return true;
            }
            return false;
        }

        void Place(int piece, Vector3 position, float yaw, int depth, SocketRef from, int via)
        {
            Place(piece, position, yaw, depth, from, via, new OrientedBox(pieces[piece].bounds, position, yaw, settings.overlapTolerance));
        }

        void Place(int piece, Vector3 position, float yaw, int depth, SocketRef from, int via, OrientedBox box)
        {
            int index = layout.Placements.Count;
            var placement = new Placement(piece, position, yaw, depth);
            layout.Placements.Add(placement);
            boxes.Add(box);
            counts[piece]++;
            if (pieces[piece].role != PieceRole.Cap)
                grown++;

            var sockets = pieces[piece].sockets;
            for (int j = 0; j < sockets.Length; j++)
            {
                var me = new SocketRef(index, j);
                if (j == via)
                {
                    Join(from, me);
                    continue;
                }

                int match = FindJoin(placement, j);
                if (match >= 0)
                    Join(open[match], me);
                else
                    open.Add(me);
            }
        }

        void Join(SocketRef existing, SocketRef added)
        {
            layout.Connections.Add(new Connection(existing, added));
            open.Remove(existing);
            stuck.Remove(existing);
        }

        int FindJoin(Placement placement, int socket)
        {
            GetSocketPose(placement, socket, out var position, out var yaw);
            int type = pieces[placement.piece].sockets[socket].type;
            float maxSqr = settings.joinDistance * settings.joinDistance;

            for (int i = 0; i < open.Count; i++)
            {
                var o = open[i];
                var other = layout.Placements[o.placement];
                if (!compatible(type, pieces[other.piece].sockets[o.socket].type))
                    continue;
                GetSocketPose(other, o.socket, out var otherPosition, out var otherYaw);
                if ((otherPosition - position).sqrMagnitude > maxSqr)
                    continue;
                if (Mathf.Abs(Mathf.DeltaAngle(yaw, otherYaw + 180f)) > FacingToleranceDegrees)
                    continue;
                return i;
            }
            return -1;
        }

        // ---- Helpers ------------------------------------------------------------------------------------------

        bool Allowed(int piece, int depth)
        {
            var d = pieces[piece];
            return (d.maxCount <= 0 || counts[piece] < d.maxCount) && depth >= d.minDepth;
        }

        // Pieces still owed get a weight even if their weight is 0, so a minimum can always be met.
        float Weight(int piece)
        {
            var d = pieces[piece];
            if (d.weight > 0f)
                return d.weight;
            return counts[piece] < d.minCount ? 1f : 0f;
        }

        int MissingRequired(PieceRole? role)
        {
            int missing = 0;
            for (int p = 0; p < pieces.Count; p++)
                if ((role == null || pieces[p].role == role) && counts[p] < pieces[p].minCount)
                    missing += pieces[p].minCount - counts[p];
            return missing;
        }

        int Score(Layout result)
        {
            int missing = 0;
            for (int p = 0; p < pieces.Count; p++)
                missing += Mathf.Max(0, pieces[p].minCount - result.CountOf(p));
            return result.Placements.Count - missing * 1000;
        }

        bool HasCompatibleSocket(PieceDefinition d, int type)
        {
            foreach (var s in d.sockets)
                if (compatible(type, s.type))
                    return true;
            return false;
        }

        bool Collides(in OrientedBox box)
        {
            foreach (var other in boxes)
                if (box.Overlaps(other))
                    return true;
            return false;
        }

        /// <summary>Removes and returns a weighted random entry of the list.</summary>
        int TakeWeighted(List<int> list)
        {
            float total = 0f;
            foreach (int p in list)
                total += Mathf.Max(0f, Weight(p));

            int index = list.Count - 1;
            if (total > 0f)
            {
                float roll = rng.Value() * total;
                for (int i = 0; i < list.Count; i++)
                {
                    roll -= Mathf.Max(0f, Weight(list[i]));
                    if (roll < 0f)
                    {
                        index = i;
                        break;
                    }
                }
            }
            else
            {
                index = rng.Range(0, list.Count);
            }

            int value = list[index];
            list.RemoveAt(index);
            return value;
        }

        void Shuffle(List<int> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        // Rounding keeps long chains of pieces from drifting and makes joins exact.
        static Vector3 Snap(Vector3 v) => new(Round(v.x), Round(v.y), Round(v.z));
        static float Round(float v) => Mathf.Round(v * 1000f) / 1000f;

        static float SnapYaw(float yaw)
        {
            yaw = Round(Mathf.Repeat(yaw, 360f));
            return yaw >= 360f ? 0f : yaw;
        }
    }
}
