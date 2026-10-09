using NAudio.Midi;

namespace Overdub.Audio;

public static class MidiExporter
{
    private const int TicksPerQuarter = 480;

    public static void Write(string path, MidiClip clip, string name, double bpm, int beatsPerBar, int beatUnit, int sampleRate, bool drums)
    {
        var channel = drums ? 10 : 1;
        var events = new MidiEventCollection(1, TicksPerQuarter);
        events.AddEvent(new TempoEvent((int)Math.Round(60_000_000 / bpm), 0), 0);
        events.AddEvent(new TimeSignatureEvent(0, beatsPerBar, (int)Math.Log2(beatUnit), 24, 8), 0);
        events.AddEvent(new TextEvent(name, MetaEventType.SequenceTrackName, 0), 0);
        var origin = clip.StartSample;
        var ticksPerSample = TicksPerQuarter * bpm / 60.0 / sampleRate;
        foreach (var note in clip.NoteData())
        {
            var start = (long)Math.Round((note.Start - origin) * ticksPerSample);
            var end = Math.Max(start + 1, (long)Math.Round((note.End - origin) * ticksPerSample));
            events.AddEvent(new NoteOnEvent(start, channel, note.Pitch, note.Velocity, (int)(end - start)), 0);
            events.AddEvent(new NoteEvent(end, channel, MidiCommandCode.NoteOff, note.Pitch, 0), 0);
        }

        events.PrepareForExport();
        MidiFile.Export(path, events);
    }
}
