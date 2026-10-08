using System.Runtime.CompilerServices;

namespace Overdub.Audio;

public static class TimeStretcher
{
    private const int WindowSize = 2048;
    private const int HopOut = WindowSize / 2;
    private const int SearchRange = 384;
    private const int CorrelationLength = 1024;

    private static readonly ConditionalWeakTable<float[], Dictionary<string, (float[] Left, float[]? Right)>> Cache = [];

    public static (float[] Left, float[]? Right) Get(float[] left, float[]? right, long offset, long length, double speed)
    {
        var perSource = Cache.GetOrCreateValue(left);
        var key = $"{offset}:{length}:{speed:0.####}";
        lock (perSource)
        {
            if (perSource.TryGetValue(key, out var hit))
            {
                return hit;
            }
        }

        var result = Stretch(left, right, (int)offset, (int)length, speed);
        lock (perSource)
        {
            perSource[key] = result;
        }

        return result;
    }

    public static (float[] Left, float[]? Right) Stretch(float[] left, float[]? right, int offset, int length, double speed)
    {
        var outputLength = Math.Max(1, (int)Math.Round(length / speed));
        var outLeft = new float[outputLength + WindowSize];
        var outRight = right is null ? null : new float[outputLength + WindowSize];
        if (length < WindowSize)
        {
            Array.Copy(left, offset, outLeft, 0, length);
            if (right is not null)
            {
                Array.Copy(right, offset, outRight!, 0, length);
            }

            return (outLeft[..outputLength], outRight?[..outputLength]);
        }

        var mono = new float[length];
        for (var i = 0; i < length; i++)
        {
            mono[i] = right is null ? left[offset + i] : (left[offset + i] + right[offset + i]) * 0.5f;
        }

        var window = new float[WindowSize];
        for (var i = 0; i < WindowSize; i++)
        {
            window[i] = (float)(0.5 - (0.5 * Math.Cos(2 * Math.PI * i / WindowSize)));
        }

        var hopIn = HopOut * speed;
        var frames = (int)Math.Ceiling((double)outputLength / HopOut) + 1;
        var previous = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            var outPosition = frame * HopOut;
            var nominal = (int)Math.Round(frame * hopIn);
            var start = nominal;
            if (frame > 0)
            {
                start = BestMatch(mono, previous + HopOut, nominal);
            }

            start = Math.Clamp(start, 0, Math.Max(0, length - 1));
            var available = Math.Min(WindowSize, length - start);
            for (var i = 0; i < available; i++)
            {
                var w = window[i];
                outLeft[outPosition + i] += w * left[offset + start + i];
                if (outRight is not null)
                {
                    outRight[outPosition + i] += w * right![offset + start + i];
                }
            }

            previous = start;
        }

        return (outLeft[..outputLength], outRight?[..outputLength]);
    }

    private static int BestMatch(float[] mono, int target, int nominal)
    {
        var length = mono.Length;
        var span = Math.Min(CorrelationLength, length - target);
        if (span <= 0)
        {
            return Math.Clamp(nominal, 0, length - 1);
        }

        var low = Math.Max(0, nominal - SearchRange);
        var high = Math.Min(length - span, nominal + SearchRange);
        if (high < low)
        {
            return Math.Clamp(nominal, 0, length - 1);
        }

        var best = Math.Clamp(nominal, low, high);
        var bestScore = double.MaxValue;
        for (var candidate = low; candidate <= high; candidate += 2)
        {
            var score = Difference(mono, target, candidate, span, 4, bestScore);
            if (score < bestScore)
            {
                bestScore = score;
                best = candidate;
            }
        }

        var refined = best;
        var refinedScore = double.MaxValue;
        for (var candidate = Math.Max(low, best - 1); candidate <= Math.Min(high, best + 1); candidate++)
        {
            var score = Difference(mono, target, candidate, span, 4, refinedScore);
            if (score < refinedScore)
            {
                refinedScore = score;
                refined = candidate;
            }
        }

        return refined;
    }

    private static double Difference(float[] mono, int target, int candidate, int span, int step, double stopAbove)
    {
        double sum = 0;
        for (var j = 0; j < span; j += step)
        {
            sum += Math.Abs(mono[target + j] - mono[candidate + j]);
            if (sum > stopAbove)
            {
                return sum;
            }
        }

        return sum;
    }
}
