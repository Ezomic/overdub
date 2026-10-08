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

    public static void PanGains(float pan, out float left, out float right)
    {
        left = Math.Min(1f, 1f - pan);
        right = Math.Min(1f, 1f + pan);
    }

    public static void AddPanned(float[] source, float gain, float pan, float[] left, float[] right, int frames)
    {
        PanGains(pan, out var gl, out var gr);
        gl *= gain;
        gr *= gain;
        for (var i = 0; i < frames; i++)
        {
            left[i] += source[i] * gl;
            right[i] += source[i] * gr;
        }
    }

    public static void Mix(IReadOnlyList<PlaybackTrack> tracks, bool anySolo, long position, float[] left, float[] right, int frames, int destOffset = 0)
    {
        foreach (var track in tracks)
        {
            if (track.Mute || (anySolo && !track.Solo))
            {
                continue;
            }

            PanGains(track.Pan, out var gl, out var gr);
            gl *= track.Gain;
            gr *= track.Gain;
            var from = Math.Max(position, track.StartSample);
            var to = Math.Min(position + frames, track.EndSample);
            for (var at = from; at < to; at++)
            {
                var index = track.Offset + (at - track.StartSample);
                var sample = track.Samples[index];
                var other = track.Right is { } r ? r[index] : sample;
                left[destOffset + (int)(at - position)] += sample * gl;
                right[destOffset + (int)(at - position)] += other * gr;
            }
        }
    }

    public static void Export(IReadOnlyList<PlaybackTrack> tracks, IReadOnlyList<MidiClip> midi, int sampleRate, string path, float synthGain = 1f, float synthPan = 0f, long minLength = 0, string? preset = null)
    {
        var tail = sampleRate;
        var length = Math.Max(
            tracks.Select(t => t.EndSample).DefaultIfEmpty(0).Max(),
            midi.Where(c => c.Events.Length > 0).Select(c => c.EndSample + tail).DefaultIfEmpty(0).Max());
        length = Math.Max(length, minLength);
        if (length == 0)
        {
            throw new InvalidOperationException("Nothing to export yet. Record something first.");
        }

        const int block = 4096;
        var synth = new Synth();
        synth.Configure(sampleRate);
        synth.SetPreset(preset);
        var sequencer = new MidiSequencer();
        var anySolo = AnySolo(tracks, midi);
        var left = new float[block];
        var right = new float[block];
        var synthBuf = new float[block];
        var stereo = new float[block * 2];
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2));
        for (long position = 0; position < length; position += block)
        {
            var frames = (int)Math.Min(block, length - position);
            Array.Clear(left, 0, block);
            Array.Clear(right, 0, block);
            Array.Clear(synthBuf, 0, block);
            Mix(tracks, anySolo, position, left, right, frames);
            sequencer.Render(synth, midi, anySolo, position, synthBuf, frames);
            AddPanned(synthBuf, synthGain, synthPan, left, right, frames);
            for (var i = 0; i < frames; i++)
            {
                stereo[i * 2] = SoftLimit(left[i]);
                stereo[(i * 2) + 1] = SoftLimit(right[i]);
            }

            writer.WriteSamples(stereo, 0, frames * 2);
        }
    }
}
