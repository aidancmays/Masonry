using System;
using System.Collections.Generic;
using UnityEngine;

namespace Masonry
{
    /// <summary>How the generator may use a piece.</summary>
    public enum PieceRole
    {
        /// <summary>Grows the layout. Placed on open sockets until the piece budget is spent.</summary>
        Normal,
        /// <summary>The first piece, placed at the builder's origin.</summary>
        Start,
        /// <summary>Only used at the end, to close sockets that are still open (dead ends, walls, alcoves).</summary>
        Cap,
        /// <summary>
        /// Placed only where another level leads into this one (a <see cref="LevelLink"/>), e.g. the foot of a
        /// stairwell. Never used as the start of the level on its own.
        /// </summary>
        Arrival,
    }

    /// <summary>Where one start piece of a layout goes, relative to the layout origin.</summary>
    public struct LayoutStart
    {
        public Vector3 position;
        public float yaw;
        /// <summary>Piece index to place, or -1 to pick an Arrival piece (else a Start piece).</summary>
        public int piece;

        public LayoutStart(Vector3 position, float yaw, int piece = -1)
        {
            this.position = position;
            this.yaw = yaw;
            this.piece = piece;
        }
    }

    /// <summary>Size and retry rules for a layout.</summary>
    [Serializable]
    public struct LayoutSettings
    {
        [Tooltip("Fewest pieces, counting the start piece but not caps.")]
        [Min(1)] public int minPieces;
        [Tooltip("Most pieces, counting the start piece but not caps.")]
        [Min(1)] public int maxPieces;
        [Tooltip("Layouts tried before giving up on the minimum counts. Every try is seeded, so the result stays deterministic.")]
        [Min(1)] public int attempts;
        [Tooltip("Pieces may overlap by this much (metres) without colliding, so touching faces are fine.")]
        [Min(0f)] public float overlapTolerance;
        [Tooltip("Open sockets this close (metres) and facing each other are joined, which closes loops.")]
        [Min(0.001f)] public float joinDistance;

        public static LayoutSettings Default => new()
        {
            minPieces = 8,
            maxPieces = 14,
            attempts = 25,
            overlapTolerance = 0.05f,
            joinDistance = 0.05f,
        };
    }

    /// <summary>A connection point in piece space. Yaw is the direction the socket faces, out of the piece.</summary>
    public struct SocketDefinition
    {
        public Vector3 position;
        public float yaw;
        /// <summary>Socket type id. Which ids connect is decided by the generator's compatibility function.</summary>
        public int type;

        public SocketDefinition(Vector3 position, float yaw, int type = 0)
        {
            this.position = position;
            this.yaw = yaw;
            this.type = type;
        }
    }

    /// <summary>Engine-free description of a piece: what the generator reasons about.</summary>
    public sealed class PieceDefinition
    {
        public string name;
        /// <summary>Space the piece occupies, in piece space. Used for overlap tests.</summary>
        public Bounds bounds;
        public SocketDefinition[] sockets = Array.Empty<SocketDefinition>();
        public PieceRole role;
        public float weight = 1f;
        public int minCount;
        /// <summary>0 = no limit.</summary>
        public int maxCount;
        /// <summary>Fewest steps from the start piece (the start piece is depth 0).</summary>
        public int minDepth;
        /// <summary>Tag ids, counted by <see cref="TagRule"/>s.</summary>
        public int[] tags = Array.Empty<int>();

        public bool HasTag(int tag) => Array.IndexOf(tags, tag) >= 0;

        public bool HasAnyTag(int[] any)
        {
            if (any == null)
                return false;
            foreach (int t in any)
                if (Array.IndexOf(tags, t) >= 0)
                    return true;
            return false;
        }

        public override string ToString() => name;
    }

    /// <summary>
    /// Limits on every piece carrying a tag, whichever piece it is. For example "exactly 3 Objective pieces, at
    /// least 4 steps from the start and 3 steps apart", where puzzles and combat rooms both carry Objective.
    /// </summary>
    public struct TagRule
    {
        public int tag;
        public int minCount;
        /// <summary>0 = no limit.</summary>
        public int maxCount;
        /// <summary>Fewest steps from the start piece.</summary>
        public int minDepth;
        /// <summary>Fewest steps between two pieces with this tag (1 = may touch, 0 = no rule).</summary>
        public int minSpacing;
        /// <summary>
        /// Pieces with this tag must sit at least this many steps deeper or shallower than each other (0 = no rule),
        /// so a run of objectives leads further and further into the level.
        /// </summary>
        public int depthStep;
    }

