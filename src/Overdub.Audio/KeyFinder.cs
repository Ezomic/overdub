namespace Overdub.Audio;

public sealed record SongKey(int Root, bool Minor, double Confidence, string Source)
{
    public string Name => NoteSpelling.KeyName(this);
}

public static class KeyFinder
{
    public static SongKey? FromSession(Session session, bool includeAudio = false)
    {
        var fromChords = FromChords(session);
        if (fromChords is not null)
        {
            return fromChords;
        }

        var notes = session.Tracks.Where(t => !t.IsDrums).SelectMany(t => t.MidiClips).SelectMany(c => c.Notes()).ToList();
        if (notes.Count > 0 && AudioAnalyzer.DetectKey(notes) is { } fromMidi)
        {
            return new SongKey(fromMidi.Tonic, fromMidi.Minor, fromMidi.Confidence, "the MIDI notes");
        }

        if (!includeAudio)
        {
            return null;
        }

        var rate = session.Engine.SampleRate;
        var clip = session.Tracks.SelectMany(t => t.Clips).OrderByDescending(c => c.Length).FirstOrDefault();
        if (clip is null || rate == 0)
        {
            return null;
        }

        var samples = new float[clip.Playback.Length];
        Array.Copy(clip.Playback.Samples, clip.Playback.Offset, samples, 0, samples.Length);
        return AudioAnalyzer.DetectKey(samples, rate) is { } fromAudio ? new SongKey(fromAudio.Tonic, fromAudio.Minor, fromAudio.Confidence, "the audio recording") : null;
    }

    public static IReadOnlyList<Chord> OpeningProgression(Session session)
    {
        var tracks = session.Tracks.Where(t => t.Machine == MachineRole.Guitar && t.ChordPatterns.Count > 0).ToList();
        if (tracks.Count == 0)
        {
            tracks = session.Tracks.Where(t => t.Machine == MachineRole.Bass && t.ChordPatterns.Count > 0).ToList();
        }

        if (tracks.Count == 0)
        {
            return [];
        }

        var earliest = tracks.SelectMany(t => t.MidiClips).Where(c => c.PatternId is not null).OrderBy(c => c.StartSample).FirstOrDefault()?.PatternId;
        var pattern = tracks.SelectMany(t => t.ChordPatterns).FirstOrDefault(p => p.Id == earliest) ?? tracks[0].ChordPatterns[0];
        var effective = session.EffectivePattern(pattern);
        return Enumerable.Range(0, effective.Bars).Select(i => effective[i]).ToList();
    }

    private static SongKey? FromChords(Session session)
    {
        var chordTracks = session.Tracks.Where(t => t.Machine is MachineRole.Guitar or MachineRole.Bass).ToList();
        var placed = chordTracks.SelectMany(t => t.MidiClips).Where(c => c.PatternId is not null).GroupBy(c => c.PatternId!).ToDictionary(g => g.Key, g => g.Count());
        var patterns = chordTracks.SelectMany(t => t.ChordPatterns).ToList();
        var used = patterns.Where(p => placed.ContainsKey(p.Id)).ToList();
        if (used.Count == 0)
        {
            used = patterns;
        }

        if (used.Count == 0)
        {
            return null;
        }

        var earliest = chordTracks.SelectMany(t => t.MidiClips).Where(c => c.PatternId is not null).OrderBy(c => c.StartSample).FirstOrDefault()?.PatternId;
        var notes = new List<MidiNote>();
        var opening = session.EffectivePattern(used.FirstOrDefault(p => p.Id == earliest) ?? used[0]);
        var first = opening[0];
        foreach (var pattern in used)
        {
            var effective = session.EffectivePattern(pattern);
            var weight = 1000L * placed.GetValueOrDefault(pattern.Id, 1);
            for (var bar = 0; bar < effective.Bars; bar++)
            {
                var chord = effective[bar];
                foreach (var interval in chord.Intervals)
                {
                    notes.Add(new MidiNote(0, interval == 0 ? weight * 2 : weight, (byte)(48 + ((chord.Root + interval) % 12))));
                }
            }
        }

        if (notes.Count == 0 || AudioAnalyzer.DetectKey(notes) is not { } key)
        {
            return null;
        }

        var firstMinor = first.Quality is ChordQuality.Minor or ChordQuality.MinorSeventh;
        var relative = key.Minor != firstMinor && ((key.Tonic - first.Root + 12) % 12) == (firstMinor ? 3 : 9);
        var parallel = key.Tonic == first.Root && key.Minor != firstMinor && first.Quality is not ChordQuality.Seventh;
        return relative || parallel
            ? new SongKey(first.Root, firstMinor, key.Confidence, "the chord patterns, starting on " + first.Name)
            : new SongKey(key.Tonic, key.Minor, key.Confidence, "the chord patterns");
    }
}
