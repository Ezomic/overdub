namespace Overdub.Audio;

public sealed class LatencyProbe
{
    private const int Bursts = 5;
    private const double OnsetFraction = 0.25;

    private readonly int _rate;
    private readonly float[] _burst;
    private readonly int[] _starts;
    private readonly float[] _captured;
    private readonly TaskCompletionSource<int?> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private long _position;

    public LatencyProbe(int sampleRate, int input)
    {
        Input = input;
        _rate = sampleRate;
        _burst = MakeBurst(sampleRate);
        _starts = StartsFor(sampleRate);
        _captured = new float[(int)(sampleRate * 2.4)];
    }

    public int Input { get; }
    public Task<int?> Completion => _done.Task;

    public static float[] MakeBurst(int sampleRate)
    {
        var length = sampleRate * 6 / 1000;
        var burst = new float[length];
        for (var t = 0; t < length; t++)
        {
            var envelope = Math.Sin(Math.PI * t / length);
            burst[t] = (float)(0.5 * envelope * Math.Sin(2 * Math.PI * 1000 * t / sampleRate));
        }

        return burst;
    }

    public static int[] StartsFor(int sampleRate) =>
        Enumerable.Range(0, Bursts).Select(k => (int)(sampleRate * (0.2 + (0.4 * k)))).ToArray();

    public void Process(float[] input, float[] left, float[] right, int frames)
    {
        if (_done.Task.IsCompleted)
        {
            return;
        }

        for (var i = 0; i < frames; i++)
        {
            var at = _position + i;
            if (at >= _captured.Length)
            {
                break;
            }

            _captured[at] = input[i];
            foreach (var start in _starts)
            {
                var t = at - start;
                if (t >= 0 && t < _burst.Length)
                {
                    left[i] += _burst[t];
                    right[i] += _burst[t];
                }
            }
        }

        _position += frames;
        if (_position >= _captured.Length)
        {
            _done.TrySetResult(Analyze(_captured, _starts, MakeBurst(_rate), _rate));
        }
    }

    public static int? Analyze(float[] captured, int[] starts, float[] burst, int sampleRate)
    {
        var window = sampleRate / 4;
        var referenceOnset = FirstAbove(burst, 0, burst.Length, burst.Max(Math.Abs) * OnsetFraction);
        var delays = new List<int>();
        foreach (var start in starts)
        {
            var end = Math.Min(captured.Length, start + window);
            if (start >= end)
            {
                continue;
            }

            var peak = 0f;
            for (var i = start; i < end; i++)
            {
                peak = Math.Max(peak, Math.Abs(captured[i]));
            }

            if (peak < 0.02f)
            {
                continue;
            }

            var onset = FirstAbove(captured, start, end, peak * OnsetFraction);
            delays.Add(onset - referenceOnset);
        }

        if (delays.Count < 3)
        {
            return null;
        }

        delays.Sort();
        return Math.Max(0, delays[delays.Count / 2]);
    }

    private static int FirstAbove(float[] data, int from, int to, double threshold)
    {
        for (var i = from; i < to; i++)
        {
            if (Math.Abs(data[i]) >= threshold)
            {
                return i - from;
            }
        }

        return 0;
    }
}
