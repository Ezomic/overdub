namespace Overdub.Audio;

public sealed record EffectParameter(string Name, double Min, double Max, double Default, string Unit, bool Integer = false);

public abstract class Effect
{
    protected Effect()
    {
        Values = new double[0];
    }

    public abstract string Name { get; }
    public abstract IReadOnlyList<EffectParameter> Parameters { get; }
    public bool Enabled { get; set; }
    public double[] Values { get; private set; }
    protected int SampleRate { get; private set; } = 44100;

    public void Initialize()
    {
        Values = Parameters.Select(p => p.Default).ToArray();
        ParametersChanged();
    }

    public void Configure(int sampleRate)
    {
        SampleRate = sampleRate;
        Reset();
        ParametersChanged();
    }

    public void CopyFrom(Effect other)
    {
        Enabled = other.Enabled;
        Array.Copy(other.Values, Values, Math.Min(other.Values.Length, Values.Length));
        ParametersChanged();
    }

    public double Get(int index) => Values[index];

    public void Set(int index, double value)
    {
        var parameter = Parameters[index];
        Values[index] = Math.Clamp(parameter.Integer ? Math.Round(value) : value, parameter.Min, parameter.Max);
        ParametersChanged();
    }

    public abstract void Process(float[] left, float[] right, int frames);

    public virtual void Reset()
    {
    }

    protected virtual void ParametersChanged()
    {
    }

    protected static double DbToLinear(double db) => Math.Pow(10, db / 20);
}

public sealed class EffectChain : IDisposable
{
    private int _version;
    private int _configuredRate;

    public EffectChain()
    {
        Effects = [new AmpSim(), new Equalizer(), new Compressor(), new Reverb()];
        foreach (var effect in Effects)
        {
            effect.Initialize();
        }
    }

    public IReadOnlyList<Effect> Effects { get; }
    public int Version => Volatile.Read(ref _version);
    public int AppliedVersion { get; private set; } = -1;
    public Vst3.PluginSlot Plugin { get; } = new();
    public bool AnyEnabled => Effects.Any(e => e.Enabled) || Plugin.Active;

    public void Dispose() => Plugin.Dispose();

    public void Touch() => Interlocked.Increment(ref _version);

    public void Configure(int sampleRate)
    {
        if (_configuredRate == sampleRate)
        {
            return;
        }

        _configuredRate = sampleRate;
        foreach (var effect in Effects)
        {
            effect.Configure(sampleRate);
        }
    }

    public void CopyFrom(EffectChain source)
    {
        for (var i = 0; i < Effects.Count; i++)
        {
            Effects[i].CopyFrom(source.Effects[i]);
        }

        Plugin.CopyFrom(source.Plugin);

        AppliedVersion = source.Version;
    }

    public EffectChain CloneForProcessing(int sampleRate)
    {
        var clone = new EffectChain();
        clone.Configure(sampleRate);
        clone.CopyFrom(this);
        clone.Plugin.Sync(sampleRate);
        return clone;
    }

    public void Process(float[] left, float[] right, int frames)
    {
        Plugin.Process(left, right, frames);
        foreach (var effect in Effects)
        {
            if (effect.Enabled)
            {
                effect.Process(left, right, frames);
            }
        }
    }
}

internal sealed class Biquad
{
    private double _b0 = 1;
    private double _b1;
    private double _b2;
    private double _a1;
    private double _a2;
    private double _x1;
    private double _x2;
    private double _y1;
    private double _y2;

    public void Reset() => _x1 = _x2 = _y1 = _y2 = 0;

    public float Process(float input)
    {
        var x = (double)input;
        var y = (_b0 * x) + (_b1 * _x1) + (_b2 * _x2) - (_a1 * _y1) - (_a2 * _y2);
        _x2 = _x1;
        _x1 = x;
        _y2 = _y1;
        _y1 = y;
        return (float)y;
    }

    public void LowShelf(double sampleRate, double frequency, double gainDb, double slope = 1.0) => Shelf(sampleRate, frequency, gainDb, slope, low: true);

    public void HighShelf(double sampleRate, double frequency, double gainDb, double slope = 1.0) => Shelf(sampleRate, frequency, gainDb, slope, low: false);

