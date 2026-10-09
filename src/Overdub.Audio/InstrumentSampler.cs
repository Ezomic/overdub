using System.Globalization;
using System.Text;
using NAudio.Wave;

namespace Overdub.Audio;

public sealed record SampledSlice(int Pitch, int Start, int Length, float Peak);

public sealed record SamplingAnalysis(IReadOnlyList<SampledSlice> Slices, int LowKey, int HighKey, IReadOnlyList<int> Gaps)
{
    public IReadOnlyList<int> Captured => Slices.Select(s => s.Pitch).Distinct().OrderBy(p => p).ToList();
}

public static class InstrumentSampler
{
    private const int MaxRoundRobins = 3;

    public static SamplingAnalysis Analyse(float[] samples, int offset, int length, int rate, MachineRole role)
    {
        var bass = role == MachineRole.Bass;
        var half = new float[length / 2];
        for (var i = 0; i < half.Length; i++)
        {
            half[i] = 0.5f * (samples[offset + (2 * i)] + samples[offset + (2 * i) + 1]);
        }

        var notes = NoteTranscriber.FromAudio(half, 0, half.Length, rate / 2, 0, bass ? 36 : 70, bass ? 2048 : 1024)
            .Where(n => n.Velocity > 0)
            .Select(n => n with { Start = n.Start * 2, End = n.End * 2 })
            .OrderBy(n => n.Start)
            .ToList();
        var minLength = rate / 5;
        var maxLength = (int)(rate * 2.5);
        var slices = new List<SampledSlice>();
        for (var i = 0; i < notes.Count; i++)
        {
            var start = (int)notes[i].Start;
            var next = i + 1 < notes.Count ? (int)notes[i + 1].Start : length;
            var end = Math.Min(next, Math.Min(start + maxLength, length));
            end = TrimSilence(samples, offset + start, end - start, rate) + start;
            if (end - start < minLength)
            {
                continue;
            }

            var peak = 0f;
            for (var s = start; s < end; s++)
            {
                peak = Math.Max(peak, Math.Abs(samples[offset + s]));
            }

            slices.Add(new SampledSlice(notes[i].Pitch, start, end - start, peak));
        }

        var kept = slices
            .GroupBy(s => s.Pitch)
            .SelectMany(g => g.OrderByDescending(s => s.Length).Take(MaxRoundRobins))
            .OrderBy(s => s.Pitch)
            .ThenBy(s => s.Start)
            .ToList();
        if (kept.Count == 0)
        {
            return new SamplingAnalysis(kept, 0, 0, []);
        }

        var low = kept.Min(s => s.Pitch);
        var high = kept.Max(s => s.Pitch);
        var captured = kept.Select(s => s.Pitch).ToHashSet();
        var gaps = Enumerable.Range(low, high - low + 1).Where(p => !captured.Contains(p)).ToList();
        return new SamplingAnalysis(kept, low, high, gaps);
    }

    public static string Create(float[] samples, int offset, int rate, SamplingAnalysis analysis, string name, string? root = null)
    {
        if (analysis.Slices.Count == 0)
        {
            throw new InvalidOperationException("There are no usable notes in that recording.");
        }

        root ??= SfzInstrument.Folder;
        var folderName = SafeName(name);
        var folder = Path.Combine(root, folderName);
        for (var n = 2; Directory.Exists(folder); n++)
        {
            folderName = $"{SafeName(name)} {n}";
            folder = Path.Combine(root, folderName);
        }

        var sampleFolder = Path.Combine(folder, "samples");
        Directory.CreateDirectory(sampleFolder);
        var gain = 0.9f / Math.Max(1e-4f, analysis.Slices.Max(s => s.Peak));
        var pitches = analysis.Captured;
        var sfz = new StringBuilder();
        sfz.AppendLine("// Made by Overdub from a recording.");
        sfz.AppendLine("<group>");
        sfz.AppendLine("ampeg_release=0.25");
        for (var p = 0; p < pitches.Count; p++)
        {
            var pitch = pitches[p];
            var lokey = p == 0 ? Math.Max(0, pitch - 12) : (pitches[p - 1] + pitch + 1) / 2;
            var hikey = p == pitches.Count - 1 ? Math.Min(127, pitch + 12) : (pitch + pitches[p + 1]) / 2;
            var takes = analysis.Slices.Where(s => s.Pitch == pitch).ToList();
            for (var t = 0; t < takes.Count; t++)
            {
                var file = $"n{pitch:000}_{t + 1}.wav";
                WriteWav(Path.Combine(sampleFolder, file), samples, offset + takes[t].Start, takes[t].Length, rate, gain);
                sfz.Append(CultureInfo.InvariantCulture, $"<region> sample=samples\\{file} pitch_keycenter={pitch} lokey={lokey} hikey={hikey}");
                if (takes.Count > 1)
                {
                    sfz.Append(CultureInfo.InvariantCulture, $" seq_length={takes.Count} seq_position={t + 1}");
                }

                sfz.AppendLine();
            }
        }

        var sfzPath = Path.Combine(folder, folderName + ".sfz");
        File.WriteAllText(sfzPath, sfz.ToString());
        SoundPrograms.Invalidate();
        return SoundPrograms.Discover().FirstOrDefault(p => string.Equals(Path.GetFullPath(p.Path), Path.GetFullPath(sfzPath), StringComparison.OrdinalIgnoreCase))?.Name
            ?? $"{folderName}: Main (sampled)";
    }

    private static int TrimSilence(float[] samples, int from, int length, int rate)
    {
        var window = Math.Max(1, rate / 20);
        const float floor = 0.004f;
        var end = length;
        while (end > window)
        {
            var peak = 0f;
            for (var s = end - window; s < end; s++)
            {
                peak = Math.Max(peak, Math.Abs(samples[from + s]));
            }

            if (peak > floor)
            {
                break;
            }

            end -= window;
        }

        return Math.Min(length, end + window);
    }

    private static void WriteWav(string path, float[] samples, int from, int length, int rate, float gain)
    {
        var fadeIn = Math.Min(length / 2, rate / 500);
        var fadeOut = Math.Min(length / 2, rate / 25);
        var buffer = new float[length];
        for (var i = 0; i < length; i++)
        {
            var value = samples[from + i] * gain;
            if (i < fadeIn)
            {
                value *= (float)i / fadeIn;
            }

            if (i >= length - fadeOut)
            {
                value *= (float)(length - i) / fadeOut;
            }

            buffer[i] = value;
        }

        using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(rate, 1));
        writer.WriteSamples(buffer, 0, buffer.Length);
    }

    private static string SafeName(string name)
    {
        var cleaned = new string(name.Trim().Where(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '\'').ToArray()).Trim();
        return cleaned.Length == 0 ? "My instrument" : cleaned;
    }
}
