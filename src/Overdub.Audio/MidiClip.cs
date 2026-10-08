namespace Overdub.Audio;

public readonly record struct MidiEvent(long At, byte Note, byte Velocity);

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
        var left = new List<MidiEvent>();
        var right = new List<MidiEvent>();
        foreach (var e in Events)
        {
            if (e.At < local)
            {
                left.Add(e);
                if (e.Velocity > 0)
                {
                    open[e.Note] = e.Velocity;
                }
                else
                {
                    open.Remove(e.Note);
                }
            }
            else
            {
                right.Add(e);
            }
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
