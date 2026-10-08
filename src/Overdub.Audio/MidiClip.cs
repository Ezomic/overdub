namespace Overdub.Audio;

public readonly record struct MidiEvent(long At, byte Note, byte Velocity);

public readonly record struct MidiNote(long Start, long End, byte Pitch);

public sealed class MidiClip
{
    public MidiClip(IEnumerable<MidiEvent> events)
    {
        Events = events.OrderBy(e => e.At).ToArray();
    }

    public MidiEvent[] Events { get; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }

    public long StartSample => Events.Length == 0 ? 0 : Events[0].At;
    public long EndSample => Events.Length == 0 ? 0 : Events[^1].At;

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
                notes.Add(new MidiNote(start, e.At, e.Note));
            }
        }

        return notes;
    }
}
