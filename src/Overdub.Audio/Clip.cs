namespace Overdub.Audio;

public sealed class Clip
{
    public const int PeakBlock = 441;

    public Clip(string path, PlaybackTrack playback)
    {
        Path = path;
        Playback = playback;
    }

    public string Path { get; }
    public PlaybackTrack Playback { get; }

    public long StartSample
    {
        get => Playback.StartSample;
        set => Playback.StartSample = value;
    }

    public long Length => Playback.Length;
    public long EndSample => Playback.EndSample;

    public float[] ComputePeaks()
    {
        var offset = (int)Playback.Offset;
        var length = (int)Playback.Length;
        var peaks = new float[(length + PeakBlock - 1) / PeakBlock];
        for (var i = 0; i < length; i++)
        {
            var value = Math.Abs(Playback.Samples[offset + i]);
            if (value > peaks[i / PeakBlock])
            {
                peaks[i / PeakBlock] = value;
            }
        }

        return peaks;
    }

    public Clip Copy(long start, long offset, long length) => new(Path, new PlaybackTrack(Playback.Samples, start)
    {
        Offset = offset,
        Length = length,
        Gain = Playback.Gain,
        Pan = Playback.Pan,
        Mute = Playback.Mute,
        Solo = Playback.Solo,
    });
}
