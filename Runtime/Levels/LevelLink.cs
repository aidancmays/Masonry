using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// Leads from this piece into another level built from another Piece Set, such as a crypt entrance whose stairs
    /// go down to the crypts. Put it on a child of the piece where the other level's arrival piece should stand: its
    /// position and facing become that piece's origin and forward. Every link to the same set in a layout feeds one
    /// shared level, grown from all of them at once.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LevelLink : MonoBehaviour
    {
        [Tooltip("Piece Set of the level this leads to.")]
        [SerializeField] PieceSet target;
        [Tooltip("Piece placed here. Must be in the target set. Empty = one of the set's Arrival pieces (else a Start piece).")]
        [SerializeField] Piece arrival;

        public PieceSet Target
        {
            get => target;
            set => target = value;
        }

        public Piece Arrival
        {
            get => arrival;
            set => arrival = value;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.9f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, Quaternion.LookRotation(Flat(transform.forward), Vector3.up), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(1.5f, 0.1f, 1.5f));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * 1.5f);
            Gizmos.DrawLine(Vector3.zero, Vector3.up * 3f);
            Gizmos.matrix = Matrix4x4.identity;
        }

        static Vector3 Flat(Vector3 v)
        {
            v.y = 0f;
            return v.sqrMagnitude > 1e-6f ? v.normalized : Vector3.forward;
        }
    }
}
