using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// A connection point on a <see cref="Piece"/>. Put it on a child of the piece, on the edge of the piece's
    /// bounds, with its blue (forward) axis pointing out of the piece and its up axis pointing up. Two sockets join
    /// by facing each other at the same spot.
    /// </summary>
    /// <remarks>
    /// After a build, each socket knows whether it was joined. Use <see cref="whenJoined"/> and
    /// <see cref="whenOpen"/> to switch art for each case, for example an archway when joined and a wall or fence
    /// when it was left open.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class Socket : MonoBehaviour
    {
        [SerializeField] SocketType type;
        [Tooltip("Turned on when this socket is joined to another piece, off otherwise.")]
        [SerializeField] GameObject[] whenJoined = System.Array.Empty<GameObject>();
        [Tooltip("Turned on when this socket was left open, off otherwise.")]
        [SerializeField] GameObject[] whenOpen = System.Array.Empty<GameObject>();

        public SocketType Type
        {
            get => type;
            set => type = value;
        }

        /// <summary>The socket this one was joined to by the last build, or null.</summary>
        public Socket JoinedTo { get; private set; }

        public bool IsJoined => JoinedTo != null;

        /// <summary>Records the result of a build and switches the joined / open objects.</summary>
        public void SetJoined(Socket other)
        {
            JoinedTo = other;
            foreach (var go in whenJoined)
                if (go != null)
                    go.SetActive(other != null);
            foreach (var go in whenOpen)
                if (go != null)
                    go.SetActive(other == null);
        }

        void OnDrawGizmos()
        {
            float width = type != null ? type.GizmoWidth : 1f;
            var color = type != null ? type.GizmoColor : Color.white;
            if (IsJoined)
                color.a *= 0.35f;

            Gizmos.color = color;
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.LookRotation(Flat(transform.forward), Vector3.up), Vector3.one);
            Gizmos.DrawWireCube(new Vector3(0f, 0.5f, 0f), new Vector3(width, 1f, 0.02f));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 0.75f);
            Gizmos.DrawLine(Vector3.forward * 0.75f, new Vector3(0.2f, 0f, 0.5f));
            Gizmos.DrawLine(Vector3.forward * 0.75f, new Vector3(-0.2f, 0f, 0.5f));
            Gizmos.matrix = Matrix4x4.identity;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }
    }
}
