namespace Overdub.Audio;

public static class NoteTranscriber
{
    private static int RefineOnset(float[] x, int frameStart, int window)
    {
        const int block = 128;
        var from = Math.Max(0, frameStart - (window / 2));
        var to = Math.Min(x.Length - block, frameStart + window);
        if (to <= from)
        {
            return frameStart;
        }

        var levels = new List<(int At, double Rms)>();
        for (var at = from; at <= to; at += block)
        {
            double sum = 0;
            for (var i = 0; i < block; i++)
            {
                sum += x[at + i] * x[at + i];
            }

            levels.Add((at, Math.Sqrt(sum / block)));
        }

        var peak = levels.Max(l => l.Rms);
        if (peak < 1e-4)
        {
            return frameStart;
        }

        foreach (var (at, rms) in levels)
        {
            if (rms >= 0.25 * peak)
            {
                return at;
            }
        }

        return frameStart;
    }

    public static IReadOnlyList<MidiNoteData> FromAudio(float[] samples, int offset, int length, int sampleRate, long startSample, double minHz = 70, int window = 2048)
    {
        var Window = window;
        var Hop = window / 4;
        var frames = new List<(long At, double Midi)?>();
        var buffer = new float[Window];
        for (var position = 0; position + Window <= length && offset + position + Window <= samples.Length; position += Hop)
        {
            Array.Copy(samples, offset + position, buffer, 0, Window);
            var reading = PitchDetector.Detect(buffer, sampleRate, minHz, 1200);
            frames.Add(reading is { Clarity: >= 0.85 } r ? (position, 69 + (12 * Math.Log2(r.Frequency / 440.0))) : null);
        }

        var notes = new List<MidiNoteData>();
        var run = new List<(long At, double Midi)>();
        var gap = 0;
        void Flush()
        {
            if (run.Count >= 4)
            {
                var sorted = run.Select(f => f.Midi).OrderBy(m => m).ToList();
                var pitch = (int)Math.Round(sorted[sorted.Count / 2]);
                var begin = startSample + (RefineOnset(samples, offset + (int)run[0].At, Window) - offset);
                var end = startSample + run[^1].At + (Window / 2) + (Hop / 2);
                notes.Add(new MidiNoteData(begin, end, (byte)Math.Clamp(pitch, 0, 127), 96));
            }

            run.Clear();
        }

        foreach (var frame in frames)
        {
            if (frame is { } f && (run.Count == 0 || Math.Abs(f.Midi - run.Average(x => x.Midi)) < 0.7))
            {
                run.Add(f);
                gap = 0;
            }
            else if (frame is null && run.Count > 0 && gap < 1)
            {
                gap++;
            }
            else
            {
                Flush();
                gap = 0;
                if (frame is { } restart)
                {
                    run.Add(restart);
                }
            }
        }

        Flush();
        return notes;
    }
}

public static class MelodyVariations
{
    public static readonly string[] Names = ["Up a third", "Down a third", "Mirror", "Reverse", "Fill in the gaps", "Answer phrase"];

    public static (int Root, MelodyScale Scale)? KeyOf(IReadOnlyList<MidiNoteData> notes)
    {
        var key = AudioAnalyzer.DetectKey(notes.Select(n => new MidiNote(n.Start, n.End, n.Pitch)));
        if (key is not { } found)
        {
            return null;
        }

        var parts = found.Name.Split(' ');
        var root = Array.IndexOf(Chord.Roots, parts[0]);
        return root < 0 ? null : (root, parts[1] == "minor" ? MelodyScale.Minor : MelodyScale.Major);
    }

    public static IReadOnlyList<MidiNoteData> Make(IReadOnlyList<MidiNoteData> notes, int kind, (int Root, MelodyScale Scale)? key)
    {
        if (notes.Count == 0)
        {
            return notes;
        }

        var scale = key is { } k ? MelodyGenerator.ScaleOffsets(k.Scale) : null;
        int Step(int pitch, int steps)
        {
            if (key is not { } kk || scale is null)
            {
                return pitch + (steps * 2);
            }

            var current = pitch;
            var direction = Math.Sign(steps);
            for (var moved = 0; moved < Math.Abs(steps);)
            {
                current += direction;
                if (scale.Contains((((current - kk.Root) % 12) + 12) % 12))
                {
                    moved++;
                }
            }

            return current;
        }

        var ordered = notes.OrderBy(n => n.Start).ToList();
        var first = ordered[0].Start;
        var last = ordered.Max(n => n.End);
        var result = new List<MidiNoteData>();
        switch (kind % Names.Length)
        {
            case 0:
                result.AddRange(ordered.Select(n => n with { Pitch = (byte)Math.Clamp(Step(n.Pitch, 2), 0, 127) }));
                break;
            case 1:
                result.AddRange(ordered.Select(n => n with { Pitch = (byte)Math.Clamp(Step(n.Pitch, -2), 0, 127) }));
                break;
            case 2:
                var anchor = ordered[0].Pitch;
                foreach (var n in ordered)
                {
                    var degrees = DegreeDistance(anchor, n.Pitch, key, scale);
                    result.Add(n with { Pitch = (byte)Math.Clamp(Step(anchor, -degrees), 0, 127) });
                }

                break;
            case 3:
                result.AddRange(ordered.Select(n => n with { Start = first + (last - n.End), End = first + (last - n.Start) }));
                break;
            case 4:
                for (var i = 0; i < ordered.Count; i++)
                {
                    var note = ordered[i];
                    var next = i + 1 < ordered.Count ? ordered[i + 1] : (MidiNoteData?)null;
                    if (next is { } n2 && Math.Abs(n2.Pitch - note.Pitch) >= 3 && n2.Start - note.Start >= (note.End - note.Start) * 0.9 && note.End - note.Start >= 2000)
                    {
                        var half = note.Start + ((n2.Start - note.Start) / 2);
                        var passing = Step(note.Pitch, n2.Pitch > note.Pitch ? 1 : -1);
                        result.Add(note with { End = half });
                        result.Add(new MidiNoteData(half, n2.Start, (byte)Math.Clamp(passing, 0, 127), (byte)(note.Velocity * 0.85)));
                    }
                    else
                    {
                        result.Add(note);
                    }
                }

                break;
            default:
                var half2 = first + ((last - first) / 2);
                var firstHalf = ordered.Where(n => n.Start < half2).ToList();
                result.AddRange(firstHalf);
                var offset = half2 - first;
                result.AddRange(firstHalf.Select(n => n with { Start = n.Start + offset, End = n.End + offset, Pitch = (byte)Math.Clamp(Step(n.Pitch, 1), 0, 127) }));
                break;
        }

        return result.OrderBy(n => n.Start).ToList();
    }

    private static int DegreeDistance(int from, int to, (int Root, MelodyScale Scale)? key, IReadOnlyList<int>? scale)
    {
        if (key is not { } k || scale is null)
        {
            return (to - from) / 2;
        }

        var sign = Math.Sign(to - from);
        var count = 0;
        for (var p = from; p != to; p += sign)
        {
            if (scale.Contains((((p + sign - k.Root) % 12) + 12) % 12))
            {
                count++;
            }
        }

        return sign * count;
    }
}
