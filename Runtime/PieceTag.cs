using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// A label for a kind of piece, such as Objective, Puzzle or Combat. A piece can carry several. A Piece Set's tag
    /// rules then count every piece with the tag, so "exactly 3 Objectives" can be met by any mix of puzzle and
    /// combat pieces that are tagged Objective.
    /// </summary>
    [CreateAssetMenu(menuName = "Masonry/Piece Tag", fileName = "Piece Tag", order = 302)]
    public sealed class PieceTag : ScriptableObject
    {
        [Tooltip("Colour of pieces with this tag in the Workbench maps.")]
        [SerializeField] Color color = new(0.9f, 0.4f, 0.3f, 1f);

        public Color Color => color;
    }
}
