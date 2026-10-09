using NAudio.Midi;

namespace Overdub.Audio;

public static class MidiExporter
{
    private const int TicksPerQuarter = 480;

    public static void WriteSong(string path, IReadOnlyList<(string Name, IReadOnlyList<MidiNoteData> Notes, bool Drums)> tracks, double bpm, int beatsPerBar, int beatUnit, int sampleRate)
    {
        var events = new MidiEventCollection(1, TicksPerQuarter);
        events.AddEvent(new TempoEvent((int)Math.Round(60_000_000 / bpm), 0), 0);
        events.AddEvent(new TimeSignatureEvent(0, beatsPerBar, (int)Math.Log2(beatUnit), 24, 8), 0);
        events.AddEvent(new TextEvent("Overdub", MetaEventType.SequenceTrackName, 0), 0);
        var ticksPerSample = TicksPerQuarter * bpm / 60.0 / sampleRate;
        var channel = 0;
        foreach (var track in tracks.Where(t => t.Notes.Count > 0))
        {
            events.AddTrack();
            var index = events.Tracks - 1;
            events.AddEvent(new TextEvent(track.Name, MetaEventType.SequenceTrackName, 0), index);
            var midiChannel = track.Drums ? 10 : (++channel == 10 ? ++channel : channel);
            foreach (var note in track.Notes)
            {
                var start = (long)Math.Round(note.Start * ticksPerSample);
                var end = Math.Max(start + 1, (long)Math.Round(note.End * ticksPerSample));
                events.AddEvent(new NoteOnEvent(start, midiChannel, note.Pitch, note.Velocity, (int)(end - start)), index);
                events.AddEvent(new NoteEvent(end, midiChannel, MidiCommandCode.NoteOff, note.Pitch, 0), index);
            }
        }

        events.PrepareForExport();
        MidiFile.Export(path, events);
    }

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
