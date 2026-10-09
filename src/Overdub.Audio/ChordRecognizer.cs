namespace Overdub.Audio;

public static class ChordRecognizer
{
    private sealed record Shape(ChordQuality Quality, int[] Tones, double Penalty);

    private static readonly Shape[] Shapes =
    [
        new(ChordQuality.Major, [0, 4, 7], 0),
        new(ChordQuality.Minor, [0, 3, 7], 0),
        new(ChordQuality.Seventh, [0, 4, 7, 10], 0.08),
        new(ChordQuality.MinorSeventh, [0, 3, 7, 10], 0.08),
        new(ChordQuality.MajorSeventh, [0, 4, 7, 11], 0.1),
        new(ChordQuality.Suspended, [0, 5, 7], 0.1),
    ];

    public static Chord? Recognize(double[] chroma)
    {
        var peak = chroma.Max();
        if (peak <= 0)
        {
            return null;
        }

        var c = chroma.Select(x => x / peak).ToArray();
        Chord? best = null;
        var bestScore = double.MinValue;
        for (var root = 0; root < 12; root++)
        {
            foreach (var shape in Shapes)
            {
                var inside = shape.Tones.Sum(t => c[(root + t) % 12]) / shape.Tones.Length;
                var outside = Enumerable.Range(0, 12).Where(i => !shape.Tones.Contains((i - root + 12) % 12)).Sum(i => c[i]) / (12 - shape.Tones.Length);
                var score = inside - (0.6 * outside) - shape.Penalty + (0.15 * c[root]);
                if (score > bestScore)
                {
                    bestScore = score;
                    best = new Chord(root, shape.Quality);
                }
            }
        }

        return bestScore < 0.25 ? null : best;
    }

    public static IReadOnlyList<Chord?> PerBar(float[] samples, int offset, int sampleRate, double barSamples, int bars)
    {
        var result = new List<Chord?>();
        for (var bar = 0; bar < bars; bar++)
        {
            var start = offset + (int)Math.Round(bar * barSamples);
            var length = (int)Math.Min(barSamples, Math.Max(0, samples.Length - start));
            result.Add(length < 8192 ? null : Recognize(AudioAnalyzer.Chroma(samples, start, length, sampleRate)));
        }

        return result;
    }
}
