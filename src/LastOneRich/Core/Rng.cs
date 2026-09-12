namespace LastOneRich.Core;

/// <summary>Shared RNG. Seedable so the headless simulator produces reproducible races.</summary>
public static class Rng
{
    public static Random Shared { get; private set; } = new(1234);

    public static void SetSeed(int seed) => Shared = new Random(seed);

    public static float Float() => (float)Shared.NextDouble();
    public static float Range(float a, float b) => a + (b - a) * Float();
    public static bool Chance(float p) => Float() < p;
    public static int Int(int maxExclusive) => Shared.Next(maxExclusive);
}
