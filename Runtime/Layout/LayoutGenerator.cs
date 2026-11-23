using System;
using System.Collections.Generic;
using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// Grows a layout from a start piece by attaching pieces to open sockets, then closes what is left with caps.
    /// Pure and scene-free: it works on <see cref="PieceDefinition"/>s, so it can run on a server, in tests, in an
    /// editor preview, or ahead of time. The same pieces, rules and seed always give the same layout.
    /// </summary>
    /// <remarks>
    /// Pieces only ever turn about Y. A piece joins an open socket through one of its own sockets: the two face each
    /// other and share a position. Pieces must not overlap (beyond <see cref="LayoutSettings.overlapTolerance"/>).
    /// Any other socket of a new piece that lands on an open socket is joined too, which makes loops.
    /// Piece and tag minimums are met by spending the end of the piece budget on whatever is still owed. If a try
    /// still misses one it is thrown away and the next seeded try runs, up to <see cref="LayoutSettings.attempts"/>;
    /// the best try is returned with <see cref="Layout.Valid"/> false.
    /// </remarks>
    public sealed class LayoutGenerator
    {
        /// <summary>Whether sockets of type a and type b may join.</summary>
        public delegate bool Compatibility(int a, int b);

        const float FacingToleranceDegrees = 1f;

        readonly IReadOnlyList<PieceDefinition> pieces;
        readonly TagRule[] tagRules;
        readonly bool[] tagRuleGrows; // some Normal piece carries the tag, so growth can pay it off
        readonly List<(int[] tags, int minSteps)> spacing = new(); // tag rule spacing and spacing rules alike
        readonly DeadEndRule deadEnds;
        readonly Compatibility compatible;
        readonly LayoutSettings settings;

        Rng rng;
        Layout layout;
        int[] counts;
        int[] tagCounts;
        int grown;
        readonly List<OrientedBox> boxes = new();
        readonly List<List<int>> neighbours = new();
        readonly List<SocketRef> open = new();
        readonly HashSet<SocketRef> stuck = new();
        readonly List<int> candidates = new();
        readonly List<int> socketOrder = new();
        readonly List<int> free = new();
        readonly List<int> distances = new();
        readonly Queue<int> frontier = new();
        int distancesFrom = -1;
        IReadOnlyList<LayoutStart> starts;

        public LayoutGenerator(IReadOnlyList<PieceDefinition> pieces, LayoutSettings settings, Compatibility compatible = null,
            IReadOnlyList<TagRule> tagRules = null, IReadOnlyList<SpacingRule> spacingRules = null, DeadEndRule deadEnds = default)
        {
            this.deadEnds = deadEnds;
            this.pieces = pieces ?? throw new ArgumentNullException(nameof(pieces));
            this.settings = settings;
            this.compatible = compatible ?? ((a, b) => a == b);

            this.tagRules = new TagRule[tagRules?.Count ?? 0];
            tagRuleGrows = new bool[this.tagRules.Length];
            for (int r = 0; r < this.tagRules.Length; r++)
            {
                this.tagRules[r] = tagRules[r];
                foreach (var p in pieces)
                    if (p.role == PieceRole.Normal && p.HasTag(this.tagRules[r].tag))
                        tagRuleGrows[r] = true;
                if (this.tagRules[r].minSpacing > 1)
                    spacing.Add((new[] { this.tagRules[r].tag }, this.tagRules[r].minSpacing));
            }
            if (spacingRules != null)
                foreach (var rule in spacingRules)
                    if (rule.minSteps > 1 && rule.tags != null && rule.tags.Length > 0)
                        spacing.Add((rule.tags, rule.minSteps));
        }

        public DeadEndRule DeadEndRule => deadEnds;

        public IReadOnlyList<PieceDefinition> Pieces => pieces;
        public IReadOnlyList<TagRule> TagRules => tagRules;
        public LayoutSettings Settings => settings;

        /// <summary>Builds a layout from one start piece at the origin. Never returns null; check <see cref="Layout.Valid"/>.</summary>
        public Layout Generate(int seed) => Generate(seed, null);

        /// <summary>
        /// Builds a layout grown from several start points at once, e.g. every stairwell that leads down into this
        /// level. Each start places its own piece (or an <see cref="PieceRole.Arrival"/> piece, else a Start piece);
        /// the piece budget and every rule cover the whole level. Null or empty = one start at the origin.
        /// </summary>
        public Layout Generate(int seed, IReadOnlyList<LayoutStart> starts)
        {
            this.starts = starts;
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

                int score = result.Placements.Count - Deficit(result) * 1000;
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

        /// <summary>How many piece and tag minimums a layout misses, in pieces.</summary>
        public int Deficit(Layout result)
        {
            int missing = 0;
            for (int p = 0; p < pieces.Count; p++)
                missing += Mathf.Max(0, pieces[p].minCount - result.CountOf(p));
            foreach (var rule in tagRules)
                missing += Mathf.Max(0, rule.minCount - result.CountWithTag(rule.tag));
            return missing + result.DeadEndViolations;
        }

        // ---- One try ------------------------------------------------------------------------------------------

        Layout Build(int attemptSeed)
        {
            rng = new Rng(attemptSeed);
            layout = new Layout { Pieces = pieces };
            counts = new int[pieces.Count];
            tagCounts = new int[tagRules.Length];
            grown = 0;
            boxes.Clear();
            neighbours.Clear();
            open.Clear();
            stuck.Clear();
            distancesFrom = -1;

            if (starts == null || starts.Count == 0)
            {
                int start = PickStart(PieceRole.Start);
                if (start < 0)
                {
                    Debug.LogWarning("[Masonry] The piece set has no start piece and no normal piece to start from.");
                    return layout;
                }
                Place(start, Vector3.zero, 0f, 0, default, -1,
                    new OrientedBox(pieces[start].bounds, Vector3.zero, 0f, settings.overlapTolerance));
            }
            else
            {
                foreach (var s in starts)
                {
                    int piece = s.piece >= 0 && s.piece < pieces.Count ? s.piece : PickStart(PieceRole.Arrival);
                    if (piece < 0)
                        continue;
                    float yaw = SnapYaw(s.yaw);
                    var position = Snap(s.position);
                    var box = new OrientedBox(pieces[piece].bounds, position, yaw, settings.overlapTolerance);
                    if (Collides(box))
                        continue; // two entrances too close together: the first one wins
                    Place(piece, position, yaw, 0, default, -1, box);
                }
                if (layout.Placements.Count == 0)
                {
                    Debug.LogWarning("[Masonry] None of the level's start points could take a piece.");
                    return layout;
                }
            }

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

                var socket = open[free[PickGrowthSocket()]];
                if (!TryAdd(socket, PieceRole.Normal, target - grown))
                    stuck.Add(socket);
            }

            Cap();

            layout.OpenSockets.AddRange(open);
            FindDeadEnds();
            layout.Valid = grown >= min && Deficit(layout) == 0;
            return layout;
        }

        // Index into `free`: usually random; with depthBias, sometimes the deepest open socket (ties at random).
        int PickGrowthSocket()
        {
            if (settings.depthBias <= 0f || rng.Value() >= settings.depthBias)
                return rng.Range(0, free.Count);
            int deepest = -1, ties = 0, pick = 0;
            for (int i = 0; i < free.Count; i++)
            {
                int d = layout.Placements[open[free[i]].placement].depth;
                if (d > deepest)
                {
                    deepest = d;
                    ties = 1;
                    pick = i;
                }
                else if (d == deepest && rng.Range(0, ++ties) == 0)
                {
                    pick = i;
                }
            }
            return pick;
        }

        // Arrival falls back to Start, Start falls back to Normal.
        int PickStart(PieceRole role)
        {
            candidates.Clear();
            if (role == PieceRole.Arrival)
                for (int p = 0; p < pieces.Count; p++)
                    if (pieces[p].role == PieceRole.Arrival)
                        candidates.Add(p);
            if (candidates.Count == 0)
                for (int p = 0; p < pieces.Count; p++)
                    if (pieces[p].role == PieceRole.Start)
                        candidates.Add(p);
            if (candidates.Count == 0)
                for (int p = 0; p < pieces.Count; p++)
                    if (pieces[p].role == PieceRole.Normal)
                        candidates.Add(p);
            return candidates.Count == 0 ? -1 : TakeWeighted(candidates);
        }

        void Cap()
        {
            // Owed caps first, then the rest; repeat while something was placed (a cap can have sockets too).
            bool placed = true;
            for (int pass = 0; placed && pass < 8; pass++)
            {
                placed = false;
                for (int i = 0; i < open.Count; i++)
                {
                    // A cap here ends a corridor; if that corridor is long, it should end in something.
                    var s = open[i];
                    bool needsContent = deadEnds.minLength > 0 && 1 + Corridor(s.placement, -1, s.placement) >= deadEnds.minLength;
                    if (TryAdd(s, PieceRole.Cap, int.MaxValue, owedOnly: true)
                        || (needsContent && TryAdd(s, PieceRole.Cap, int.MaxValue, contentOnly: true))
                        || TryAdd(s, PieceRole.Cap, int.MaxValue))
                    {
                        placed = true;
                        i = -1; // the open list changed; start over from the top
                    }
                }
            }
        }

        /// <summary>Tries pieces of one role on an open socket.</summary>
        bool TryAdd(SocketRef from, PieceRole role, int remaining, bool owedOnly = false, bool contentOnly = false)
        {
            var parent = layout.Placements[from.placement];
            int type = pieces[parent.piece].sockets[from.socket].type;
            int depth = parent.depth + 1;

            // Once the budget left is only enough for what is still owed, place only that.
            if (role == PieceRole.Normal)
            {
                int owed = Owed();
                owedOnly |= owed > 0 && remaining <= owed;
            }

            candidates.Clear();
            for (int p = 0; p < pieces.Count; p++)
            {
                var d = pieces[p];
                if (d.role != role || (owedOnly && !IsOwed(p)) || Weight(p) <= 0f)
                    continue;
                if (contentOnly && !d.HasAnyTag(deadEnds.tags))
                    continue;
                if (!HasCompatibleSocket(d, type) || !Allowed(p, depth, from.placement))
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
            var parent = layout.Placements[from.placement];
            GetSocketPose(parent, from.socket, out var targetPosition, out var targetYaw);
            int type = pieces[parent.piece].sockets[from.socket].type;

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

        void Place(int piece, Vector3 position, float yaw, int depth, SocketRef from, int via, OrientedBox box)
        {
            int index = layout.Placements.Count;
            var placement = new Placement(piece, position, yaw, depth);
            layout.Placements.Add(placement);
            boxes.Add(box);
            neighbours.Add(new List<int>());
            distancesFrom = -1;
            counts[piece]++;
            for (int r = 0; r < tagRules.Length; r++)
                if (pieces[piece].HasTag(tagRules[r].tag))
                    tagCounts[r]++;
            if (pieces[piece].role != PieceRole.Cap)
                grown++;

            if (via >= 0)
                Join(from, new SocketRef(index, via));

            var sockets = pieces[piece].sockets;
            for (int j = 0; j < sockets.Length; j++)
            {
                if (j == via)
                    continue;
                var me = new SocketRef(index, j);
                int match = FindJoin(placement, j);
                if (match >= 0 && JoinKeepsSpacing(open[match].placement, index))
                    Join(open[match], me);
                else
                    open.Add(me);
            }
        }

        // A loop can bring two spaced pieces closer than their rule allows; such sockets are left unjoined.
        bool JoinKeepsSpacing(int a, int b)
        {
            if (spacing.Count == 0 || a == b || neighbours[a].Contains(b))
                return true;

            var fromA = Steps(a);
            var fromB = Steps(b);
            foreach (var (tags, minSteps) in spacing)
            {
                for (int i = 0; i < fromA.Length; i++)
                {
                    if (fromA[i] < 0 || !pieces[layout.Placements[i].piece].HasAnyTag(tags))
                        continue;
                    for (int k = 0; k < fromB.Length; k++)
                    {
                        if (k == i || fromB[k] < 0 || !pieces[layout.Placements[k].piece].HasAnyTag(tags))
                            continue;
                        if (fromA[i] + 1 + fromB[k] < minSteps)
                            return false;
                    }
                }
            }
            return true;
        }

        // ---- Dead ends ----------------------------------------------------------------------------------------

        // Pieces in the corridor through `node`, walking away from `from`, up to a junction or a start piece.
        // `extraOn` counts one more connection on that piece (a cap about to be added there).
        int Corridor(int node, int from, int extraOn)
        {
            int length = 0, prev = from, cur = node;
            while (cur >= 0 && length <= layout.Placements.Count)
            {
                var role = pieces[layout.Placements[cur].piece].role;
                if (role == PieceRole.Start || role == PieceRole.Arrival)
                    break;
                int degree = neighbours[cur].Count + (cur == extraOn ? 1 : 0);
                if (degree >= 3)
                    break;
                length++;
                int next = -1;
                foreach (int n in neighbours[cur])
                    if (n != prev)
                    {
                        next = n;
                        break;
                    }
                prev = cur;
                cur = next;
            }
            return length;
        }

        void FindDeadEnds()
        {
            int violations = 0;
            for (int i = 0; i < layout.Placements.Count; i++)
            {
                var d = pieces[layout.Placements[i].piece];
                if (d.role == PieceRole.Start || d.role == PieceRole.Arrival || neighbours[i].Count > 1)
                    continue;
                int length = Corridor(i, -1, -1);
                bool content = d.HasAnyTag(deadEnds.tags);
                layout.DeadEnds.Add(new DeadEnd(i, length, content));
                if (deadEnds.minLength > 0 && !content && length >= deadEnds.minLength)
                    violations++;
            }
            layout.DeadEndViolations = violations;
        }

        int[] Steps(int source)
        {
            var steps = new int[layout.Placements.Count];
            for (int i = 0; i < steps.Length; i++)
                steps[i] = -1;
            steps[source] = 0;
            frontier.Clear();
            frontier.Enqueue(source);
            while (frontier.Count > 0)
            {
                int at = frontier.Dequeue();
                foreach (int n in neighbours[at])
                {
                    if (steps[n] >= 0)
                        continue;
                    steps[n] = steps[at] + 1;
                    frontier.Enqueue(n);
                }
            }
            return steps;
        }

        void Join(SocketRef existing, SocketRef added)
        {
            layout.Connections.Add(new Connection(existing, added));
            open.Remove(existing);
            stuck.Remove(existing);
            if (!neighbours[existing.placement].Contains(added.placement))
            {
                neighbours[existing.placement].Add(added.placement);
                neighbours[added.placement].Add(existing.placement);
            }
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

        // ---- Rules --------------------------------------------------------------------------------------------

        bool Allowed(int piece, int depth, int parent)
        {
            var d = pieces[piece];
            if ((d.maxCount > 0 && counts[piece] >= d.maxCount) || depth < d.minDepth)
                return false;

            for (int r = 0; r < tagRules.Length; r++)
            {
                var rule = tagRules[r];
                if (!d.HasTag(rule.tag))
                    continue;
                if ((rule.maxCount > 0 && tagCounts[r] >= rule.maxCount) || depth < rule.minDepth)
                    return false;
                if (rule.depthStep > 0)
                    foreach (var p in layout.Placements)
                        if (pieces[p.piece].HasTag(rule.tag) && Mathf.Abs(p.depth - depth) < rule.depthStep)
                            return false;
            }
            foreach (var (tags, minSteps) in spacing)
                if (d.HasAnyTag(tags) && !FarEnough(tags, minSteps, parent))
                    return false;
            return true;
        }

        // Steps from a new piece (one past its parent) to every placed piece with any of the tags.
        bool FarEnough(int[] tags, int minSteps, int parent)
        {
            bool any = false;
            foreach (var p in layout.Placements)
                any |= pieces[p.piece].HasAnyTag(tags);
            if (!any)
                return true;
            DistancesFrom(parent);
            for (int i = 0; i < layout.Placements.Count; i++)
            {
                if (!pieces[layout.Placements[i].piece].HasAnyTag(tags))
                    continue;
                int d = distances[i];
                if (d >= 0 && d + 1 < minSteps)
                    return false;
            }
            return true;
        }

        void DistancesFrom(int source)
        {
            if (distancesFrom == source)
                return;
            distancesFrom = source;
            distances.Clear();
            distances.AddRange(Steps(source));
        }

        bool IsOwed(int piece)
        {
            var d = pieces[piece];
            if (counts[piece] < d.minCount)
                return true;
            for (int r = 0; r < tagRules.Length; r++)
                if (tagCounts[r] < tagRules[r].minCount && d.HasTag(tagRules[r].tag))
                    return true;
            return false;
        }

        // Pieces still owed by growth (normal pieces, and tags a normal piece can pay off).
        int Owed()
        {
            int owed = 0;
            for (int p = 0; p < pieces.Count; p++)
                if (pieces[p].role == PieceRole.Normal)
                    owed += Mathf.Max(0, pieces[p].minCount - counts[p]);
            for (int r = 0; r < tagRules.Length; r++)
                if (tagRuleGrows[r])
                    owed += Mathf.Max(0, tagRules[r].minCount - tagCounts[r]);
            return owed;
        }

        // Owed pieces get a weight even if their weight is 0, so a minimum can always be met.
        float Weight(int piece)
        {
            var d = pieces[piece];
            if (d.weight > 0f)
                return d.weight;
            return IsOwed(piece) ? 1f : 0f;
        }

        // ---- Helpers ------------------------------------------------------------------------------------------

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
