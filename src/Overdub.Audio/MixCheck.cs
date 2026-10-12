using NAudio.Wave;

namespace Overdub.Audio;

public enum MixLight
{
    Fine,
    Look,
    Fix,
}

public sealed record MixPart(string Name, MixRole Role, double Value);

public sealed record MixFinding(string Title, MixLight Light, string Label, string Value, string Text, string Short, MixStep? Step, double? Bar, IReadOnlyList<MixPart>? Parts);

public sealed record MixReport(IReadOnlyList<MixFinding> Findings, string Summary, string Mastering, double Seconds, int Bars)
{
    public int Fine => Findings.Count(f => f.Light == MixLight.Fine);
}

public static class MixCheck
{
    public const double StreamingLufs = -14;

    public static MixReport Run(Session session)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var folder = Path.Combine(Path.GetTempPath(), "overdub-mixcheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var mixPath = Path.Combine(folder, "mix.wav");
            session.ExportMixdown(mixPath);
            var loudness = session.LastLoudness ?? Mastering.Measure(mixPath);
            var (left, right, rate) = Read(mixPath);
            var rmsL = Rms(left);
            var rmsR = Rms(right);

            var players = MeasurePlayers(session, Path.Combine(folder, "stems"));
            var barSamples = session.Engine.SamplesPerBeat * session.Engine.BeatsPerBar;
            var bars = (int)Math.Round(session.LengthSamples / Math.Max(1.0, barSamples));
            var preset = Mastering.Presets[Math.Clamp(session.MasterIndex, 0, Mastering.Presets.Count - 1)].Name;
            var findings = new List<MixFinding>
            {
                Peaks(loudness),
                Loudness(loudness, preset),
                LowEnd(players),
                Balance(rmsL, rmsR),
                Loudest(players),
            };
            return new MixReport(findings, Summary(findings), preset, clock.Elapsed.TotalSeconds, bars);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
            }
        }
    }

    public static List<(Track Track, MixRole Role, double Lufs, double Low)> MeasurePlayers(Session session, string stemsFolder)
    {
        var audible = session.Tracks
            .Where(t => t.Clips.Count > 0 || t.MidiClips.Count > 0)
            .Where(t => !t.Mute && (!session.Tracks.Any(o => o.Solo) || t.Solo))
            .ToList();
        var written = session.ExportStems(stemsFolder, respectClipMutes: true);
        var all = session.Tracks.Where(t => t.Clips.Count > 0 || t.MidiClips.Count > 0).ToList();
        var players = new List<(Track Track, MixRole Role, double Lufs, double Low)>();
        for (var i = 0; i < Math.Min(all.Count, written.Count); i++)
        {
            if (!audible.Contains(all[i]))
            {
                continue;
            }

            var (sl, sr, srate) = Read(written[i]);
            var lufs = LoudnessMeter.Integrated(sl, sr, srate) ?? double.NegativeInfinity;
            if (double.IsNegativeInfinity(lufs))
            {
                continue;
            }

            players.Add((all[i], MixingGuide.RoleOf(all[i]), lufs, LowEnergy(sl, sr, srate)));
        }

        return players;
    }

    public static List<(Track Track, MixRole Role, double Lufs, double Low)> MeasureInTemp(Session session)
    {
        var folder = Path.Combine(Path.GetTempPath(), "overdub-levels-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            return MeasurePlayers(session, folder);
        }
        finally
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static MixFinding Peaks(LoudnessReport loudness)
    {
        var peak = loudness.PeakDb;
        var value = $"loudest peak {peak:0.0} dB";
        if (peak >= -0.1)
        {
            return new MixFinding("Peaks", MixLight.Fix, "worth fixing", value, "The mix reaches the top, so parts of it may crackle. Turn the loudest players down.", "the mix reaches the top and may crackle", MixStep.Levels, null, null);
        }

        if (peak > -0.9)
        {
            return new MixFinding("Peaks", MixLight.Look, "a look", value, "The peaks are very close to the top. A little more room would be safer.", "the peaks are very close to the top", MixStep.Levels, null, null);
        }

        return new MixFinding("Peaks", MixLight.Fine, "fine", value, "Nothing clips, and you have a little room left. Aim to keep peaks below 0 dB.", "peaks are fine", null, null, null);
    }

    private static MixFinding Loudness(LoudnessReport loudness, string preset)
    {
        var lufs = loudness.Lufs;
        var bar = double.IsNegativeInfinity(lufs) ? 0 : Math.Clamp((lufs + 30) / 24, 0, 1);
        var measured = $" Measured with the {preset} mastering preset.";
        if (double.IsNegativeInfinity(lufs))
        {
            return new MixFinding("Loudness", MixLight.Fix, "silent", "nothing to measure", "I could not hear anything in the mix. Check that the players are not muted." + measured, "nothing is audible", MixStep.Levels, 0, null);
        }

        var diff = lufs - StreamingLufs;
        var value = $"{lufs:0} LUFS";
        if (Math.Abs(diff) <= 2)
        {
            return new MixFinding("Loudness", MixLight.Fine, "about right", value, "That is about the level streaming services use, so your song will sit well next to others." + measured, "loudness is about right", null, bar, null);
        }

        if (diff < 0)
        {
            return new MixFinding("Loudness", diff < -8 ? MixLight.Fix : MixLight.Look, diff < -8 ? "quiet" : "a bit quiet", value, "Streaming services play songs at about -14 LUFS, so yours will sound softer than others. Pick the Streaming preset in the Finish step." + measured, "the mix is quiet", MixStep.Finish, bar, null);
        }

        return new MixFinding("Loudness", MixLight.Look, "a bit loud", value, "Louder than about -14 LUFS gains nothing on streaming services, which turn it down, and it leaves less room for the music to breathe. The Streaming preset sets it for you." + measured, "the mix is loud", MixStep.Finish, bar, null);
    }

    private static MixFinding LowEnd(List<(Track Track, MixRole Role, double Lufs, double Low)> players)
    {
        var bass = players.Where(p => p.Role is MixRole.Bass or MixRole.AudioBass).OrderByDescending(p => p.Low).FirstOrDefault();
        if (bass.Track is null)
        {
            return new MixFinding("Low end", MixLight.Fine, "fine", "no bass in the mix", "There is no bass player to compete with, so nothing covers the low end.", "the low end is fine", null, null, null);
        }

        var total = Math.Max(1e-12, players.Sum(p => p.Low));
        var parts = players.Where(p => p.Low / total >= 0.03).OrderByDescending(p => p.Low).Select(p => new MixPart(p.Track.Name, p.Role, p.Low / total)).ToList();
        var rival = players.Where(p => p.Role is not (MixRole.Bass or MixRole.AudioBass)).OrderByDescending(p => p.Low).FirstOrDefault();
        var allowance = rival.Role == MixRole.Drums ? 4.0 : 1.5;
        if (rival.Track is not null && rival.Low > bass.Low * allowance)
        {
            var db = 10 * Math.Log10(rival.Low / Math.Max(1e-12, bass.Low));
            var fix = rival.Role == MixRole.Drums ? "Turn the drums down a little, or the bass up, in the Levels step." : "Make room in the EQ step.";
            return new MixFinding("Low end", MixLight.Fix, "worth fixing", $"{rival.Track.Name} louder than the bass below 200 Hz", $"The bass should own the low end. Here {rival.Track.Name} has about {db:0} dB more energy there, so the bass sounds thin and muddy. {fix}", $"{rival.Track.Name} is covering the bass", rival.Role == MixRole.Drums ? MixStep.Levels : MixStep.Room, null, parts);
        }

        return new MixFinding("Low end", MixLight.Fine, "fine", "the bass owns the low end", "The bass has the most energy below 200 Hz, so the low end sounds full and clear.", "the low end is fine", null, null, parts);
    }

    private static MixFinding Balance(double rmsL, double rmsR)
    {
        var diff = 20 * Math.Log10(Math.Max(1e-9, rmsL) / Math.Max(1e-9, rmsR));
        var side = diff > 0 ? "left" : "right";
        var size = Math.Abs(diff);
        if (size <= 1.5)
        {
            return new MixFinding("Left and right", MixLight.Fine, "fine", $"balanced within {Math.Max(size, 0.1):0.#} dB", "Both speakers play about the same amount, so the song does not lean to one side.", "left and right are balanced", null, null, null);
        }

        return new MixFinding("Left and right", size <= 3 ? MixLight.Look : MixLight.Fix, size <= 3 ? "a look" : "worth fixing", $"leans {side} by {size:0.#} dB", $"The {side} speaker plays more than the other. Move a player towards the middle, or to the other side.", $"the song leans {side}", MixStep.Pan, null, null);
    }

    private static MixFinding Loudest(List<(Track Track, MixRole Role, double Lufs, double Low)> players)
    {
        if (players.Count < 2)
        {
            return new MixFinding("Who is loudest", MixLight.Fine, "fine", "only one player", "There is only one player to compare, so there is nothing to balance.", "levels are fine", null, null, null);
        }

        var ordered = players.OrderByDescending(p => p.Lufs).ToList();
        var top = ordered[0].Lufs;
        var parts = ordered.Select(p => new MixPart(p.Track.Name, p.Role, Math.Clamp(1 - ((top - p.Lufs) / 24), 0, 1))).ToList();
        var rhythm = players.Where(p => p.Role is MixRole.Drums or MixRole.Bass or MixRole.AudioBass).OrderByDescending(p => p.Lufs).FirstOrDefault();
        var leader = ordered[0];
        if (leader.Role is MixRole.Drums or MixRole.Bass or MixRole.AudioBass || rhythm.Track is null || leader.Lufs - rhythm.Lufs < 2)
        {
            return new MixFinding("Who is loudest", MixLight.Fine, "fine", $"{leader.Track.Name} is the loudest player", "Drums and bass lead, or are close to the front, which is how most mixes work.", "the levels are fine", null, null, parts);
        }

        var gap = leader.Lufs - rhythm.Lufs;
        return new MixFinding("Who is loudest", MixLight.Look, "a look", $"{leader.Track.Name} is the loudest player", $"Drums and bass usually lead. Here {leader.Track.Name} is above {rhythm.Track.Name} by about {gap:0} dB.", $"{leader.Track.Name} is louder than the rhythm section", MixStep.Levels, null, parts);
    }

    private static string Summary(IReadOnlyList<MixFinding> findings)
    {
        var fine = findings.Count(f => f.Light == MixLight.Fine);
        var look = findings.Count(f => f.Light == MixLight.Look);
        var fix = findings.Count(f => f.Light == MixLight.Fix);
        if (fine == findings.Count)
        {
            return "Your mix is in good shape. Everything I measured is fine.";
        }

        var head = fix == 0 ? "Your mix is close." : "Your mix needs some work.";
        var counts = $" {fine} {(fine == 1 ? "thing is" : "things are")} fine, {look} {(look == 1 ? "needs" : "need")} a look, and {fix} {(fix == 1 ? "is" : "are")} worth fixing.";
        var first = findings.FirstOrDefault(f => f.Light == MixLight.Fix) ?? findings.First(f => f.Light == MixLight.Look);
        return $"{head}{counts} Start with this: {first.Short}.";
    }

    private static (float[] Left, float[] Right, int Rate) Read(string path)
    {
        using var reader = new AudioFileReader(path);
        var channels = reader.WaveFormat.Channels;
        var frames = (int)(reader.Length / 4 / channels);
        var data = new float[frames * channels];
        var read = 0;
        ISampleProvider provider = reader;
        while (read < data.Length)
        {
            var n = provider.Read(data.AsSpan(read));
            if (n <= 0)
            {
                break;
            }

            read += n;
        }

        var left = new float[frames];
        var right = new float[frames];
        for (var i = 0; i < frames && (i * channels) + channels - 1 < read; i++)
        {
            left[i] = data[i * channels];
            right[i] = channels > 1 ? data[(i * channels) + 1] : data[i * channels];
        }

        return (left, right, reader.WaveFormat.SampleRate);
    }

    private static double Rms(float[] x)
    {
        double sum = 0;
        foreach (var v in x)
        {
            sum += v * v;
        }

        return Math.Sqrt(sum / Math.Max(1, x.Length));
    }

    private static double LowEnergy(float[] left, float[] right, int rate)
    {
        var w0 = 2 * Math.PI * 200 / rate;
        var cos = Math.Cos(w0);
        var alpha = Math.Sin(w0) / (2 * 0.7071);
        var a0 = 1 + alpha;
        var b0 = ((1 - cos) / 2) / a0;
        var b1 = (1 - cos) / a0;
        var a1 = (-2 * cos) / a0;
        var a2 = (1 - alpha) / a0;
        double x1 = 0, x2 = 0, y1 = 0, y2 = 0, energy = 0;
        for (var i = 0; i < left.Length; i++)
        {
            var x = 0.5 * (left[i] + right[i]);
            var y = (b0 * x) + (b1 * x1) + (b0 * x2) - (a1 * y1) - (a2 * y2);
            x2 = x1;
            x1 = x;
            y2 = y1;
            y1 = y;
            energy += y * y;
        }

        return energy;
    }
}
