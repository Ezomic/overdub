using NAudio.Wave;

namespace Overdub.Audio;

public sealed class PlaybackTrack
{
    public PlaybackTrack(float[] samples, long startSample)
    {
        Samples = samples;
        StartSample = startSample;
    }

    public float[] Samples { get; }
    public long StartSample { get; }
    public float Gain { get; set; } = 1f;
    public float Pan { get; set; }
    public bool Mute { get; set; }
    public bool Solo { get; set; }

    public static PlaybackTrack FromWav(string path, long startSample, int expectedSampleRate)
    {
        using var reader = new AudioFileReader(path);
        if (reader.WaveFormat.SampleRate != expectedSampleRate)
        {
            throw new InvalidOperationException($"{Path.GetFileName(path)} is {reader.WaveFormat.SampleRate} Hz but the engine runs at {expectedSampleRate} Hz.");
        }

        var channels = reader.WaveFormat.Channels;
        var interleaved = new float[reader.Length / sizeof(float)];
        var read = ((ISampleProvider)reader).Read(interleaved.AsSpan());
        var frames = read / channels;
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
