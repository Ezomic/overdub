namespace Overdub.Audio;

public sealed class Session : IDisposable
{
    private readonly Dictionary<Track, string> _recordingPaths = [];
    private int _take;

    public Session(string directory)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
    }

    public string Directory { get; }
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

    public void ExportMixdown(string path) =>
        Mixer.Export(Tracks.SelectMany(t => t.Clips).Select(c => c.Playback).ToList(), Engine.SampleRate, path);

    public void Dispose() => Engine.Dispose();
}
