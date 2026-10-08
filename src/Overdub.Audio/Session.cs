namespace Overdub.Audio;

public sealed class Session : IDisposable
{
    private readonly Dictionary<Track, string> _recordingPaths = [];
    private int _take;

    public Session(string directory)
    {
        Directory = directory;
    }

    public string Directory { get; private set; }
    public AsioEngine Engine { get; } = new();
    public List<Track> Tracks { get; } = [];

    public bool CanRecord => Tracks.Any(t => t is { Armed: true, Input: not null });

    public void StartRecording()
    {
        _take++;
        _recordingPaths.Clear();
        var inputs = new Dictionary<int, string>();
        foreach (var track in Tracks.Where(t => t is { Armed: true, Input: not null }))
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = System.IO.Path.Combine(Directory, $"{track.Name}-take{_take}.wav");
            _recordingPaths[track] = path;
            inputs[track.Input!.Value] = path;
        }

        Engine.StartRecording(inputs);
        Engine.Play();
    }

    public void StopRecording()
    {
        var start = Math.Max(0, Engine.RecordStartSample - Engine.CompensationSamples);
        Engine.StopRecording();
        foreach (var (track, path) in _recordingPaths)
        {
            var playback = PlaybackTrack.FromWav(path, start, Engine.SampleRate);
            track.AddClip(new Clip(path, playback));
        }

        _recordingPaths.Clear();
        Engine.SetTracks(Tracks.SelectMany(t => t.Clips).Select(c => c.Playback));
    }

    public long LengthSamples => Tracks.SelectMany(t => t.Clips).Select(c => c.StartSample + c.Length).DefaultIfEmpty(0).Max();

    public string ProjectPath => System.IO.Path.Combine(Directory, ProjectFile.FileName);

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var tracks = Tracks.Select(t => new TrackData(
            t.Name,
            t.Mute,
            t.Solo,
            t.Gain,
            t.Clips.Select(c => new ClipData(System.IO.Path.GetRelativePath(Directory, c.Path), c.StartSample)).ToList())).ToList();
        ProjectFile.Write(ProjectPath, new ProjectData(1, Engine.SampleRate, Engine.Bpm, tracks));
    }

    public double Load(string projectPath)
    {
        var data = ProjectFile.Read(projectPath);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(projectPath))!;
        var clips = new List<(Track Track, TrackData Data, List<Clip> Clips)>();
        foreach (var trackData in data.Tracks)
        {
            var track = Tracks.FirstOrDefault(t => t.Name == trackData.Name);
            if (track is null)
            {
                continue;
            }

            var loaded = trackData.Clips
                .Select(c => new Clip(
                    System.IO.Path.Combine(directory, c.File),
                    PlaybackTrack.FromWav(System.IO.Path.Combine(directory, c.File), c.StartSample, Engine.SampleRate)))
                .ToList();
            clips.Add((track, trackData, loaded));
        }

        foreach (var track in Tracks)
        {
            track.Clips.Clear();
        }

        foreach (var (track, trackData, loaded) in clips)
        {
            track.Mute = trackData.Mute;
            track.Solo = trackData.Solo;
            track.Gain = trackData.Gain;
            loaded.ForEach(track.AddClip);
        }

        Directory = directory;
        Engine.SetTracks(Tracks.SelectMany(t => t.Clips).Select(c => c.Playback));
        return data.Bpm;
    }

    public void ExportMixdown(string path) =>
        Mixer.Export(Tracks.SelectMany(t => t.Clips).Select(c => c.Playback).ToList(), Engine.SampleRate, path);

    public void Dispose() => Engine.Dispose();
}
