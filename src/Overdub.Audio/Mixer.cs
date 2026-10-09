using NAudio.Wave;

namespace Overdub.Audio;

public sealed class ChannelStrip(IReadOnlyList<PlaybackTrack> clips, EffectChain? source, EffectChain? processor)
{
    public IReadOnlyList<PlaybackTrack> Clips { get; } = clips;
    public Sends? Sends { get; init; }
    public EffectChain? Source { get; } = source;
    public EffectChain? Processor { get; } = processor;
}

public sealed class MixScratch
{
    public float[] Left { get; private set; } = new float[4096];
    public float[] Right { get; private set; } = new float[4096];

    public void Ensure(int frames)
    {
        if (Left.Length < frames)
        {
            Left = new float[frames];
            Right = new float[frames];
        }
    }
}

public static class Mixer
{
    public static void MixChannels(IReadOnlyList<ChannelStrip> channels, bool anySolo, long position, float[] left, float[] right, int frames, int destOffset, MixScratch scratch, SendBus? bus = null)
    {
        foreach (var strip in channels)
        {
            var sending = bus is not null && strip.Sends is { Any: true };
            if (sending || strip is { Source.AnyEnabled: true, Processor: not null })
            {
                var processor = strip.Processor;
                if (processor is not null && strip.Source is { AnyEnabled: true } source)
                {
                    if (processor.AppliedVersion != source.Version)
                    {
                        processor.CopyFrom(source);
                    }
                }
                else
                {
                    processor = null;
                }

                scratch.Ensure(frames);
                Array.Clear(scratch.Left, 0, frames);
                Array.Clear(scratch.Right, 0, frames);
                Mix(strip.Clips, anySolo, position, scratch.Left, scratch.Right, frames, 0);
                processor?.Process(scratch.Left, scratch.Right, frames);
                for (var i = 0; i < frames; i++)
                {
                    left[destOffset + i] += scratch.Left[i];
                    right[destOffset + i] += scratch.Right[i];
                }

                bus?.Add(scratch.Left, scratch.Right, 0, destOffset, frames, strip.Sends);
            }
            else
            {
                Mix(strip.Clips, anySolo, position, left, right, frames, destOffset);
            }
        }
    }

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

    private static Sends? ScaledSends(Sends? sends, float gain) => sends is { Any: true } ? new Sends { Reverb = sends.Reverb * gain, Delay = sends.Delay * gain } : null;

