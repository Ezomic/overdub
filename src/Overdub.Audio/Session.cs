namespace Overdub.Audio;

public sealed class Session : IDisposable
{
    private readonly Dictionary<Track, string> _recordingPaths = [];
    private readonly object _midiLock = new();
    private int _take;
    private int _created;
    private Track? _midiTrack;
    private List<MidiEvent>? _midiBuffer;
    private volatile int _midiActivity;

    public Session(string directory)
    {
        Directory = directory;
        Midi.NoteReceived += HandleNote;
    }

    public string Directory { get; private set; }
    public AsioEngine Engine { get; } = new();
    public MidiInput Midi { get; } = new();
    public List<Track> Tracks { get; } = [];

    public Track AddTrack(string name, int? input)
    {
        var track = new Track(name, input) { ColorIndex = _created++ };
        Tracks.Add(track);
        ApplyMixerState();
        return track;
    }

    public void RemoveTrack(Track track)
    {
        Tracks.Remove(track);
        PublishClips();
        ApplyMixerState();
    }

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public bool CanRecord => Tracks.Any(t => t is { Armed: true, Input: not null }) || HasArmedMidi;

    public bool HasArmedMidi => Tracks.Any(t => t is { Armed: true, IsMidi: true });

    public void StartRecording()
    {
        _take++;
        _recordingPaths.Clear();
        var inputs = new Dictionary<int, string>();
        foreach (var track in Tracks.Where(t => t is { Armed: true, Input: not null }))
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = System.IO.Path.Combine(Directory, $"{SafeName(track.Name)}-{track.Id}-take{_take}.wav");
            _recordingPaths[track] = path;
            inputs[track.Input!.Value] = path;
        }

        lock (_midiLock)
        {
            _midiTrack = HasArmedMidi ? Tracks.First(t => t is { Armed: true, IsMidi: true }) : null;
            _midiBuffer = _midiTrack is null ? null : [];
        }

