namespace Overdub.Audio;

public enum MidiKind
{
    Note,
    Sustain,
    PitchBend,
}

public readonly record struct MidiEvent(long At, byte Note, byte Velocity, MidiKind Kind = MidiKind.Note, int Value = 0);

public readonly record struct MidiNote(long Start, long End, byte Pitch);

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

    public MidiClip Copy(long shiftBy) => new(Events, Shift + shiftBy) { Mute = Mute, Solo = Solo };

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
