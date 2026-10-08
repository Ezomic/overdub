namespace Overdub.Audio;

public enum MidiKind
{
    Note,
    Sustain,
    PitchBend,
}

public readonly record struct MidiEvent(long At, byte Note, byte Velocity, MidiKind Kind = MidiKind.Note, int Value = 0);

public readonly record struct MidiNote(long Start, long End, byte Pitch);

public readonly record struct MidiNoteData(long Start, long End, byte Pitch, byte Velocity);

public sealed class MidiClip
{
    public MidiClip(IEnumerable<MidiEvent> events, long shift = 0)
    {
        Events = events.OrderBy(e => e.At).ToArray();
        Shift = shift;
    }

    public MidiEvent[] Events { get; }
    public long Shift { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }

    public long StartSample => Events.Length == 0 ? 0 : Events[0].At + Shift;
    public long EndSample => Events.Length == 0 ? 0 : Events[^1].At + Shift;

    public IReadOnlyList<MidiNoteData> NoteData()
    {
        var open = new Dictionary<byte, (long At, byte Velocity)>();
        var notes = new List<MidiNoteData>();
        foreach (var e in Events)
        {
            if (e.Kind != MidiKind.Note)
            {
                continue;
            }

            if (e.Velocity > 0)
            {
                open[e.Note] = (e.At, e.Velocity);
            }
            else if (open.Remove(e.Note, out var start))
            {
                notes.Add(new MidiNoteData(start.At + Shift, e.At + Shift, e.Note, start.Velocity));
            }
        }

        return notes.OrderBy(n => n.Start).ThenBy(n => n.Pitch).ToList();
    }

    public MidiClip WithNotes(IEnumerable<MidiNoteData> notes)
    {
        var events = Events.Where(e => e.Kind != MidiKind.Note).ToList();
        foreach (var note in notes)
        {
            events.Add(new MidiEvent(note.Start - Shift, note.Pitch, Math.Max((byte)1, note.Velocity)));
            events.Add(new MidiEvent(Math.Max(note.Start + 1, note.End) - Shift, note.Pitch, 0));
        }

        return new MidiClip(events, Shift) { Mute = Mute, Solo = Solo };
    }

    public IReadOnlyList<MidiNote> Notes()
    {
        var open = new Dictionary<byte, long>();
        var notes = new List<MidiNote>();
        foreach (var e in Events)
        {
            if (e.Kind != MidiKind.Note)
            {
                continue;
            }

            if (e.Velocity > 0)
            {
                open[e.Note] = e.At;
            }
            else if (open.Remove(e.Note, out var start))
            {
                notes.Add(new MidiNote(start + Shift, e.At + Shift, e.Note));
            }
        }

        return notes;
    }

    public IEnumerable<int> PitchesAt(long position) =>
        Notes().Where(n => n.Start <= position && position < n.End).Select(n => (int)n.Pitch);

    public MidiClip Scaled(double factor) =>
        new(Events.Select(e => e with { At = (long)Math.Round(e.At * factor) }), (long)Math.Round(Shift * factor)) { Mute = Mute, Solo = Solo };

    public MidiClip Copy(long shiftBy) => new(Events, Shift + shiftBy) { Mute = Mute, Solo = Solo };

    public MidiClip Quantize(long grid, double strength = 1.0)
    {
        if (grid <= 0)
        {
            return Copy(0);
        }

        var result = new List<MidiEvent>();
        var open = new Dictionary<byte, (long Start, byte Velocity)>();
        var moved = new Dictionary<byte, Queue<long>>();
        foreach (var e in Events.OrderBy(e => e.At))
        {
            if (e.Kind != MidiKind.Note)
            {
                result.Add(e);
                continue;
            }

            if (e.Velocity > 0)
            {
                var absolute = e.At + Shift;
                var target = (long)(Math.Round((double)absolute / grid) * grid);
                var start = absolute + (long)Math.Round((target - absolute) * strength);
                open[e.Note] = (e.At, e.Velocity);
                if (!moved.TryGetValue(e.Note, out var queue))
                {
                    moved[e.Note] = queue = new Queue<long>();
                }

                queue.Enqueue(start - Shift - e.At);
                result.Add(e with { At = e.At + (start - absolute) });
                continue;
            }

            if (!open.Remove(e.Note, out _))
            {
                result.Add(e);
                continue;
            }

            var offset = moved[e.Note].Dequeue();
            result.Add(e with { At = e.At + offset });
        }

        return new MidiClip(result, Shift) { Mute = Mute, Solo = Solo };
    }

    public (MidiClip? Left, MidiClip? Right) Split(long position)
    {
        var local = position - Shift;
        var open = new Dictionary<byte, byte>();
        var sustain = 0;
        var bend = 0;
        var left = new List<MidiEvent>();
        var right = new List<MidiEvent>();
        foreach (var e in Events)
        {
            if (e.At >= local)
            {
                right.Add(e);
                continue;
            }

            left.Add(e);
            switch (e.Kind)
            {
                case MidiKind.Sustain:
                    sustain = e.Value;
                    break;
                case MidiKind.PitchBend:
                    bend = e.Value;
                    break;
                case MidiKind.Note when e.Velocity > 0:
                    open[e.Note] = e.Velocity;
                    break;
                default:
                    open.Remove(e.Note);
                    break;
            }
        }

        if (sustain >= 64)
        {
            left.Add(new MidiEvent(local, 0, 0, MidiKind.Sustain, 0));
            right.Insert(0, new MidiEvent(local, 0, 0, MidiKind.Sustain, sustain));
        }

        if (bend != 0)
        {
            left.Add(new MidiEvent(local, 0, 0, MidiKind.PitchBend, 0));
            right.Insert(0, new MidiEvent(local, 0, 0, MidiKind.PitchBend, bend));
        }

        foreach (var (note, velocity) in open)
        {
            left.Add(new MidiEvent(local, note, 0));
            right.Insert(0, new MidiEvent(local, note, velocity));
        }

        return (
            left.Count > 0 ? new MidiClip(left, Shift) { Mute = Mute, Solo = Solo } : null,
            right.Count > 0 ? new MidiClip(right, Shift) { Mute = Mute, Solo = Solo } : null);
    }
}