        Engine.StartRecording(inputs);
        if (Engine.CountInBars > 0)
        {
            Engine.BeginCountIn();
        }
        else
        {
            Engine.Play();
        }
    }

    public void StopRecording()
    {
        var start = Math.Max(0, Engine.RecordStartSample - Engine.CompensationSamples);
        Engine.StopRecording();
        FinishMidiRecording();
        foreach (var (track, path) in _recordingPaths)
        {
            var playback = PlaybackTrack.FromWav(path, start, Engine.SampleRate);
            if (playback.Samples.Length == 0)
            {
                File.Delete(path);
                continue;
            }

            track.AddClip(new Clip(path, playback));
        }

        _recordingPaths.Clear();
        PublishClips();
    }

    public float ReadMidiActivity()
    {
        var velocity = _midiActivity;
        _midiActivity = 0;
        return velocity / 127f;
    }

    public long LengthSamples => Math.Max(
        Tracks.SelectMany(t => t.Clips).Select(c => c.StartSample + c.Length).DefaultIfEmpty(0).Max(),
        Tracks.SelectMany(t => t.MidiClips).Select(c => c.EndSample).DefaultIfEmpty(0).Max());

    public void ApplyMixerState()
    {
        var keys = Tracks.FirstOrDefault(t => t.IsMidi);
        Engine.SynthGain = keys?.Gain ?? 1f;
        Engine.SynthPan = keys?.Pan ?? 0f;
    }

    private void PublishClips()
    {
        Engine.SetTracks(Tracks.SelectMany(t => t.Clips).Select(c => c.Playback));
        Engine.SetMidiClips(Tracks.SelectMany(t => t.MidiClips));
    }

    public void HandleNote(byte note, byte velocity)
    {
        if (velocity > 0)
        {
            Engine.Synth.NoteOn(note, velocity);
            _midiActivity = Math.Max(_midiActivity, velocity);
        }
        else
        {
            Engine.Synth.NoteOff(note);
        }

        lock (_midiLock)
        {
            if (_midiBuffer is null || Engine.IsCountingIn)
            {
                return;
            }

            var at = Math.Max(0, Engine.Position - Engine.OutputLatencySamples - Engine.ManualOffsetSamples);
            _midiBuffer.Add(new MidiEvent(at, note, velocity));
        }
    }

    private void FinishMidiRecording()
    {
        Track? track;
        List<MidiEvent>? events;
        lock (_midiLock)
        {
            track = _midiTrack;
            events = _midiBuffer;
            _midiTrack = null;
            _midiBuffer = null;
        }

        if (track is null || events is not { Count: > 0 })
        {
            return;
        }

        var held = new HashSet<byte>();
        foreach (var e in events)
        {
            if (e.Velocity > 0)
            {
                held.Add(e.Note);
            }
            else
            {
                held.Remove(e.Note);
            }
        }

        var end = Engine.Position;
        events.AddRange(held.Select(note => new MidiEvent(Math.Max(end, events[^1].At), note, 0)));
        track.AddMidiClip(new MidiClip(events));
    }

    public string ProjectPath => System.IO.Path.Combine(Directory, ProjectFile.FileName);

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var tracks = Tracks.Select(t => new TrackData(
            t.Name,
            t.Mute,
            t.Solo,
            t.Gain,
            t.Clips.Select(c => new ClipData(System.IO.Path.GetRelativePath(Directory, c.Path), c.StartSample)).ToList(),
            t.MidiClips.Select(m => new MidiClipData(m.Events.Select(e => new MidiEventData(e.At, e.Note, e.Velocity)).ToList())).ToList(),
            t.Pan,
            t.Input,
            t.IsMidi,
            t.ColorIndex)).ToList();
        ProjectFile.Write(ProjectPath, new ProjectData(1, Engine.SampleRate, Engine.Bpm, tracks, Engine.BeatsPerBar, Engine.BeatUnit));
    }

    public double Load(string projectPath)
    {
        var data = ProjectFile.Read(projectPath);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(projectPath))!;
        var loaded = new List<Track>();
        foreach (var d in data.Tracks)
        {
            var isMidi = d.IsMidi ?? (d.Input is null && d.Name == "Keys");
            int? input = isMidi ? null : d.Input ?? (d.Name == "Bass" ? 1 : 0);
            var track = new Track(d.Name, input) { ColorIndex = d.Color ?? loaded.Count };
            track.Mute = d.Mute;
            track.Solo = d.Solo;
            track.Gain = d.Gain;
            track.Pan = d.Pan;
            foreach (var c in d.Clips)
            {
                var path = System.IO.Path.Combine(directory, c.File);
                track.AddClip(new Clip(path, PlaybackTrack.FromWav(path, c.StartSample, Engine.SampleRate)));
            }

            foreach (var midi in d.MidiClips ?? [])
            {
                track.AddMidiClip(new MidiClip(midi.Events.Select(e => new MidiEvent(e.At, (byte)e.Note, (byte)e.Velocity))));
            }

            loaded.Add(track);
        }

        Tracks.Clear();
        Tracks.AddRange(loaded);
        _created = loaded.Count == 0 ? 0 : loaded.Max(t => t.ColorIndex) + 1;
        Directory = directory;
        PublishClips();
        ApplyMixerState();
        Engine.BeatsPerBar = data.BeatsPerBar;
        Engine.BeatUnit = data.BeatUnit;
        return data.Bpm;
    }

    public void ExportMixdown(string path) =>
        Mixer.Export(
            Tracks.SelectMany(t => t.Clips).Select(c => c.Playback).ToList(),
            Tracks.SelectMany(t => t.MidiClips).ToList(),
            Engine.SampleRate,
            path,
            Engine.SynthGain,
            Engine.SynthPan);

    public void Dispose()
    {
        Midi.Dispose();
        Engine.Dispose();
    }
}
