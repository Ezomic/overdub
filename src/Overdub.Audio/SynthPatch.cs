namespace Overdub.Audio;

public sealed class SynthPatch
{
    private const int TableSize = 2048;

    public SynthPatch(string name, double[] harmonics, double[] detunes, float attack, float decay, float sustain, float release,
        float heldDecay, float cutoff, float velocityBrightness, float gain)
    {
        Name = name;
        Detunes = detunes;
        Attack = attack;
        Decay = decay;
        Sustain = sustain;
        Release = release;
        HeldDecay = heldDecay;
        Cutoff = cutoff;
        VelocityBrightness = velocityBrightness;
        Gain = gain / detunes.Length;
        Table = BuildTable(harmonics);
    }

    public string Name { get; }
    public float[] Table { get; }
    public double[] Detunes { get; }
    public float Attack { get; }
    public float Decay { get; }
    public float Sustain { get; }
    public float Release { get; }
    public float HeldDecay { get; }
    public float Cutoff { get; }
    public float VelocityBrightness { get; }
    public float Gain { get; }

    public static IReadOnlyList<SynthPatch> All { get; } =
    [
        new("Lead", Saw(30), [1.0, 1.006], 0.005f, 0.15f, 0.6f, 0.3f, 0f, 0.25f, 0f, 1.0f),
        new("Piano", [1.0, 0.75, 0.5, 0.35, 0.25, 0.18, 0.12, 0.09, 0.06, 0.04], [1.0, 1.0012], 0.002f, 1.5f, 0.2f, 0.25f, 0.9f, 0.5f, 0.7f, 1.1f),
        new("Organ", [1.0, 0.6, 0.45, 0.3, 0.0, 0.2, 0.0, 0.12], [1.0], 0.01f, 0.01f, 1.0f, 0.08f, 0f, 0.6f, 0f, 0.9f),
        new("Pad", Saw(12), [0.996, 1.0, 1.004], 0.45f, 0.5f, 0.8f, 1.0f, 0f, 0.08f, 0.2f, 1.1f),
        new("Pluck", [1.0, 0.8, 0.6, 0.5, 0.4, 0.3, 0.25, 0.2, 0.15, 0.1], [1.0, 1.003], 0.002f, 0.35f, 0.0f, 0.12f, 0f, 0.35f, 0.5f, 1.2f),
        new("Bass", [1.0, 0.5, 0.25, 0.12, 0.06], [1.0], 0.005f, 0.2f, 0.7f, 0.15f, 0f, 0.12f, 0.3f, 1.3f),
    ];

    public static SynthPatch Named(string? name) =>
        All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    public static string Next(string? name)
    {
        var index = All.ToList().FindIndex(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        return All[(index + 1) % All.Count].Name;
    }

    private static double[] Saw(int partials) => Enumerable.Range(1, partials).Select(k => 1.0 / k).ToArray();

    private static float[] BuildTable(double[] harmonics)
    {
        var table = new float[TableSize + 1];
        var peak = 0.0;
        for (var i = 0; i < TableSize; i++)
        {
            var sum = 0.0;
            for (var h = 0; h < harmonics.Length; h++)
            {
                sum += harmonics[h] * Math.Sin(2 * Math.PI * (h + 1) * i / TableSize);
            }

            table[i] = (float)sum;
            peak = Math.Max(peak, Math.Abs(sum));
        }

        for (var i = 0; i < TableSize; i++)
        {
            table[i] = (float)(table[i] / peak);
        }

        table[TableSize] = table[0];
        return table;
    }
}
