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

    public static bool AnySolo(IReadOnlyList<PlaybackTrack> tracks, IReadOnlyList<MidiClip> midi)
    {
        foreach (var track in tracks)
        {
            if (track.Solo)
            {
                return true;
            }
        }

        foreach (var clip in midi)
        {
            if (clip.Solo)
            {
                return true;
            }
        }

        return false;
    }

    public static void Mix(IReadOnlyList<PlaybackTrack> tracks, bool anySolo, long position, float[] destination, int frames)
    {
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

    public static void Export(IReadOnlyList<PlaybackTrack> tracks, IReadOnlyList<MidiClip> midi, int sampleRate, string path)
    {
        var tail = sampleRate;
        var length = Math.Max(
            tracks.Select(t => t.StartSample + t.Samples.Length).DefaultIfEmpty(0).Max(),
            midi.Where(c => c.Events.Length > 0).Select(c => c.EndSample + tail).DefaultIfEmpty(0).Max());
        if (length == 0)
        {
            throw new InvalidOperationException("Nothing to export yet. Record something first.");
        }

        const int block = 4096;
        var synth = new Synth();
        synth.Configure(sampleRate);
        var sequencer = new MidiSequencer();
        var anySolo = AnySolo(tracks, midi);
        var mono = new float[block];
        var stereo = new float[block * 2];
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2));
        for (long position = 0; position < length; position += block)
        {
            var frames = (int)Math.Min(block, length - position);
            Array.Clear(mono, 0, block);
            Mix(tracks, anySolo, position, mono, frames);
            sequencer.Render(synth, midi, anySolo, position, mono, frames);
            for (var i = 0; i < frames; i++)
            {
                var value = SoftLimit(mono[i]);
                stereo[i * 2] = value;
                stereo[(i * 2) + 1] = value;
            }

            writer.WriteSamples(stereo, 0, frames * 2);
        }
    }
}