    /// <summary>Keeps every piece carrying any of these tags at least <see cref="minSteps"/> steps from every other.</summary>
    public struct SpacingRule
    {
        public int[] tags;
        public int minSteps;
    }

    /// <summary>
    /// Long dead ends must end in something: a dead end at the end of a corridor of at least <see cref="minLength"/>
    /// pieces (counted back to the nearest junction or start) has to be a piece with one of <see cref="tags"/>.
    /// The generator prefers such caps there, and layouts that still break the rule are retried.
    /// </summary>
    public struct DeadEndRule
    {
        /// <summary>0 = no rule.</summary>
        public int minLength;
        public int[] tags;
    }

    /// <summary>A dead end in a layout: a piece with at most one neighbour.</summary>
    public readonly struct DeadEnd
    {
        public readonly int placement;
        /// <summary>Pieces in the corridor that ends here, back to the nearest junction or start.</summary>
        public readonly int length;
        /// <summary>True when the piece carries one of the dead-end rule's tags.</summary>
        public readonly bool hasContent;

        public DeadEnd(int placement, int length, bool hasContent)
        {
            this.placement = placement;
            this.length = length;
            this.hasContent = hasContent;
        }
    }

    /// <summary>One placed piece. Position and yaw are relative to the layout origin.</summary>
    public readonly struct Placement
    {
        public readonly int piece;
        public readonly Vector3 position;
        public readonly float yaw;
        public readonly int depth;

        public Placement(int piece, Vector3 position, float yaw, int depth)
        {
            this.piece = piece;
            this.position = position;
            this.yaw = yaw;
            this.depth = depth;
        }

        public Quaternion Rotation => Quaternion.Euler(0f, yaw, 0f);
    }

    /// <summary>A socket on a placed piece.</summary>
    public readonly struct SocketRef : IEquatable<SocketRef>
    {
        public readonly int placement;
        public readonly int socket;

        public SocketRef(int placement, int socket)
        {
            this.placement = placement;
            this.socket = socket;
        }

        public bool Equals(SocketRef other) => placement == other.placement && socket == other.socket;
        public override bool Equals(object obj) => obj is SocketRef other && Equals(other);
        public override int GetHashCode() => (placement * 397) ^ socket;
        public override string ToString() => $"{placement}:{socket}";
    }

    /// <summary>Two sockets joined together.</summary>
    public readonly struct Connection
    {
        public readonly SocketRef a;
        public readonly SocketRef b;

        public Connection(SocketRef a, SocketRef b)
        {
            this.a = a;
            this.b = b;
        }
    }

    /// <summary>Result of <see cref="LayoutGenerator.Generate"/>.</summary>
    public sealed class Layout
    {
        public int Seed { get; internal set; }
        /// <summary>Which attempt produced this layout (0 = the first).</summary>
        public int Attempt { get; internal set; }
        /// <summary>True when the minimum piece count and every piece and tag minimum were met.</summary>
        public bool Valid { get; internal set; }

        /// <summary>The piece definitions the placements index into.</summary>
        public IReadOnlyList<PieceDefinition> Pieces { get; internal set; } = Array.Empty<PieceDefinition>();

        public List<Placement> Placements { get; } = new();
        public List<Connection> Connections { get; } = new();
        /// <summary>Sockets still unconnected after capping.</summary>
        public List<SocketRef> OpenSockets { get; } = new();

        /// <summary>Every dead end (start pieces left out), with how long its corridor is.</summary>
        public List<DeadEnd> DeadEnds { get; } = new();

        /// <summary>Long dead ends that break the dead-end rule (0 in a valid layout).</summary>
        public int DeadEndViolations { get; internal set; }

        public int CountWithTag(int tag)
        {
            int n = 0;
            foreach (var p in Placements)
                if (Pieces[p.piece].HasTag(tag))
                    n++;
            return n;
        }

        public int CountOf(int piece)
        {
            int n = 0;
            foreach (var p in Placements)
                if (p.piece == piece)
                    n++;
            return n;
        }
    }
}
