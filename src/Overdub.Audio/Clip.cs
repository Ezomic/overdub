namespace Overdub.Audio;

public sealed class Clip
{
    public const int PeakBlock = 441;

    public Clip(string path, PlaybackTrack playback)
    {
        Path = path;
        Playback = playback;
        Peaks = ComputePeaks(playback.Samples);
    }

    public string Path { get; }
    public PlaybackTrack Playback { get; }
    public float[] Peaks { get; }
    public long StartSample => Playback.StartSample;
    public long Length => Playback.Samples.Length;

    private static float[] ComputePeaks(float[] samples)
    {
        var peaks = new float[(samples.Length + PeakBlock - 1) / PeakBlock];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = Math.Abs(samples[i]);
            if (value > peaks[i / PeakBlock])
            {
                peaks[i / PeakBlock] = value;
            }
        }

        return peaks;
    }
}
