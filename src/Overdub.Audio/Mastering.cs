using NAudio.Wave;

namespace Overdub.Audio;

public sealed record MasterPreset(string Name, double[] Eq, double[] Compressor, double CeilingDb, double? TargetLufs);

public sealed record LoudnessReport(double Lufs, double PeakDb, double GainDb);

public static class Mastering
{
    public static IReadOnlyList<MasterPreset> Presets { get; } =
    [
        new("Off", [0, 0, 1000, 0], [-18, 1, 10, 150, 0], -1, null),
        new("Polish", [0, 0, 1000, 1.5], [-20, 2, 20, 200, 2], -1, null),
        new("Streaming, -14 LUFS", [0, 0, 1000, 1.5], [-22, 2.5, 15, 180, 2], -1, -14),
        new("Loud, -9 LUFS", [1, 0, 1000, 2], [-24, 3.5, 10, 120, 3], -0.5, -9),
    ];

    public static LoudnessReport Master(string inputWav, string outputWav, MasterPreset preset)
    {
        var (left, right, rate) = Read(inputWav);
        var eq = new Equalizer();
        var comp = new Compressor();
        eq.Initialize();
        comp.Initialize();
        eq.Configure(rate);
        comp.Configure(rate);
        for (var i = 0; i < preset.Eq.Length; i++)
        {
            eq.Set(i, preset.Eq[i]);
        }

        for (var i = 0; i < preset.Compressor.Length; i++)
        {
            comp.Set(i, preset.Compressor[i]);
        }

        eq.Enabled = true;
        comp.Enabled = true;
        const int block = 4096;
        for (var at = 0; at < left.Length; at += block)
        {
            var count = Math.Min(block, left.Length - at);
            var l = new float[count];
            var r = new float[count];
            Array.Copy(left, at, l, 0, count);
            Array.Copy(right, at, r, 0, count);
            eq.Process(l, r, count);
            comp.Process(l, r, count);
            Array.Copy(l, 0, left, at, count);
            Array.Copy(r, 0, right, at, count);
        }

        var gain = 0.0;
        float[] outL = left;
        float[] outR = right;
        var ceiling = Math.Pow(10, preset.CeilingDb / 20);
        for (var pass = 0; pass < 4; pass++)
        {
            outL = (float[])left.Clone();
            outR = (float[])right.Clone();
            var g = (float)Math.Pow(10, gain / 20);
            for (var i = 0; i < outL.Length; i++)
            {
                outL[i] *= g;
                outR[i] *= g;
            }

            Limit(outL, outR, rate, ceiling);
            if (preset.TargetLufs is not { } target)
            {
                break;
            }

            var measured = LoudnessMeter.Integrated(outL, outR, rate);
            if (measured is null || Math.Abs(target - measured.Value) < 0.3)
            {
                break;
            }

            gain = Math.Clamp(gain + (target - measured.Value), -12, 24);
        }

        Write(outputWav, outL, outR, rate);
        var lufs = LoudnessMeter.Integrated(outL, outR, rate) ?? double.NegativeInfinity;
        var peak = Math.Max(outL.Select(Math.Abs).DefaultIfEmpty(0).Max(), outR.Select(Math.Abs).DefaultIfEmpty(0).Max());
        return new LoudnessReport(lufs, 20 * Math.Log10(Math.Max(1e-6, peak)), gain);
    }

    public static LoudnessReport Measure(string wav)
    {
        var (left, right, rate) = Read(wav);
        var lufs = LoudnessMeter.Integrated(left, right, rate) ?? double.NegativeInfinity;
        var peak = Math.Max(left.Select(Math.Abs).DefaultIfEmpty(0).Max(), right.Select(Math.Abs).DefaultIfEmpty(0).Max());
        return new LoudnessReport(lufs, 20 * Math.Log10(Math.Max(1e-6, peak)), 0);
    }

    private static void Limit(float[] left, float[] right, int rate, double ceiling)
    {
        var look = Math.Max(16, rate / 700);
        var release = Math.Exp(-1.0 / (rate * 0.08));
        var gain = 1.0;
        var window = new LinkedList<(int Index, double Peak)>();
        var length = left.Length;
        var outL = new float[length];
        var outR = new float[length];
        for (var i = 0; i < length + look; i++)
        {
            var peak = i < length ? Math.Max(Math.Abs(left[i]), Math.Abs(right[i])) : 0.0;
            while (window.Last is { } last && last.Value.Peak <= peak)
            {
                window.RemoveLast();
            }

            window.AddLast((i, peak));
            while (window.First is { } first && first.Value.Index <= i - look)
            {
                window.RemoveFirst();
            }

            var windowPeak = window.First!.Value.Peak;
            var wanted = windowPeak > ceiling ? ceiling / windowPeak : 1.0;
            gain = wanted < gain ? wanted : (gain * release) + (wanted * (1 - release));
            var output = i - look;
            if (output >= 0)
            {
                outL[output] = (float)(left[output] * gain);
                outR[output] = (float)(right[output] * gain);
            }
        }

        Array.Copy(outL, left, length);
        Array.Copy(outR, right, length);
    }

