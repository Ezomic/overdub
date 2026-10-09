namespace Overdub.Audio;

public enum MelodyScale
{
    Major,
    Minor,
    MinorPentatonic,
    MajorPentatonic,
    Blues,
}

public enum MelodyDensity
{
    Sparse,
    Medium,
    Busy,
}

public static class MelodyGenerator
{
    private static readonly int[][] Offsets =
    [
        [0, 2, 4, 5, 7, 9, 11],
        [0, 2, 3, 5, 7, 8, 10],
        [0, 3, 5, 7, 10],
        [0, 2, 4, 7, 9],
        [0, 3, 5, 6, 7, 10],
    ];

    private static readonly int[][] Lengths =
    [
        [2, 4, 4, 6, 8],
        [1, 2, 2, 2, 3, 4],
        [1, 1, 1, 2, 2, 3],
    ];

    private static readonly double[] RestChance = [0.22, 0.14, 0.07];

    private static readonly (int Move, int Weight)[] Moves = [(0, 8), (1, 28), (-1, 28), (2, 14), (-2, 14), (3, 4), (-3, 4)];

    public static string ScaleName(MelodyScale scale) => scale switch
    {
        MelodyScale.MinorPentatonic => "Minor pentatonic",
        MelodyScale.MajorPentatonic => "Major pentatonic",
        _ => scale.ToString(),
    };

    private readonly record struct Note(int Start, int Length, int Index);

    public static MidiEvent[] Generate(int sampleRate, double bpm, int beatsPerBar, int keyRoot, MelodyScale scale, int bars, MelodyDensity density, int seed)
    {
        var random = new Random(seed);
        var eighth = sampleRate * 60.0 / bpm / 2;
        var stepsPerBar = beatsPerBar * 2;
        var pitches = Enumerable.Range(55, 30).Where(n => Offsets[(int)scale].Contains(((n - keyRoot) % 12 + 12) % 12)).ToArray();
        var rootIndexes = Enumerable.Range(0, pitches.Length).Where(i => ((pitches[i] - keyRoot) % 12 + 12) % 12 == 0).ToArray();
        var cursor = Math.Clamp(Array.FindIndex(pitches, p => p >= 64), 1, pitches.Length - 2);

        var motif = MakeBar(random, stepsPerBar, density, pitches.Length, ref cursor, pitches, keyRoot);
        var events = new List<MidiEvent>();
        var total = (long)Math.Round(bars * stepsPerBar * eighth);
        events.Add(new MidiEvent(0, 0, 0, MidiKind.Sustain, 0));
        events.Add(new MidiEvent(total, 0, 0, MidiKind.Sustain, 0));
        var fresh = motif;
        for (var bar = 0; bar < bars; bar++)
        {
            List<Note> notes;
            switch (bar % 4)
            {
                case 0:
                    notes = motif;
                    break;
                case 2:
                    fresh = MakeBar(random, stepsPerBar, density, pitches.Length, ref cursor, pitches, keyRoot);
                    notes = fresh;
                    break;
                default:
                    notes = Vary(random, bar % 4 == 1 ? motif : fresh, pitches.Length);
                    break;
            }

            if (bar == bars - 1 && notes.Count > 0)
            {
                notes = Resolve(notes, rootIndexes, stepsPerBar);
            }

            foreach (var note in notes)
            {
                var at = ((bar * stepsPerBar) + note.Start) * eighth;
                var length = note.Length * eighth * 0.92;
                var accent = note.Start == 0 ? 104 : note.Start % 2 == 0 ? 92 : 78 + random.Next(0, 10);
                var pitch = (byte)pitches[note.Index];
                var start = (long)Math.Round(at);
                events.Add(new MidiEvent(start, pitch, (byte)accent));
                events.Add(new MidiEvent(Math.Min(total, start + Math.Max(1, (long)length)), pitch, 0));
            }
        }

        return events.ToArray();
    }

    private static List<Note> MakeBar(Random random, int steps, MelodyDensity density, int count, ref int cursor, int[] pitches, int keyRoot)
    {
        var notes = new List<Note>();
        var lengths = Lengths[(int)density];
        var step = 0;
        while (step < steps)
        {
            var length = Math.Min(lengths[random.Next(lengths.Length)], steps - step);
            var rest = step > 0 && random.NextDouble() < RestChance[(int)density];
            if (!rest)
            {
                cursor = Walk(random, cursor, count, step % 2 == 0, pitches, keyRoot);
                notes.Add(new Note(step, length, cursor));
            }

            step += length;
        }

        return notes;
    }

    private static int Walk(Random random, int index, int count, bool strong, int[] pitches, int keyRoot)
    {
        var roll = random.Next(Moves.Sum(m => m.Weight));
        var move = 0;
        foreach (var (candidate, weight) in Moves)
        {
            if (roll < weight)
            {
                move = candidate;
                break;
            }

            roll -= weight;
        }

        var next = index + move;
        if (next < 0 || next >= count)
        {
            next = index - move;
        }

        next = Math.Clamp(next, 0, count - 1);
        if (strong && random.NextDouble() < 0.6)
        {
            for (var distance = 0; distance <= 2; distance++)
            {
                foreach (var candidate in new[] { next - distance, next + distance })
                {
                    if (candidate >= 0 && candidate < count && IsChordTone(pitches[candidate], keyRoot))
                    {
                        return candidate;
                    }
                }
            }
        }

        return next;
    }

    private static bool IsChordTone(int pitch, int keyRoot) => (((pitch - keyRoot) % 12 + 12) % 12) is 0 or 3 or 4 or 7;

    private static List<Note> Vary(Random random, List<Note> source, int count)
    {
        var copy = source.ToList();
        for (var i = Math.Max(0, copy.Count - 2); i < copy.Count; i++)
        {
            var shift = random.Next(2) == 0 ? -1 : 1;
            copy[i] = copy[i] with { Index = Math.Clamp(copy[i].Index + shift, 0, count - 1) };
        }

        return copy;
    }

    private static List<Note> Resolve(List<Note> notes, int[] rootIndexes, int stepsPerBar)
    {
        var copy = notes.ToList();
        var last = copy[^1];
        var nearest = rootIndexes.OrderBy(r => Math.Abs(r - last.Index)).First();
        copy[^1] = new Note(last.Start, stepsPerBar - last.Start, nearest);
        return copy;
    }
}
