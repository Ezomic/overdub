using NAudio.Wave;

namespace Overdub.Audio;

public sealed class PlaybackTrack
{
    public PlaybackTrack(float[] samples, long startSample)
    {
        Samples = samples;
        StartSample = startSample;
        Length = samples.Length;
    }

    public float[] Samples { get; }
    public float[]? Right { get; init; }
    public long StartSample { get; set; }
    public long Offset { get; set; }
    public long Length { get; set; }
    public long EndSample => StartSample + Length;
    public float Gain { get; set; } = 1f;
    public float Pan { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }

    public static PlaybackTrack FromWav(string path, long startSample, int expectedSampleRate, bool keepStereo = false)
    {
        using var reader = new AudioFileReader(path);
        if (reader.WaveFormat.SampleRate != expectedSampleRate)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} is {reader.WaveFormat.SampleRate} Hz but the engine runs at {expectedSampleRate} Hz.");
        }

        var channels = reader.WaveFormat.Channels;
        var interleaved = new float[reader.Length / sizeof(float)];
        var read = ((ISampleProvider)reader).Read(interleaved.AsSpan());
        return FromInterleaved(interleaved, read, channels, startSample, keepStereo);
    }

    public static PlaybackTrack FromInterleaved(float[] interleaved, int count, int channels, long startSample, bool keepStereo)
    {
        var frames = count / channels;
        if (keepStereo && channels >= 2)
        {
            var left = new float[frames];
            var right = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                left[i] = interleaved[i * channels];
                right[i] = interleaved[(i * channels) + 1];
            }

            return new PlaybackTrack(left, startSample) { Right = right };
        }

        var mono = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            var sum = 0f;
            for (var c = 0; c < channels; c++)
            {
                sum += interleaved[(i * channels) + c];
            }

            mono[i] = sum / channels;
        }

        return new PlaybackTrack(mono, startSample);
    }
}
