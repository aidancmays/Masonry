using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// A kind of boundary, such as Fence, Wall or Hedge. Each <see cref="Boundary"/> on a piece holds the art for the
    /// styles it supports, and the Piece Set's boundary rules say which style goes between which module types.
    /// No style (an empty rule) means nothing is drawn.
    /// </summary>
    [CreateAssetMenu(menuName = "Masonry/Boundary Style", fileName = "Boundary Style", order = 304)]
    public sealed class BoundaryStyle : ScriptableObject
    {
        [Tooltip("Colour of the edge gizmo and in the Workbench.")]
        [SerializeField] Color color = new(0.8f, 0.8f, 0.8f, 1f);

        public Color Color => color;
    }
}
