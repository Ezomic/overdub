using System.Collections.Concurrent;

namespace Overdub.Audio;

public interface INoteTarget
{
    void NoteOn(int note, int velocity);
    void NoteOff(int note);
    void SustainPedal(bool down);
    void PitchBend(int value);
    void Render(float[] destination, int offset, int frames);
}

public sealed record DrumLane(string Name, byte Note);

public sealed class DrumLaneMix
{
    public float Gain { get; set; } = 1f;
    public float Pan { get; set; }
    public bool Mute { get; set; }
}

public sealed class DrumKit : INoteTarget
{
    private const int MaxVoices = 24;
    private const float Level = 0.55f;

    private enum Sound
    {
        Kick,
        Snare,
        ClosedHat,
        OpenHat,
        Clap,
        LowTom,
        HighTom,
        Crash,
    }

    public static IReadOnlyList<DrumLane> Lanes { get; } =
    [
        new("Kick", 36),
        new("Snare", 38),
        new("Closed hat", 42),
        new("Open hat", 46),
        new("Clap", 39),
        new("Low tom", 45),
        new("High tom", 50),
        new("Crash", 49),
    ];

    private readonly Voice[] _voices = new Voice[MaxVoices];
    private readonly ConcurrentQueue<(int Note, int Velocity)> _commands = new();
    private int _sampleRate = 44100;
    private uint _seed = 0x1234567;

    public DrumLaneMix[]? Mix { get; set; }

    public int Style { get; set; }

    public static readonly string[] StyleNames = ["Rock", "Electronic", "Brush"];

    public float[]? RightBuffer { get; set; }

    public void Configure(int sampleRate) => _sampleRate = sampleRate;

    public void NoteOn(int note, int velocity) => _commands.Enqueue((note, Math.Clamp(velocity, 1, 127)));

    public void NoteOff(int note)
    {
    }

    public void SustainPedal(bool down)
    {
    }

    public void PitchBend(int value)
    {
    }

    public void Silence() => _commands.Enqueue((-1, 0));

    public void Render(float[] destination, int offset, int frames)
    {
        while (_commands.TryDequeue(out var command))
        {
            if (command.Note < 0)
            {
                Array.Clear(_voices);
            }
            else
            {
                Trigger(command.Note, command.Velocity);
            }
        }

        for (var v = 0; v < MaxVoices; v++)
        {
            ref var voice = ref _voices[v];
            if (voice.Active)
            {
                RenderVoice(ref voice, destination, offset, frames);
            }
        }
    }

    private void Trigger(int note, int velocity)
    {
        var index = -1;
        for (var i = 0; i < Lanes.Count; i++)
        {
            if (Lanes[i].Note == note)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return;
        }

        var mix = Mix is { } lanes && index < lanes.Length ? lanes[index] : null;
        if (mix is { Mute: true })
        {
            return;
        }

        Mixer.PanGains(mix?.Pan ?? 0f, out var panLeft, out var panRight);
        var laneGain = mix?.Gain ?? 1f;
        var sound = (Sound)index;
        if (sound == Sound.ClosedHat)
        {
            for (var v = 0; v < MaxVoices; v++)
            {
                if (_voices[v].Active && _voices[v].Sound == Sound.OpenHat)
                {
                    _voices[v].Active = false;
                }
            }
        }

        var slot = 0;
        var oldest = -1L;
        for (var v = 0; v < MaxVoices; v++)
        {
            if (!_voices[v].Active)
            {
                slot = v;
                oldest = long.MaxValue;
                break;
            }

            if (_voices[v].Frame > oldest)
            {
                oldest = _voices[v].Frame;
                slot = v;
            }
        }

        _seed = (_seed * 1664525u) + 1013904223u;
        _voices[slot] = new Voice { Active = true, Sound = sound, Gain = ((velocity / 127f) * (velocity / 127f) * 0.6f + 0.4f * (velocity / 127f)) * laneGain, PanL = panLeft * 1.4142f, PanR = panRight * 1.4142f, Noise = _seed | 1u, Kit = Style };
    }

    private void RenderVoice(ref Voice voice, float[] destination, int offset, int frames)
    {
        var rate = _sampleRate;
        var right = RightBuffer;
        for (var i = 0; i < frames; i++)
        {
            var t = voice.Frame / (double)rate;
            var sample = Sample(ref voice, t, rate, out var finished);
            if (finished)
            {
                voice.Active = false;
                return;
            }

            var scaled = sample * voice.Gain * Level;
            if (right is null)
            {
                destination[offset + i] += scaled * (voice.PanL + voice.PanR) * 0.5f;
            }
            else
            {
                destination[offset + i] += scaled * voice.PanL;
                right[offset + i] += scaled * voice.PanR;
            }

            voice.Frame++;
        }
    }

    private static float NextNoise(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return (state / (float)uint.MaxValue * 2f) - 1f;
    }

    private static float HighPass(ref Voice voice, float input, float coefficient)
    {
        voice.Lowpass += coefficient * (input - voice.Lowpass);
        return input - voice.Lowpass;
    }

    private static float Sweep(ref Voice voice, double t, int rate, double low, double high, double sweepTime, double decay)
    {
        var frequency = low + ((high - low) * Math.Exp(-t / sweepTime));
        voice.Phase += 2 * Math.PI * frequency / rate;
        return (float)(Math.Sin(voice.Phase) * Math.Exp(-t / decay));
    }