    private static (float[] Left, float[] Right, int Rate) Read(string path)
    {
        using var reader = new AudioFileReader(path);
        var channels = reader.WaveFormat.Channels;
        var total = (int)(reader.Length / 4);
        var data = new float[total];
        var read = 0;
        var provider = (ISampleProvider)reader;
        while (read < total)
        {
            var n = provider.Read(data.AsSpan(read));
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        var frames = read / channels;
        var left = new float[frames];
        var right = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            left[i] = data[i * channels];
            right[i] = channels > 1 ? data[(i * channels) + 1] : data[i * channels];
        }

        return (left, right, reader.WaveFormat.SampleRate);
    }

    private static void Write(string path, float[] left, float[] right, int rate)
    {
        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(rate, 2));
        var buffer = new float[8192 * 2];
        for (var at = 0; at < left.Length; at += 8192)
        {
            var count = Math.Min(8192, left.Length - at);
            for (var i = 0; i < count; i++)
            {
                buffer[i * 2] = left[at + i];
                buffer[(i * 2) + 1] = right[at + i];
            }

            writer.WriteSamples(buffer, 0, count * 2);
        }
    }
}

public static class LoudnessMeter
{
    private sealed class Filter(double b0, double b1, double b2, double a1, double a2)
    {
        private double _z1;
        private double _z2;

        public double Process(double x)
        {
            var y = (b0 * x) + _z1;
            _z1 = (b1 * x) - (a1 * y) + _z2;
            _z2 = (b2 * x) - (a2 * y);
            return y;
        }
    }

    private static Filter[] KWeighting(int rate)
    {
        const double f1 = 1681.974450955533;
        const double g1 = 3.999843853973347;
        const double q1 = 0.7071752369554196;
        var k1 = Math.Tan(Math.PI * f1 / rate);
        var vh = Math.Pow(10, g1 / 20);
        var vb = Math.Pow(vh, 0.4996667741545416);
        var a0 = 1 + (k1 / q1) + (k1 * k1);
        var shelf = new Filter(
            (vh + (vb * k1 / q1) + (k1 * k1)) / a0,
            2 * ((k1 * k1) - vh) / a0,
            (vh - (vb * k1 / q1) + (k1 * k1)) / a0,
            2 * ((k1 * k1) - 1) / a0,
            (1 - (k1 / q1) + (k1 * k1)) / a0);
        const double f2 = 38.13547087602444;
        const double q2 = 0.5003270373238773;
        var k2 = Math.Tan(Math.PI * f2 / rate);
        var d0 = 1 + (k2 / q2) + (k2 * k2);
        var high = new Filter(1, -2, 1, 2 * ((k2 * k2) - 1) / d0, (1 - (k2 / q2) + (k2 * k2)) / d0);
        return [shelf, high];
    }

    public static double? Integrated(float[] left, float[] right, int rate)
    {
        var blockLength = (int)(0.4 * rate);
        var hop = (int)(0.1 * rate);
        if (left.Length < blockLength)
        {
            return null;
        }

        var weightedLeft = Weighted(left, rate);
        var weightedRight = Weighted(right, rate);
        var prefixLeft = Prefix(weightedLeft);
        var prefixRight = Prefix(weightedRight);
        var blocks = new List<double>();
        for (var start = 0; start + blockLength <= left.Length; start += hop)
        {
            var energy = ((prefixLeft[start + blockLength] - prefixLeft[start]) + (prefixRight[start + blockLength] - prefixRight[start])) / blockLength;
            blocks.Add(energy);
        }

        double ToLufs(double energy) => -0.691 + (10 * Math.Log10(Math.Max(energy, 1e-12)));
        var absolute = blocks.Where(e => ToLufs(e) > -70).ToList();
        if (absolute.Count == 0)
        {
            return null;
        }

        var relativeGate = ToLufs(absolute.Average()) - 10;
        var gated = absolute.Where(e => ToLufs(e) > relativeGate).ToList();
        return gated.Count == 0 ? null : ToLufs(gated.Average());
    }

    private static double[] Weighted(float[] x, int rate)
    {
        var filters = KWeighting(rate);
        var squared = new double[x.Length];
        for (var i = 0; i < x.Length; i++)
        {
            var v = filters[1].Process(filters[0].Process(x[i]));
            squared[i] = v * v;
        }

        return squared;
    }

    private static double[] Prefix(double[] values)
    {
        var sums = new double[values.Length + 1];
        for (var i = 0; i < values.Length; i++)
        {
            sums[i + 1] = sums[i] + values[i];
        }

        return sums;
    }
}
