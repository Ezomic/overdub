namespace Overdub.Audio;

public readonly record struct NoteReading(string Name, int Octave, double Cents, double Frequency);

public static class PitchDetector
{
    private static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public static (double Frequency, double Clarity)? Detect(float[] x, int sampleRate, double minHz = 25, double maxHz = 1000)
    {
        var n = x.Length;
        var tauMax = Math.Min(n / 2, (int)(sampleRate / minHz));
        var tauMin = Math.Max(2, (int)(sampleRate / maxHz));
        var window = n - tauMax;

        double energy = 0;
        foreach (var v in x)
        {
            energy += v * v;
        }

        if (Math.Sqrt(energy / n) < 0.003)
        {
            return null;
        }

        var difference = new double[tauMax + 1];
        for (var tau = 1; tau <= tauMax; tau++)
        {
            double sum = 0;
            for (var j = 0; j < window; j++)
            {
                var d = x[j] - x[j + tau];
                sum += d * d;
            }

            difference[tau] = sum;
        }

        var normalized = new double[tauMax + 1];
        normalized[0] = 1;
        double running = 0;
        for (var tau = 1; tau <= tauMax; tau++)
        {
            running += difference[tau];
            normalized[tau] = running == 0 ? 1 : difference[tau] * tau / running;
        }

        var best = -1;
        for (var tau = tauMin; tau <= tauMax; tau++)
        {
            if (normalized[tau] >= 0.15)
            {
                continue;
            }

            while (tau + 1 <= tauMax && normalized[tau + 1] < normalized[tau])
            {
                tau++;
            }

            best = tau;
            break;
        }

        if (best < 0)
        {
            var lowest = tauMin;
            for (var tau = tauMin; tau <= tauMax; tau++)
            {
                if (normalized[tau] < normalized[lowest])
                {
                    lowest = tau;
                }
            }

            if (normalized[lowest] > 0.35)
            {
                return null;
            }

            best = lowest;
        }

        var refined = (double)best;
        if (best > 1 && best < tauMax)
        {
            var s0 = normalized[best - 1];
            var s1 = normalized[best];
            var s2 = normalized[best + 1];
            var curvature = s0 + s2 - (2 * s1);
            if (Math.Abs(curvature) > 1e-12)
            {
                refined = best + ((s0 - s2) / (2 * curvature));
            }
        }

        return (sampleRate / refined, 1 - normalized[best]);
    }

    public static NoteReading ToNote(double frequency)
    {
        var exact = 69 + (12 * Math.Log2(frequency / 440.0));
        var midi = (int)Math.Round(exact);
        return new NoteReading(Names[((midi % 12) + 12) % 12], (midi / 12) - 1, (exact - midi) * 100, frequency);
    }
}
