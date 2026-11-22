using System.Collections.Generic;
using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// A kind of connection, such as a 3 m path or a crypt doorway. Sockets join when they share a type, or when
    /// either type lists the other under <see cref="ConnectsTo"/>. Sockets with no type join only each other.
    /// </summary>
    [CreateAssetMenu(menuName = "Masonry/Socket Type", fileName = "Socket Type", order = 300)]
    public sealed class SocketType : ScriptableObject
    {
        [Tooltip("Other types this one may join, besides itself. Joining works both ways.")]
        [SerializeField] List<SocketType> connectsTo = new();
        [Tooltip("Width of the opening, only used to draw the socket in the Scene view.")]
        [SerializeField, Min(0f)] float gizmoWidth = 2f;
        [SerializeField] Color gizmoColor = new(1f, 0.75f, 0.2f, 1f);

        public IReadOnlyList<SocketType> ConnectsTo => connectsTo;
        public float GizmoWidth => gizmoWidth;
        public Color GizmoColor => gizmoColor;

        public static bool CanJoin(SocketType a, SocketType b)
        {
            if (a == b)
                return true;
            if (a == null || b == null)
                return false;
            return a.connectsTo.Contains(b) || b.connectsTo.Contains(a);
        }
    }
}
