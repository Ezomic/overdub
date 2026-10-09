namespace Overdub.Audio;

public sealed record ChainPreset(string Name, string Group, double[]? Amp, double[]? Eq, double[]? Compressor, double[]? Reverb)
{
    public IReadOnlyList<double[]?> Slots => [Amp, Eq, Compressor, Reverb];

    public void ApplyTo(EffectChain chain)
    {
        var slots = Slots;
        for (var i = 0; i < slots.Count && i < chain.Effects.Count; i++)
        {
            var effect = chain.Effects[i];
            var values = slots[i];
            if (values is null)
            {
                effect.Enabled = false;
                continue;
            }

            for (var p = 0; p < Math.Min(values.Length, effect.Values.Length); p++)
            {
                effect.Set(p, values[p]);
            }

            effect.Enabled = true;
        }

        chain.Touch();
    }
}

public static class EffectPresets
{
    public static IReadOnlyList<ChainPreset> All { get; } =
    [
        new("Clean and roomy", "Guitar", [0, 0.2, 0, 0, 1, 0.35, 1, 0], null, [-20, 2, 15, 200, 2], [0.6, 0.5, 0.25]),
        new("Edge of breakup", "Guitar", [1, 0.3, 1, 1, 1, 0.4, 1, 0], null, [-18, 2.5, 10, 150, 2], [0.4, 0.5, 0.15]),
        new("Crunch rhythm", "Guitar", [1, 0.6, 2, 0, 2, 0.45, 1, -1], [0, -1, 800, 1], null, [0.3, 0.6, 0.1]),
        new("High-gain lead", "Guitar", [2, 0.8, 1, 3, 0, 0.55, 1, -3], [0, 2, 1200, 0], [-24, 4, 5, 120, 3], [0.7, 0.4, 0.3]),
        new("Ambient clean", "Guitar", [0, 0.1, 0, -1, 2, 0.3, 1, 0], null, [-22, 3, 20, 300, 3], [1, 0.3, 0.5]),
        new("Acoustic strum", "Guitar", null, [-2, -1, 400, 3], [-18, 2, 10, 150, 2], [0.5, 0.5, 0.2]),
        new("Bass DI", "Bass", null, [1, 0, 800, 0], [-18, 4, 10, 150, 4], null),
        new("Warm bass amp", "Bass", [3, 0.25, 3, -1, 0, 0.1, 1, 0], null, [-18, 4, 15, 150, 3], null),
        new("Bass grit", "Bass", [3, 0.6, 2, 2, 1, 0.3, 1, -2], null, [-20, 5, 10, 120, 4], null),
        new("Slap bright", "Bass", [3, 0.2, 3, -3, 4, 0.4, 1, 0], null, [-22, 6, 3, 100, 5], [0.3, 0.6, 0.08]),
        new("Motown round", "Bass", [3, 0.15, 4, 0, -4, 0, 1, 0], [2, 0, 500, -4], [-16, 3, 20, 200, 3], null),
        new("Fuzz bass", "Bass", [2, 0.9, 4, 0, -2, 0.2, 1, -4], [3, 0, 700, -2], [-20, 4, 5, 150, 2], null),
        new("Keys polish", "Other", null, [0, 0, 1000, 2], [-20, 2, 20, 200, 2], [0.6, 0.5, 0.2]),
        new("Tight and dry", "Other", null, null, [-18, 3, 5, 100, 2], null),
        new("Lo-fi", "Other", [1, 0.4, -6, 3, -8, 0, 1, -2], [-4, 3, 1500, -8], [-24, 8, 5, 100, 6], [0.3, 0.9, 0.1]),
        new("All off", "Other", null, null, null, null),
    ];

    public static IEnumerable<string> Groups => All.Select(p => p.Group).Distinct();
}
