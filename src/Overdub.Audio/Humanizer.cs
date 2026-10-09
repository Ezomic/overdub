namespace Overdub.Audio;

public static class Humanizer
{
    public static readonly string[] FeelNames = ["Tight", "Light", "Loose"];

    public static int StableSeed(string id, long start)
    {
        var hash = 17;
        foreach (var c in id)
        {
            hash = (hash * 31) + c;
        }

        return (hash ^ (int)(start % int.MaxValue)) | 1;
    }

    public static MidiEvent[] Apply(List<MidiEvent> events, int feel, int seed, int sampleRate, long total)
    {
        var random = new Random(seed);
        var timing = sampleRate * 0.004 * feel;
        var result = events.ToArray();
        for (var i = 2; i + 1 < result.Length; i += 2)
        {
            var on = result[i];
            var off = result[i + 1];
            if (on.Kind != MidiKind.Note || off.Kind != MidiKind.Note)
            {
                continue;
            }

            var shift = (long)((random.NextDouble() * 2 - 1) * timing);
            shift = Math.Max(-on.At, shift);
            var velocity = on.Velocity < 50 ? on.Velocity : Math.Clamp(on.Velocity + (int)((random.NextDouble() * 2 - 1) * 7 * feel), 1, 127);
            result[i] = on with { At = Math.Min(total, on.At + shift), Velocity = (byte)velocity };
            result[i + 1] = off with { At = Math.Min(total, off.At + shift) };
        }

        return result;
    }
}
