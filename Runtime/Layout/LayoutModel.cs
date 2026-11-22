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

        public override string ToString() => name;
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
        /// <summary>True when every piece's minimum count and the minimum piece count were met.</summary>
        public bool Valid { get; internal set; }

        public List<Placement> Placements { get; } = new();
        public List<Connection> Connections { get; } = new();
        /// <summary>Sockets still unconnected after capping.</summary>
        public List<SocketRef> OpenSockets { get; } = new();

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