    private static float Sample(ref Voice voice, double t, int rate, out bool finished)
    {
        finished = false;
        var kit = voice.Kit;
        switch (voice.Sound)
        {
            case Sound.Kick:
                if (t > (kit == 1 ? 1.2 : 0.7))
                {
                    finished = true;
                    return 0;
                }

                if (kit == 1)
                {
                    return Sweep(ref voice, t, rate, 42, 130, 0.05, 0.5) * 1.15f;
                }

                if (kit == 2)
                {
                    return Sweep(ref voice, t, rate, 55, 110, 0.03, 0.12) * 0.8f;
                }

                var click = t < 0.004 ? NextNoise(ref voice.Noise) * 0.25f : 0f;
                return (Sweep(ref voice, t, rate, 46, 160, 0.03, 0.2) * 1.1f) + click;
            case Sound.Snare:
                if (t > (kit == 2 ? 0.5 : 0.45))
                {
                    finished = true;
                    return 0;
                }

                if (kit == 2)
                {
                    var brushNoise = HighPass(ref voice, NextNoise(ref voice.Noise), 0.45f) * (float)(Math.Min(1, t / 0.01) * Math.Exp(-t / 0.16));
                    return brushNoise * 0.7f;
                }

                if (kit == 1)
                {
                    var digital = HighPass(ref voice, NextNoise(ref voice.Noise), 0.2f) * (float)Math.Exp(-t / 0.09);
                    return (digital * 0.6f) + (Sweep(ref voice, t, rate, 190, 260, 0.012, 0.1) * 0.7f);
                }

                var snareNoise = HighPass(ref voice, NextNoise(ref voice.Noise), 0.15f) * (float)Math.Exp(-t / 0.075);
                return (snareNoise * 0.75f) + (Sweep(ref voice, t, rate, 180, 230, 0.01, 0.06) * 0.55f);
            case Sound.ClosedHat:
                if (t > 0.15)
                {
                    finished = true;
                    return 0;
                }

                if (kit == 2)
                {
                    return HighPass(ref voice, NextNoise(ref voice.Noise), 0.2f) * (float)Math.Exp(-t / 0.03) * 0.3f;
                }

                return HighPass(ref voice, NextNoise(ref voice.Noise), kit == 1 ? 0.03f : 0.08f) * (float)Math.Exp(-t / (kit == 1 ? 0.015 : 0.022)) * 0.55f;
            case Sound.OpenHat:
                if (t > 0.8)
                {
                    finished = true;
                    return 0;
                }

                if (kit == 2)
                {
                    return HighPass(ref voice, NextNoise(ref voice.Noise), 0.2f) * (float)Math.Exp(-t / 0.2) * 0.3f;
                }

                return HighPass(ref voice, NextNoise(ref voice.Noise), kit == 1 ? 0.03f : 0.08f) * (float)Math.Exp(-t / (kit == 1 ? 0.22 : 0.14)) * 0.5f;
            case Sound.Clap:
                if (t > 0.4)
                {
                    finished = true;
                    return 0;
                }

                if (kit == 2)
                {
                    return HighPass(ref voice, NextNoise(ref voice.Noise), 0.35f) * (float)Math.Exp(-t / 0.025) * 0.6f;
                }

                var envelope = 0.0;
                for (var k = 0; k < 3; k++)
                {
                    var start = k * 0.012;
                    if (t >= start)
                    {
                        envelope += Math.Exp(-(t - start) / 0.004) * 0.6;
                    }
                }

                if (t >= 0.036)
                {
                    envelope += Math.Exp(-(t - 0.036) / (kit == 1 ? 0.11 : 0.07)) * 0.8;
                }

                return HighPass(ref voice, NextNoise(ref voice.Noise), 0.3f) * (float)envelope * 0.8f;
            case Sound.LowTom:
                if (t > 0.8)
                {
                    finished = true;
                    return 0;
                }

                return kit == 1 ? Sweep(ref voice, t, rate, 75, 125, 0.08, 0.35) : kit == 2 ? Sweep(ref voice, t, rate, 95, 130, 0.04, 0.2) * 0.6f : Sweep(ref voice, t, rate, 95, 150, 0.04, 0.2);
            case Sound.HighTom:
                if (t > 0.7)
                {
                    finished = true;
                    return 0;
                }

                return kit == 1 ? Sweep(ref voice, t, rate, 130, 210, 0.08, 0.3) : kit == 2 ? Sweep(ref voice, t, rate, 160, 200, 0.04, 0.17) * 0.6f : Sweep(ref voice, t, rate, 160, 230, 0.04, 0.17);
            default:
                if (t > (kit == 2 ? 3.2 : 2.8))
                {
                    finished = true;
                    return 0;
                }

                if (kit == 2)
                {
                    return HighPass(ref voice, NextNoise(ref voice.Noise), 0.1f) * (float)(Math.Min(1, t / 0.08) * Math.Exp(-t / 0.7)) * 0.35f;
                }

                return HighPass(ref voice, NextNoise(ref voice.Noise), kit == 1 ? 0.03f : 0.06f) * (float)Math.Exp(-t / (kit == 1 ? 0.35 : 0.55)) * 0.5f;
        }
    }

    private struct Voice
    {
        public bool Active;
        public Sound Sound;
        public long Frame;
        public float Gain;
        public float PanL;
        public int Kit;
        public float PanR;
        public double Phase;
        public float Lowpass;
        public uint Noise;
    }
}
