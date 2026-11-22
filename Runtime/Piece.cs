using System.Collections.Generic;
using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// Root of a modular piece prefab. Holds the space the piece takes up (used so pieces never overlap) and finds
    /// its <see cref="Socket"/>s among its children. Pieces are only ever turned about Y, so model them upright.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Piece : MonoBehaviour
    {
        [Tooltip("Space the piece occupies, in the piece's local space. Other pieces may not overlap it.")]
        [SerializeField] Bounds bounds = new(new Vector3(0f, 2f, 0f), new Vector3(10f, 4f, 10f));

        Socket[] sockets;

        public Bounds Bounds
        {
            get => bounds;
            set => bounds = value;
        }

        /// <summary>Sockets in hierarchy order. That order is the socket index the generator uses.</summary>
        public Socket[] Sockets => sockets ??= GetComponentsInChildren<Socket>(true);

        /// <summary>Index of this piece in the builder's layout, or -1 when not built by one.</summary>
        public int PlacementIndex { get; internal set; } = -1;

        /// <summary>Steps from the start piece in the built layout (start is 0).</summary>
        public int Depth { get; internal set; }

        /// <summary>Forgets cached sockets, after adding or removing sockets by script.</summary>
        public void RefreshSockets() => sockets = null;

        /// <summary>Sets the bounds to cover every renderer under the piece, in local space.</summary>
        public void FitBoundsToRenderers()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            bool any = false;
            var result = new Bounds();
            var toLocal = transform.worldToLocalMatrix;
            foreach (var r in renderers)
            {
                var b = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = toLocal.MultiplyPoint3x4(corner);
                    if (!any)
                    {
                        result = new Bounds(local, Vector3.zero);
                        any = true;
                    }
                    else
                    {
                        result.Encapsulate(local);
                    }
                }
            }
            if (any)
                bounds = result;
        }

        /// <summary>Builds the generator's description of this piece.</summary>
        public PieceDefinition ToDefinition(System.Func<SocketType, int> typeId)
        {
            var found = GetComponentsInChildren<Socket>(true);
            var list = new List<SocketDefinition>(found.Length);
            var root = transform;
            foreach (var s in found)
            {
                var localPosition = root.InverseTransformPoint(s.transform.position);
                var localForward = root.InverseTransformDirection(s.transform.forward);
                float yaw = Mathf.Atan2(localForward.x, localForward.z) * Mathf.Rad2Deg;
                list.Add(new SocketDefinition(localPosition, yaw, typeId(s.Type)));
            }
            return new PieceDefinition
            {
                name = name,
                bounds = bounds,
                sockets = list.ToArray(),
            };
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(bounds.center, bounds.size);
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}
