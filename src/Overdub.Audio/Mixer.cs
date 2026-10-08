using NAudio.Wave;

namespace Overdub.Audio;

public static class Mixer
{
    private const float Knee = 0.8f;

    public static float SoftLimit(float value)
    {
        var magnitude = Math.Abs(value);
        if (magnitude <= Knee)
        {
            return value;
        }

        var limited = Knee + ((1f - Knee) * MathF.Tanh((magnitude - Knee) / (1f - Knee)));
        return MathF.CopySign(limited, value);
    }

    public static void Mix(IReadOnlyList<PlaybackTrack> tracks, long position, float[] destination, int frames)
    {
        var anySolo = false;
        foreach (var track in tracks)
        {
            anySolo |= track.Solo;
        }

        foreach (var track in tracks)
        {
            if (track.Mute || (anySolo && !track.Solo))
            {
                continue;
            }

            var from = Math.Max(position, track.StartSample);
            var to = Math.Min(position + frames, track.StartSample + track.Samples.Length);
            for (var at = from; at < to; at++)
            {
                destination[at - position] += track.Samples[at - track.StartSample] * track.Gain;
            }
        }
    }

    public static void Export(IReadOnlyList<PlaybackTrack> tracks, int sampleRate, string path)
    {
        var length = tracks.Select(t => t.StartSample + t.Samples.Length).DefaultIfEmpty(0).Max();
        if (length == 0)
        {
            throw new InvalidOperationException("Nothing to export yet. Record something first.");
        }

        const int block = 4096;
        var mono = new float[block];
        var stereo = new float[block * 2];
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2));
        for (long position = 0; position < length; position += block)
        {
            var frames = (int)Math.Min(block, length - position);
            Array.Clear(mono, 0, block);
            Mix(tracks, position, mono, frames);
            for (var i = 0; i < frames; i++)
            {
                stereo[i * 2] = mono[i];
                stereo[(i * 2) + 1] = mono[i];
            }

            writer.WriteSamples(stereo, 0, frames * 2);
        }
    }
}
