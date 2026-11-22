using UnityEngine;

namespace Masonry
{
    /// <summary>
    /// A box turned about the Y axis only, which is all a layout ever does to a piece. Overlap is a separating-axis
    /// test on the XZ plane plus a height check. Touching faces do not count as overlapping.
    /// </summary>
    public readonly struct OrientedBox
    {
        readonly Vector2 center;
        readonly Vector2 axisX;
        readonly Vector2 axisZ;
        readonly float extentX;
        readonly float extentZ;
        readonly float yMin;
        readonly float yMax;

        /// <param name="local">The box in piece space.</param>
        /// <param name="position">Piece position.</param>
        /// <param name="yaw">Piece yaw in degrees.</param>
        /// <param name="shrink">Taken off every side, so pieces may overlap this much.</param>
        public OrientedBox(Bounds local, Vector3 position, float yaw, float shrink = 0f)
        {
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            var c = position + rotation * local.center;
            var right = rotation * Vector3.right;
            var forward = rotation * Vector3.forward;

            center = new Vector2(c.x, c.z);
            axisX = new Vector2(right.x, right.z);
            axisZ = new Vector2(forward.x, forward.z);
            extentX = Mathf.Max(0f, local.extents.x - shrink);
            extentZ = Mathf.Max(0f, local.extents.z - shrink);
            float extentY = Mathf.Max(0f, local.extents.y - shrink);
            yMin = c.y - extentY;
            yMax = c.y + extentY;
        }

        public bool Overlaps(in OrientedBox other)
        {
            if (yMax <= other.yMin || other.yMax <= yMin)
                return false;
            var d = other.center - center;
            return !Separated(axisX, d, other)
                && !Separated(axisZ, d, other)
                && !Separated(other.axisX, d, other)
                && !Separated(other.axisZ, d, other);
        }

        bool Separated(Vector2 axis, Vector2 d, in OrientedBox other)
        {
            float ra = extentX * Mathf.Abs(Vector2.Dot(axisX, axis)) + extentZ * Mathf.Abs(Vector2.Dot(axisZ, axis));
            float rb = other.extentX * Mathf.Abs(Vector2.Dot(other.axisX, axis)) + other.extentZ * Mathf.Abs(Vector2.Dot(other.axisZ, axis));
            return Mathf.Abs(Vector2.Dot(d, axis)) >= ra + rb;
        }
    }
}
