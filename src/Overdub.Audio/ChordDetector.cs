namespace Overdub.Audio;

public static class ChordDetector
{
    private static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private sealed record Template(string Suffix, int[] Required, int[] Optional);

    private static readonly Template[] Templates =
    [
        new("", [0, 4], [7]),
        new("m", [0, 3], [7]),
        new("dim", [0, 3, 6], []),
        new("aug", [0, 4, 8], []),
        new("sus2", [0, 2], [7]),
        new("sus4", [0, 5], [7]),
        new("6", [0, 4, 9], [7]),
        new("m6", [0, 3, 9], [7]),
        new("7", [0, 4, 10], [7]),
        new("maj7", [0, 4, 11], [7]),
        new("m7", [0, 3, 10], [7]),
        new("m7b5", [0, 3, 6, 10], []),
        new("dim7", [0, 3, 6, 9], []),
        new("mMaj7", [0, 3, 11], [7]),
        new("add9", [0, 2, 4], [7]),
        new("9", [0, 2, 4, 10], [7]),
        new("maj9", [0, 2, 4, 11], [7]),
        new("m9", [0, 2, 3, 10], [7]),
        new("7sus4", [0, 5, 10], [7]),
    ];

    public static string? Name(IEnumerable<int> notes)
    {
        var sorted = notes.Distinct().OrderBy(n => n).ToList();
        if (sorted.Count == 0)
        {
            return null;
        }

        var bass = sorted[0] % 12;
        var classes = sorted.Select(n => n % 12).Distinct().ToList();
        if (classes.Count == 1)
        {
            return Names[classes[0]];
        }

        if (classes.Count == 2)
        {
            var gap = ((classes[1] - classes[0]) + 12) % 12;
            if (gap is 7 or 5)
            {
                var root = gap == 7 ? classes[0] : classes[1];
                return Names[root] + "5";
            }

            return null;
        }

        (double Score, string Text)? best = null;
        foreach (var root in classes)
        {
            var intervals = classes.Select(c => (c - root + 12) % 12).ToHashSet();
            foreach (var template in Templates)
            {
                var allowed = template.Required.Concat(template.Optional).ToHashSet();
                if (!template.Required.All(intervals.Contains) || !intervals.All(allowed.Contains))
                {
                    continue;
                }

                var score = template.Required.Length
                    - (0.2 * template.Optional.Count(o => !intervals.Contains(o)))
                    + (root == bass ? 1.5 : 0);
                var text = Names[root] + template.Suffix + (root == bass ? string.Empty : "/" + Names[bass]);
                if (best is null || score > best.Value.Score)
                {
                    best = (score, text);
                }
            }
        }

        return best?.Text;
    }
}
