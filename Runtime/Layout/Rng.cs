namespace Masonry
{
    /// <summary>
    /// Small seeded random number generator (SplitMix64). Used instead of <c>System.Random</c> or
    /// <c>UnityEngine.Random</c> so a seed gives the same sequence on every machine, runtime and Unity version.
    /// </summary>
    public sealed class Rng
    {
        ulong state;

        public Rng(int seed) => state = (ulong)(uint)seed * 0xD1B54A32D192ED03UL + 0x9E3779B97F4A7C15UL;

        public ulong NextULong()
        {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int NextInt() => (int)(NextULong() >> 33);

        /// <summary>Integer in [min, maxExclusive). Returns min when the range is empty.</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min)
                return min;
            return min + (int)(NextULong() % (ulong)(maxExclusive - min));
        }

        /// <summary>Float in [0, 1).</summary>
        public float Value() => (NextULong() >> 40) / 16777216f;
    }
}
