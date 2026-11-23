using System;
using System.Collections.Generic;
using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// One stretch of a piece's edge (usually one grid cell wide) that can show a fence, wall or nothing, depending
    /// on what the layout puts on the other side. Place it like a <see cref="Socket"/>: on the edge of the bounds,
    /// blue axis pointing out, red axis along the edge. Put the art for each style under it.
    /// </summary>
    /// <remarks>
    /// After a build, two boundaries facing each other at the same spot are one shared edge: the Piece Set's rules
    /// pick a style from the two module types, and only one side draws it, so walls are never doubled. A boundary
    /// with nothing opposite uses the rule for the edge of the layout.
    /// If the edge holds a socket, the style's art should leave the opening clear; <see cref="Variant.closedOpening"/>
    /// fills it when the socket ends up unjoined.
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class Boundary : MonoBehaviour
    {
        [Serializable]
        public sealed class Variant
        {
            public BoundaryStyle style;
            [Tooltip("Shown when this edge uses the style and this side draws it.")]
            public GameObject art;
            [Tooltip("Shown with the art when this edge's socket was not joined, to close the opening. Optional.")]
            public GameObject closedOpening;
        }

        [Tooltip("Length of the edge in metres (one cell). Used for the gizmo.")]
        [SerializeField, Min(0.1f)] float length = 10f;
        [Tooltip("The socket in this edge, if any. Found automatically when left empty.")]
        [SerializeField] Socket socket;
        [SerializeField] List<Variant> variants = new();

        bool socketSearched;

        public float Length
        {
            get => length;
            set => length = value;
        }

        public List<Variant> Variants => variants;

        /// <summary>The socket in this edge: the assigned one, or one of the piece's sockets at this spot.</summary>
        public Socket Socket
        {
            get
            {
                if (socket == null && !socketSearched)
                {
                    socketSearched = true;
                    var piece = GetComponentInParent<Piece>();
                    if (piece != null)
                        foreach (var s in piece.Sockets)
                            if (Vector3.Distance(Flat(s.transform.position), Flat(transform.position)) < 0.05f)
                                socket = s;
                }
                return socket;
            }
            set => socket = value;
        }

        /// <summary>Style chosen by the last build (null = nothing).</summary>
        public BoundaryStyle Style { get; private set; }

        /// <summary>The boundary facing this one in the last build, or null at the edge of the layout.</summary>
        public Boundary Neighbour { get; internal set; }

        /// <summary>True when this side draws the shared edge.</summary>
        public bool Drawn { get; private set; }

        /// <summary>Shows the art for a style on this side, or hides everything.</summary>
        public void Apply(BoundaryStyle style, bool drawHere)
        {
            Style = style;
            Drawn = drawHere && style != null;
            bool open = Socket != null && !Socket.IsJoined;
            foreach (var v in variants)
            {
                bool on = Drawn && v.style == style;
                if (v.art != null)
                    v.art.SetActive(on);
                if (v.closedOpening != null)
                    v.closedOpening.SetActive(on && open);
            }
        }

        /// <summary>Hides every variant (before a build decides).</summary>
        public void HideAll() => Apply(null, false);

        void OnDrawGizmos()
        {
            Color color = Style != null ? Style.Color : new Color(0.7f, 0.7f, 0.7f, 0.6f);
            if (Application.isPlaying && !Drawn)
                color.a *= 0.25f;
            Gizmos.color = color;
            var right = transform.right * (length / 2f);
            var p = transform.position + Vector3.up * 0.05f;
            Gizmos.DrawLine(p - right, p + right);
            Gizmos.DrawLine(p, p + transform.forward * 0.4f);
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);
    }
}
