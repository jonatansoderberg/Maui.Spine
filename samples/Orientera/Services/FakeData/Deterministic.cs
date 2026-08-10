namespace Orientera.Services.FakeData;

/// <summary>
/// Stable pseudo-randomness for the demo seed. <see cref="string.GetHashCode()"/> is
/// randomised per process, so anything derived from it would produce a different calendar on
/// every launch — this uses FNV-1a instead, keeping the dataset identical across runs,
/// platforms and test sessions.
/// </summary>
internal static class Deterministic
{
    public static int Seed(params string[] parts)
    {
        unchecked
        {
            const uint offsetBasis = 2166136261;
            const uint prime = 16777619;

            uint hash = offsetBasis;

            foreach (string part in parts)
            {
                foreach (char c in part)
                {
                    hash ^= c;
                    hash *= prime;
                }

                hash ^= '|';
                hash *= prime;
            }

            return (int)(hash & 0x7FFFFFFF);
        }
    }

    public static Random For(params string[] parts) => new(Seed(parts));

    /// <summary>A stable value in [min, max).</summary>
    public static double Between(double min, double max, params string[] parts) =>
        min + (For(parts).NextDouble() * (max - min));
}