    public static void AddPannedStereo(float[] sourceLeft, float[] sourceRight, float gain, float pan, float[] left, float[] right, int frames)
    {
        PanGains(pan, out var gl, out var gr);
        gl *= gain;
        gr *= gain;
        for (var i = 0; i < frames; i++)
        {
            left[i] += sourceLeft[i] * gl;
            right[i] += sourceRight[i] * gr;
        }
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

    public static void Export(IReadOnlyList<ChannelStrip> channels, IReadOnlyList<MidiClip> midi, int sampleRate, string path, float synthGain = 1f, float synthPan = 0f, long minLength = 0, string? preset = null, Vst3.Vst3Plugin? instrument = null, DrumMix? drums = null, IReadOnlyList<MachineMix>? machines = null, Sends? synthSends = null, double bpm = 120)
    {
        var tracks = channels.SelectMany(c => c.Clips).ToList();
        var tail = sampleRate;
        var length = Math.Max(
            tracks.Select(t => t.EndSample).DefaultIfEmpty(0).Max(),
            midi.Concat(drums?.Clips ?? []).Concat((machines ?? []).SelectMany(m => m.Clips)).Where(c => c.Events.Length > 0).Select(c => c.EndSample + tail).DefaultIfEmpty(0).Max());
        length = Math.Max(length, minLength);
        if (length == 0)
        {
            throw new InvalidOperationException("Nothing to export yet. Record something first.");
        }

        const int block = 4096;
        var synth = new Synth();
        synth.Configure(sampleRate);
        synth.SetPreset(preset);
        synth.Instrument = instrument;
        var sequencer = new MidiSequencer();
        var drumKit = new DrumKit();
        drumKit.Configure(sampleRate);
        var drumSequencer = new MidiSequencer();
        var drumClips = drums?.Clips ?? [];
        var machineList = machines ?? [];
        var machineSynths = machineList.Select(m => MachineLane.CreateVoice(m.Role, m.Preset, sampleRate, m.Instrument)).ToList();
        var machineSequencers = machineList.Select(_ => new MidiSequencer()).ToList();
        var machineBuf = new float[block];
        var machineRight = new float[block];
        var anySolo = AnySolo(tracks, midi) || AnySolo([], drumClips) || machineList.Any(m => AnySolo([], m.Clips));
        var scratch = new MixScratch();
        var left = new float[block];
        var right = new float[block];
        var synthBuf = new float[block];
        var drumBuf = new float[block];
        var drumRight = new float[block];
        drumKit.RightBuffer = drumRight;
        drumKit.Mix = drums?.Lanes;
        drumKit.Style = drums?.Kit ?? 0;
        var stereo = new float[block * 2];
        var bus = new SendBus();
        bus.Configure(sampleRate);
        var samplesPerBeat = sampleRate * 60.0 / bpm;
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2));
        for (long position = 0; position < length; position += block)
        {
            var frames = (int)Math.Min(block, length - position);
            Array.Clear(left, 0, block);
            Array.Clear(right, 0, block);
            Array.Clear(synthBuf, 0, block);
            Array.Clear(drumBuf, 0, block);
            Array.Clear(drumRight, 0, block);
            bus.Clear(frames);
            MixChannels(channels, anySolo, position, left, right, frames, 0, scratch, bus);
            sequencer.Render(synth, midi, anySolo, position, synthBuf, frames);
            AddPanned(synthBuf, synthGain, synthPan, left, right, frames);
            bus.AddMono(synthBuf, synthGain, frames, synthSends);
            if (drums is not null)
            {
                drumSequencer.Render(drumKit, drumClips, anySolo, position, drumBuf, frames);
                AddPannedStereo(drumBuf, drumRight, drums.Gain, drums.Pan, left, right, frames);
                bus.Add(drumBuf, drumRight, 0, 0, frames, drums.Sends is { Any: true } ds ? new Sends { Reverb = ds.Reverb * drums.Gain, Delay = ds.Delay * drums.Gain } : null);
            }

            for (var m = 0; m < machineList.Count; m++)
            {
                Array.Clear(machineBuf, 0, block);
                machineSequencers[m].Render(machineSynths[m], machineList[m].Clips, anySolo, position, machineBuf, frames);
                if (machineList[m].Effects is { AnyEnabled: true } fx)
                {
                    Array.Copy(machineBuf, machineRight, frames);
                    fx.Process(machineBuf, machineRight, frames);
                    AddPannedStereo(machineBuf, machineRight, machineList[m].Gain, machineList[m].Pan, left, right, frames);
                    bus.Add(machineBuf, machineRight, 0, 0, frames, ScaledSends(machineList[m].Sends, machineList[m].Gain));
                }
                else
                {
                    AddPanned(machineBuf, machineList[m].Gain, machineList[m].Pan, left, right, frames);
                    bus.AddMono(machineBuf, machineList[m].Gain, frames, machineList[m].Sends);
                }
            }

            bus.Render(left, right, frames, samplesPerBeat);
            for (var i = 0; i < frames; i++)
            {
                stereo[i * 2] = SoftLimit(left[i]);
                stereo[(i * 2) + 1] = SoftLimit(right[i]);
            }

            writer.WriteSamples(stereo, 0, frames * 2);
        }
    }
}

public sealed record DrumMix(IReadOnlyList<MidiClip> Clips, float Gain, float Pan, DrumLaneMix[]? Lanes = null, int Kit = 0, Sends? Sends = null);
