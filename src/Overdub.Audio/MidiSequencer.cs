namespace Overdub.Audio;

public sealed class MidiSequencer
{
    private readonly List<(int Offset, byte Note, byte Velocity)> _due = [];

    public void Render(Synth synth, IReadOnlyList<MidiClip> clips, bool anySolo, long position, float[] destination, int frames, int destOffset = 0)
    {
        _due.Clear();
        foreach (var clip in clips)
        {
            if (clip.Mute || (anySolo && !clip.Solo))
            {
                continue;
            }

            var events = clip.Events;
            var shift = clip.Shift;
            for (var i = FirstAtOrAfter(events, position - shift); i < events.Length && events[i].At + shift < position + frames; i++)
            {
                _due.Add(((int)(events[i].At + shift - position), events[i].Note, events[i].Velocity));
            }
        }

        _due.Sort((a, b) => a.Offset.CompareTo(b.Offset));

        var cursor = destOffset;
        foreach (var (offset, note, velocity) in _due)
        {
            synth.Render(destination, cursor, destOffset + offset - cursor);
            cursor = destOffset + offset;
            if (velocity > 0)
            {
                synth.NoteOn(note, velocity);
            }
            else
            {
                synth.NoteOff(note);
            }
        }

        synth.Render(destination, cursor, destOffset + frames - cursor);
    }

    private static int FirstAtOrAfter(MidiEvent[] events, long position)
    {
        var low = 0;
        var high = events.Length;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (events[middle].At < position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