    public void Peaking(double sampleRate, double frequency, double gainDb, double q)
    {
        var a = Math.Pow(10, gainDb / 40);
        var w = 2 * Math.PI * frequency / sampleRate;
        var alpha = Math.Sin(w) / (2 * q);
        var cos = Math.Cos(w);
        Set(1 + (alpha * a), -2 * cos, 1 - (alpha * a), 1 + (alpha / a), -2 * cos, 1 - (alpha / a));
    }

    public void LowPass(double sampleRate, double frequency, double q)
    {
        var w = 2 * Math.PI * frequency / sampleRate;
        var alpha = Math.Sin(w) / (2 * q);
        var cos = Math.Cos(w);
        Set((1 - cos) / 2, 1 - cos, (1 - cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    public void HighPass(double sampleRate, double frequency, double q)
    {
        var w = 2 * Math.PI * frequency / sampleRate;
        var alpha = Math.Sin(w) / (2 * q);
        var cos = Math.Cos(w);
        Set((1 + cos) / 2, -(1 + cos), (1 + cos) / 2, 1 + alpha, -2 * cos, 1 - alpha);
    }

    private void Shelf(double sampleRate, double frequency, double gainDb, double slope, bool low)
    {
        var a = Math.Pow(10, gainDb / 40);
        var w = 2 * Math.PI * frequency / sampleRate;
        var cos = Math.Cos(w);
        var sin = Math.Sin(w);
        var alpha = sin / 2 * Math.Sqrt(((a + (1 / a)) * ((1 / slope) - 1)) + 2);
        var root = 2 * Math.Sqrt(a) * alpha;
        if (low)
        {
            Set(
                a * ((a + 1) - ((a - 1) * cos) + root),
                2 * a * ((a - 1) - ((a + 1) * cos)),
                a * ((a + 1) - ((a - 1) * cos) - root),
                (a + 1) + ((a - 1) * cos) + root,
                -2 * ((a - 1) + ((a + 1) * cos)),
                (a + 1) + ((a - 1) * cos) - root);
        }
        else
        {
            Set(
                a * ((a + 1) + ((a - 1) * cos) + root),
                -2 * a * ((a - 1) + ((a + 1) * cos)),
                a * ((a + 1) + ((a - 1) * cos) - root),
                (a + 1) - ((a - 1) * cos) + root,
                2 * ((a - 1) - ((a + 1) * cos)),
                (a + 1) - ((a - 1) * cos) - root);
        }
    }

    private void Set(double b0, double b1, double b2, double a0, double a1, double a2)
    {
        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
    }
}

public sealed class Equalizer : Effect
{
    private static readonly EffectParameter[] Spec =
    [
        new("Low", -12, 12, 0, "dB"),
        new("Mid", -12, 12, 0, "dB"),
        new("Mid frequency", 200, 5000, 1000, "Hz"),
        new("High", -12, 12, 0, "dB"),
    ];

    private readonly Biquad[] _low = [new(), new()];
    private readonly Biquad[] _mid = [new(), new()];
    private readonly Biquad[] _high = [new(), new()];

    public override string Name => "Equalizer";
    public override IReadOnlyList<EffectParameter> Parameters => Spec;

    public override void Process(float[] left, float[] right, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            left[i] = _high[0].Process(_mid[0].Process(_low[0].Process(left[i])));
            right[i] = _high[1].Process(_mid[1].Process(_low[1].Process(right[i])));
        }
    }

    public override void Reset()
    {
        foreach (var filter in _low.Concat(_mid).Concat(_high))
        {
            filter.Reset();
        }
    }

    protected override void ParametersChanged()
    {
        if (Values.Length < 4)
        {
            return;
        }

        for (var c = 0; c < 2; c++)
        {
            _low[c].LowShelf(SampleRate, 120, Values[0]);
            _mid[c].Peaking(SampleRate, Values[2], Values[1], 0.9);
            _high[c].HighShelf(SampleRate, 6000, Values[3]);
        }
    }
}

public sealed class Compressor : Effect
{
    private static readonly EffectParameter[] Spec =
    [
        new("Threshold", -40, 0, -18, "dB"),
        new("Ratio", 1, 20, 3, ":1"),
        new("Attack", 1, 100, 10, "ms"),
        new("Release", 20, 1000, 150, "ms"),
        new("Makeup", 0, 24, 3, "dB"),
    ];

    private double _envelopeDb = -120;

    public override string Name => "Compressor";
    public override IReadOnlyList<EffectParameter> Parameters => Spec;

    public override void Reset() => _envelopeDb = -120;

    public override void Process(float[] left, float[] right, int frames)
    {
        var threshold = Values[0];
        var slope = 1 - (1 / Math.Max(1, Values[1]));
        var attack = Math.Exp(-1.0 / (Values[2] / 1000 * SampleRate));
        var release = Math.Exp(-1.0 / (Values[3] / 1000 * SampleRate));
        var makeup = Values[4];
        for (var i = 0; i < frames; i++)
        {
            var level = Math.Max(Math.Abs(left[i]), Math.Abs(right[i]));
            var db = level < 1e-6 ? -120 : 20 * Math.Log10(level);
            var coefficient = db > _envelopeDb ? attack : release;
            _envelopeDb = (coefficient * _envelopeDb) + ((1 - coefficient) * db);
            var over = _envelopeDb - threshold;
            var reduction = over > 0 ? over * slope : 0;
            var gain = (float)DbToLinear(makeup - reduction);
            left[i] *= gain;
            right[i] *= gain;
        }
    }
}

public sealed class Reverb : Effect
{
    private static readonly int[] CombTunings = [1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617];
    private static readonly int[] AllpassTunings = [556, 441, 341, 225];
    private const int Spread = 23;

    private static readonly EffectParameter[] Spec =
    [
        new("Room size", 0, 1, 0.6, ""),
        new("Damping", 0, 1, 0.5, ""),
        new("Mix", 0, 1, 0.25, ""),
    ];

    private float[][][] _combs = [];
    private int[][] _combIndex = [];
    private float[][] _combFilter = [];
    private float[][][] _allpasses = [];
    private int[][] _allpassIndex = [];

    public override string Name => "Reverb";
    public override IReadOnlyList<EffectParameter> Parameters => Spec;

    public override void Reset()
    {
        var scale = SampleRate / 44100.0;
        _combs = new float[2][][];
        _combIndex = new int[2][];
        _combFilter = new float[2][];
        _allpasses = new float[2][][];
        _allpassIndex = new int[2][];
        for (var c = 0; c < 2; c++)
        {
            var offset = c * Spread;
            _combs[c] = CombTunings.Select(t => new float[(int)((t + offset) * scale)]).ToArray();
            _combIndex[c] = new int[CombTunings.Length];
            _combFilter[c] = new float[CombTunings.Length];
            _allpasses[c] = AllpassTunings.Select(t => new float[(int)((t + offset) * scale)]).ToArray();
            _allpassIndex[c] = new int[AllpassTunings.Length];
        }
    }

    public override void Process(float[] left, float[] right, int frames)
    {
        if (_combs.Length == 0)
        {
            Reset();
        }

        var feedback = (float)((Values[0] * 0.28) + 0.7);
        var damp = (float)(Values[1] * 0.4);
        var mix = (float)Values[2];
        for (var i = 0; i < frames; i++)
        {
            var input = (left[i] + right[i]) * 0.015f;
            var wetL = Tank(0, input, feedback, damp);
            var wetR = Tank(1, input, feedback, damp);
            left[i] = (left[i] * (1 - mix)) + (wetL * mix * 3);
            right[i] = (right[i] * (1 - mix)) + (wetR * mix * 3);
        }
    }

    private float Tank(int channel, float input, float feedback, float damp)
    {
        float sum = 0;
        var combs = _combs[channel];
        for (var k = 0; k < combs.Length; k++)
        {
            var buffer = combs[k];
            var index = _combIndex[channel][k];
            var output = buffer[index];
            _combFilter[channel][k] = (output * (1 - damp)) + (_combFilter[channel][k] * damp);
            buffer[index] = input + (_combFilter[channel][k] * feedback);
            _combIndex[channel][k] = (index + 1) % buffer.Length;
            sum += output;
        }

        var allpasses = _allpasses[channel];
        for (var k = 0; k < allpasses.Length; k++)
        {
            var buffer = allpasses[k];
            var index = _allpassIndex[channel][k];
            var buffered = buffer[index];
            var result = -sum + buffered;
            buffer[index] = sum + (buffered * 0.5f);
            _allpassIndex[channel][k] = (index + 1) % buffer.Length;
            sum = result;
        }

        return sum;
    }
}

public sealed class AmpSim : Effect
{
    private static readonly double[] ModelGain = [3, 12, 40, 8];
    private static readonly double[] MidFrequency = [800, 700, 900, 400];
    private static readonly double[] CabinetLow = [5600, 4800, 4200, 3600];
    private static readonly float[] ModelTrim = [0.7f, 0.5f, 0.35f, 0.6f];

    private static readonly EffectParameter[] Spec =
    [
        new("Model (clean, crunch, lead, bass)", 0, 3, 1, "", true),
        new("Drive", 0, 1, 0.5, ""),
        new("Bass", -12, 12, 0, "dB"),
        new("Mid", -12, 12, 0, "dB"),
        new("Treble", -12, 12, 0, "dB"),
        new("Presence", 0, 1, 0.3, ""),
        new("Cabinet", 0, 1, 1, "", true),
        new("Level", -12, 6, 0, "dB"),
    ];

    private readonly Biquad _inputHighPass = new();
    private readonly Biquad _bass = new();
    private readonly Biquad _mid = new();
    private readonly Biquad _treble = new();
    private readonly Biquad _presence = new();
    private readonly Biquad _cabinetHigh = new();
    private readonly Biquad _cabinetLowA = new();
    private readonly Biquad _cabinetLowB = new();
    private readonly Biquad _cabinetBody = new();
    private float _dcX;
    private float _dcY;

    public override string Name => "Amp and cabinet";
    public override IReadOnlyList<EffectParameter> Parameters => Spec;

    public override void Reset()
    {
        foreach (var filter in new[] { _inputHighPass, _bass, _mid, _treble, _presence, _cabinetHigh, _cabinetLowA, _cabinetLowB, _cabinetBody })
        {
            filter.Reset();
        }

        _dcX = _dcY = 0;
    }

    public override void Process(float[] left, float[] right, int frames)
    {
        var model = (int)Values[0];
        var gain = 1 + (Values[1] * ModelGain[model]);
        var cabinet = Values[6] >= 0.5;
        var level = (float)DbToLinear(Values[7]);
        var trim = ModelTrim[model];
        const double bias = 0.12;
        var biasOffset = Math.Tanh(bias);
        for (var i = 0; i < frames; i++)
        {
            var x = _inputHighPass.Process((left[i] + right[i]) * 0.5f);
            var shaped = (float)(Math.Tanh((x * gain) + bias) - biasOffset);
            var dc = shaped - _dcX + (0.995f * _dcY);
            _dcX = shaped;
            _dcY = dc;
            var y = _treble.Process(_mid.Process(_bass.Process(dc)));
            y = _presence.Process(y);
            if (cabinet)
            {
                y = _cabinetLowB.Process(_cabinetLowA.Process(_cabinetBody.Process(_cabinetHigh.Process(y))));
            }

            y *= level * trim;
            left[i] = y;
            right[i] = y;
        }
    }

    protected override void ParametersChanged()
    {
        if (Values.Length < 8)
        {
            return;
        }

        var model = (int)Values[0];
        _inputHighPass.HighPass(SampleRate, 70, 0.707);
        _bass.LowShelf(SampleRate, 120, Values[2]);
        _mid.Peaking(SampleRate, MidFrequency[model], Values[3], 0.8);
        _treble.HighShelf(SampleRate, 3000, Values[4]);
        _presence.HighShelf(SampleRate, 5000, Values[5] * 8);
        _cabinetHigh.HighPass(SampleRate, model == 3 ? 40 : 85, 0.707);
        _cabinetBody.Peaking(SampleRate, model == 3 ? 80 : 110, 3, 1.2);
        _cabinetLowA.LowPass(SampleRate, CabinetLow[model], 0.707);
        _cabinetLowB.LowPass(SampleRate, CabinetLow[model], 0.707);
    }
}
