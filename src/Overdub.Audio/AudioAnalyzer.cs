using System.Numerics;

namespace Overdub.Audio;

public readonly record struct TempoResult(double Bpm, double Confidence, double Alternative);

public readonly record struct KeyResult(string Name, double Confidence, string Alternative);

public static class AudioAnalyzer
{
    private static readonly string[] Notes = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    private static readonly double[] MajorProfile = [6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88];
    private static readonly double[] MinorProfile = [6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17];

    public static TempoResult? DetectTempo(float[] samples, int sampleRate, double minBpm = 60, double maxBpm = 190)
    {
        const int frame = 1024;
        const int hop = 512;
        if (samples.Length < sampleRate * 4)
        {
            return null;
        }

        var envelope = OnsetEnvelope(samples, frame, hop);
        var fps = (double)sampleRate / hop;
        var mean = envelope.Average();
        var centered = envelope.Select(v => v - mean).ToArray();
        var maxLag = (int)(fps * 60 / minBpm * 4) + 2;
        var autocorrelation = new double[Math.Min(maxLag, centered.Length / 2)];
        for (var lag = 1; lag < autocorrelation.Length; lag++)
        {
            double sum = 0;
            for (var i = 0; i + lag < centered.Length; i++)
            {
                sum += centered[i] * centered[i + lag];
            }

            autocorrelation[lag] = sum / (centered.Length - lag);
        }

        double At(double lag)
        {
            if (lag < 1 || lag >= autocorrelation.Length - 1)
            {
                return 0;
            }

            var i = (int)lag;
            var f = lag - i;
            return autocorrelation[i] + ((autocorrelation[i + 1] - autocorrelation[i]) * f);
        }

        var best = (Bpm: 0.0, Score: double.MinValue);
        var scores = new List<double>();
        for (var bpm = minBpm; bpm <= maxBpm; bpm += 0.25)
        {
            var lag = fps * 60 / bpm;
            var prior = Math.Exp(-0.5 * Math.Pow(Math.Log2(bpm / 120) / 0.5, 2));
            var score = (At(lag) + (0.5 * At(lag * 2)) + (0.25 * At(lag * 4))) * prior;
            scores.Add(score);
            if (score > best.Score)
            {
                best = (bpm, score);
            }
        }

        if (best.Score <= 0)
        {
            return null;
        }

        var average = scores.Where(s => s > 0).DefaultIfEmpty(1).Average();
        var chosen = Math.Round(best.Bpm * 4) / 4;
        var alternative = chosen >= 100 ? chosen / 2 : chosen * 2;
        return new TempoResult(chosen, Math.Min(1, best.Score / (average * 6)), alternative);
    }

    public static KeyResult? DetectKey(float[] samples, int sampleRate)
    {
        if (samples.Length < ChromaWindow * 2)
        {
            return null;
        }

        return BestKey(Chroma(samples, 0, samples.Length, sampleRate));
    }

    private const int ChromaWindow = 8192;

    public static double[] Chroma(float[] samples, int offset, int length, int sampleRate)
    {
        const int size = ChromaWindow;
        const int hop = 4096;
        var chroma = new double[12];
        if (length < size)
        {
            return chroma;
        }

        var window = Hann(size);
        var buffer = new Complex[size];
        var bins = new List<(int Bin, int PitchClass, double Weight)>();
        for (var bin = 1; bin < size / 2; bin++)
        {
            var frequency = bin * (double)sampleRate / size;
            if (frequency is < 65 or > 1800)
            {
                continue;
            }

            var midi = 69 + (12 * Math.Log2(frequency / 440.0));
            var nearest = (int)Math.Round(midi);
            if (Math.Abs(midi - nearest) <= 0.35)
            {
                bins.Add((bin, ((nearest % 12) + 12) % 12, Math.Pow(frequency / 65.0, -0.5)));
            }
        }

        for (var start = offset; start + size <= offset + length && start + size <= samples.Length; start += hop)
        {
            for (var i = 0; i < size; i++)
            {
                buffer[i] = samples[start + i] * window[i];
            }

            Fft.Transform(buffer);
            foreach (var (bin, pitchClass, weight) in bins)
            {
                chroma[pitchClass] += Math.Pow(buffer[bin].Magnitude, 0.8) * weight;
            }
        }

        return chroma;
    }

    public static KeyResult? DetectKey(IEnumerable<MidiNote> notes)
    {
        var chroma = new double[12];
        foreach (var note in notes)
        {
            chroma[note.Pitch % 12] += Math.Max(1, note.End - note.Start);
        }

        return chroma.Sum() <= 0 ? null : BestKey(chroma);
    }

    private static KeyResult BestKey(double[] chroma)
    {
        var scores = new List<(string Name, double Score)>();
        for (var tonic = 0; tonic < 12; tonic++)
        {
            scores.Add(($"{Notes[tonic]} major", Correlation(chroma, MajorProfile, tonic)));
            scores.Add(($"{Notes[tonic]} minor", Correlation(chroma, MinorProfile, tonic)));
        }

        var ordered = scores.OrderByDescending(s => s.Score).ToList();
        return new KeyResult(ordered[0].Name, Math.Clamp(ordered[0].Score, 0, 1), ordered[1].Name);
    }

    private static double Correlation(double[] chroma, double[] profile, int tonic)
    {
        var meanC = chroma.Average();
        var meanP = profile.Average();
        double covariance = 0;
        double varianceC = 0;
        double varianceP = 0;
        for (var i = 0; i < 12; i++)
        {
            var c = chroma[(tonic + i) % 12] - meanC;
            var p = profile[i] - meanP;
            covariance += c * p;
            varianceC += c * c;
            varianceP += p * p;
        }

        return varianceC == 0 ? 0 : covariance / Math.Sqrt(varianceC * varianceP);
    }

    private static double[] OnsetEnvelope(float[] samples, int frame, int hop)
    {
        var window = Hann(frame);
        var buffer = new Complex[frame];
        var previous = new double[frame / 2];
        var frames = (samples.Length - frame) / hop;
        var envelope = new double[frames];
        for (var f = 0; f < frames; f++)
        {
            for (var i = 0; i < frame; i++)
            {
                buffer[i] = samples[(f * hop) + i] * window[i];
            }

            Fft.Transform(buffer);
            double flux = 0;
            for (var bin = 1; bin < frame / 2; bin++)
            {
                var magnitude = Math.Log(1 + (100 * buffer[bin].Magnitude));
                flux += Math.Max(0, magnitude - previous[bin]);
                previous[bin] = magnitude;
            }

            envelope[f] = flux;
        }

        return envelope;
    }

    private static double[] Hann(int size) =>
        Enumerable.Range(0, size).Select(i => 0.5 - (0.5 * Math.Cos(2 * Math.PI * i / (size - 1)))).ToArray();
}
