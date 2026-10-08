namespace Overdub.Audio;

public sealed class TapTempo
{
    private const long ResetAfterMilliseconds = 2000;
    private const int Window = 5;

    private readonly List<long> _taps = [];

    public double? Tap(long nowMilliseconds)
    {
        if (_taps.Count > 0 && nowMilliseconds - _taps[^1] > ResetAfterMilliseconds)
        {
            _taps.Clear();
        }

        _taps.Add(nowMilliseconds);
        if (_taps.Count > Window)
        {
            _taps.RemoveAt(0);
        }

        if (_taps.Count < 2)
        {
            return null;
        }

        var average = (double)(_taps[^1] - _taps[0]) / (_taps.Count - 1);
        return Math.Clamp(60000.0 / average, 20, 300);
    }
}
