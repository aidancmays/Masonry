using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// What kind of space a piece is, such as Graveyard, Mausoleum or Arena. The Piece Set's boundary rules pick the
    /// boundary between two pieces from their module types: two graveyards may run into each other with nothing in
    /// between, while a graveyard meeting a mausoleum gets a fence.
    /// </summary>
    [CreateAssetMenu(menuName = "Masonry/Module Type", fileName = "Module Type", order = 303)]
    public sealed class ModuleType : ScriptableObject
    {
        [Tooltip("Where two pieces meet, the boundary is drawn once, by the piece whose module type has the higher priority (a tie goes to the piece placed first). Give buildings a higher priority than open ground so their walls are the ones kept.")]
        [SerializeField] int priority;
        [Tooltip("Colour in the Workbench maps.")]
        [SerializeField] Color color = new(0.45f, 0.6f, 0.75f, 1f);

        public int Priority => priority;
        public Color Color => color;
    }
}
