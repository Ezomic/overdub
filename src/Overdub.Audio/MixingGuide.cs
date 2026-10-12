namespace Overdub.Audio;

public enum MixStep
{
    Levels,
    Pan,
    Room,
    Compress,
    Space,
    Finish,
}

public enum MixRole
{
    Drums,
    Bass,
    Guitar,
    Lead,
    Keys,
    AudioBass,
    AudioGuitar,
    Other,
}

public sealed record EffectState(bool Enabled, double[] Values);

public sealed record MixState(float Gain, float Pan, float Reverb, float Delay, IReadOnlyList<EffectState> Effects)
{
    public static MixState Capture(Track track) => new(
        track.Gain,
        track.Pan,
        track.Sends.Reverb,
        track.Sends.Delay,
        track.Effects.Effects.Select(e => new EffectState(e.Enabled, (double[])e.Values.Clone())).ToList());

    public void Restore(Track track)
    {
        track.Gain = Gain;
        track.Pan = Pan;
        track.Sends.Reverb = Reverb;
        track.Sends.Delay = Delay;
        for (var i = 0; i < Math.Min(Effects.Count, track.Effects.Effects.Count); i++)
        {
            var effect = track.Effects.Effects[i];
            effect.Enabled = Effects[i].Enabled;
            for (var v = 0; v < Math.Min(Effects[i].Values.Length, effect.Values.Length); v++)
            {
                effect.Set(v, Effects[i].Values[v]);
            }
        }

        track.Effects.Touch();
    }
}

public sealed record MixSuggestion(Track Track, MixRole Role, string Summary, double Current, double Target, bool CanApply, string? Note);

public sealed class MixPlan(MixStep step, IReadOnlyList<MixSuggestion> suggestions, IReadOnlyDictionary<Track, MixState> before, IReadOnlyDictionary<Track, MixState> after)
{
    public MixStep Step { get; } = step;
    public IReadOnlyList<MixSuggestion> Suggestions { get; } = suggestions;
    public IReadOnlyDictionary<Track, MixState> Before { get; } = before;
    public IReadOnlyDictionary<Track, MixState> After { get; } = after;
    public bool Applied { get; set; }
    public bool ShowingBefore { get; set; }
    public bool HasChanges => Suggestions.Any(s => s.CanApply && Math.Abs(s.Target - s.Current) > 0.0001);
}

public static class MixingGuide
{
    public static readonly string[] StepNames = ["Levels", "Pan", "Make room (EQ)", "Even it out (compression)", "Space (reverb, delay)", "Finish (mastering)"];

    public static MixRole RoleOf(Track track)
    {
        if (track.IsDrums)
        {
            return MixRole.Drums;
        }

        if (track.Machine is { } machine)
        {
            return machine switch { MachineRole.Bass => MixRole.Bass, MachineRole.Lead => MixRole.Lead, _ => MixRole.Guitar };
        }

        if (track.IsKeys)
        {
            return MixRole.Keys;
        }

        if (track.IsBacking)
        {
            return MixRole.Other;
        }

        var name = track.Name.ToLowerInvariant();
        return name.Contains("bass") ? MixRole.AudioBass : name.Contains("guitar") ? MixRole.AudioGuitar : MixRole.Other;
    }

    public static string RoleName(MixRole role) => role switch
    {
        MixRole.Drums => "Drums",
        MixRole.Bass => "Bass machine",
        MixRole.Guitar => "Guitar machine",
        MixRole.Lead => "Lead machine",
        MixRole.Keys => "Keys",
        MixRole.AudioBass => "Your bass",
        MixRole.AudioGuitar => "Your guitar",
        _ => "Other",
    };

    private static bool Present(Track track) => track.IsDrums || track.Machine is not null || track.IsKeys || track.Clips.Count > 0;

    public static IReadOnlyList<Track> Players(IEnumerable<Track> tracks) => tracks
        .Where(Present)
        .OrderBy(t => (int)RoleOf(t))
        .ToList();

    public static double OffsetFromDrumsDb(MixRole role) => role switch
    {
        MixRole.Drums => 0,
        MixRole.Bass or MixRole.AudioBass => -1,
        MixRole.AudioGuitar => -5,
        MixRole.Guitar => -6,
        MixRole.Lead => -6,
        MixRole.Keys => -8,
        _ => -6,
    };

    public static MixPlan PlanLevelsMeasured(IEnumerable<Track> tracks, IReadOnlyList<(Track Track, MixRole Role, double Lufs, double Low)> measured)
    {
        var players = Players(tracks);
        var anchor = measured.Where(m => m.Role == MixRole.Drums).Select(m => (double?)m.Lufs).FirstOrDefault()
            ?? measured.Where(m => m.Role is MixRole.Bass or MixRole.AudioBass).Select(m => (double?)m.Lufs).FirstOrDefault()
            ?? measured.Select(m => (double?)m.Lufs).DefaultIfEmpty(null).Max();
        var suggestions = new List<MixSuggestion>();
        var before = new Dictionary<Track, MixState>();
        var raw = new Dictionary<Track, double>();
        foreach (var track in players)
        {
            var role = RoleOf(track);
            before[track] = MixState.Capture(track);
            var found = measured.FirstOrDefault(m => ReferenceEquals(m.Track, track));
            if (found.Track is not null && anchor is { } loudness)
            {
                raw[track] = track.Gain * Math.Pow(10, (loudness + OffsetFromDrumsDb(role) - found.Lufs) / 20);
            }
            else
            {
                raw[track] = LevelFor(role);
            }
        }

        var scale = raw.Count == 0 ? 1 : Scale(raw, measured);
        var after = new Dictionary<Track, MixState>();
        foreach (var track in players)
        {
            var target = Math.Clamp(raw[track] * scale, 0.02, 1.5);
            suggestions.Add(new MixSuggestion(track, RoleOf(track), track.Name, track.Gain, target, true, null));
            after[track] = before[track] with { Gain = (float)target };
        }

        return new MixPlan(MixStep.Levels, suggestions, before, after);
    }

    private const double TargetMixLufs = -18;
    private const double TopGain = 1.2;

    private static double Scale(Dictionary<Track, double> raw, IReadOnlyList<(Track Track, MixRole Role, double Lufs, double Low)> measured)
    {
        var energy = 0.0;
        foreach (var m in measured)
        {
            if (!raw.TryGetValue(m.Track, out var gain) || m.Track.Gain < 0.0001)
            {
                continue;
            }

            energy += Math.Pow(10, (m.Lufs + (20 * Math.Log10(gain / m.Track.Gain))) / 10);
        }

        var wanted = energy <= 0 ? 1 : Math.Pow(10, (TargetMixLufs - (10 * Math.Log10(energy))) / 20);
        return Math.Min(wanted, TopGain / Math.Max(1e-6, raw.Values.Max()));
    }

    public static double LevelFor(MixRole role) => role switch
    {
        MixRole.Drums => 0.85,
        MixRole.Bass => 0.85,
        MixRole.AudioBass => 0.9,
        MixRole.Guitar => 0.6,
        MixRole.AudioGuitar => 0.8,
        MixRole.Lead => 0.55,
        MixRole.Keys => 0.5,
        _ => 0.7,
    };

    public static double PanFor(MixRole role) => role switch
    {
        MixRole.Guitar => -0.3,
        MixRole.AudioGuitar => -0.25,
        MixRole.Lead => 0.15,
        MixRole.Keys => 0.3,
        _ => 0,
    };

    public static double? LowCutFor(MixRole role) => role switch
    {
        MixRole.Guitar or MixRole.AudioGuitar => -4,
        MixRole.Lead => -3,
        _ => null,
    };

    public static double[]? CompressorFor(MixRole role) => role switch
    {
        MixRole.Bass or MixRole.AudioBass => [-18, 4, 10, 150, 3],
        MixRole.Guitar or MixRole.AudioGuitar or MixRole.Lead => [-20, 2, 15, 200, 2],
        _ => null,
    };

    public static (double Reverb, double Delay) SendsFor(MixRole role) => role switch
    {
        MixRole.Guitar or MixRole.AudioGuitar => (0.12, 0),
        MixRole.Lead => (0.2, 0.1),
        MixRole.Keys => (0.15, 0),
        MixRole.Drums => (0.05, 0),
        _ => (0, 0),
    };

    public static bool HasEffects(MixRole role) => role is not (MixRole.Keys or MixRole.Drums);

    public static MixPlan Plan(MixStep step, IEnumerable<Track> tracks)
    {
        var players = Players(tracks);
        var suggestions = new List<MixSuggestion>();
        var before = new Dictionary<Track, MixState>();
        var after = new Dictionary<Track, MixState>();
        foreach (var track in players)
        {
            var role = RoleOf(track);
            var state = MixState.Capture(track);
            before[track] = state;
            var suggestion = Suggest(step, track, role);
            suggestions.Add(suggestion);
            if (suggestion.CanApply)
            {
                Change(step, track, role);
                after[track] = MixState.Capture(track);
                state.Restore(track);
            }
            else
            {
                after[track] = state;
            }
        }

        return new MixPlan(step, suggestions, before, after);
    }

    private static MixSuggestion Suggest(MixStep step, Track track, MixRole role)
    {
        var name = track.Name;
        switch (step)
        {
            case MixStep.Levels:
                return new MixSuggestion(track, role, name, track.Gain, LevelFor(role), true, null);
            case MixStep.Pan:
                return new MixSuggestion(track, role, name, track.Pan, PanFor(role), true, null);
            case MixStep.Room:
                if (LowCutFor(role) is { } cut && HasEffects(role))
                {
                    var eq = track.Effects.Effects.OfType<Equalizer>().First();
                    return new MixSuggestion(track, role, $"Turn the low end down by {-cut:0} dB", eq.Enabled ? eq.Values[0] : 0, cut, true, null);
                }

                return new MixSuggestion(track, role, name, 0, 0, false, role == MixRole.Keys ? "Keys have no EQ yet. Turn them down in the Levels step to leave room instead." : role is MixRole.Bass or MixRole.AudioBass ? "Leave the bass as it is: the others are making room for it." : role == MixRole.Drums ? "Drums have their own range, so they stay as they are." : "Nothing to change.");
            case MixStep.Compress:
                if (CompressorFor(role) is { } values && HasEffects(role))
                {
                    var comp = track.Effects.Effects.OfType<Compressor>().First();
                    return new MixSuggestion(track, role, $"Threshold {values[0]:0} dB, ratio {values[1]:0}:1", comp.Enabled ? comp.Values[1] : 1, values[1], true, null);
                }

                return new MixSuggestion(track, role, name, 0, 0, false, role == MixRole.Keys ? "Keys have no compressor yet." : "Nothing to change.");
            case MixStep.Space:
                var (reverb, delay) = SendsFor(role);
                var parts = new List<string>();
                if (reverb > 0)
                {
                    parts.Add($"reverb {reverb * 100:0}%");
                }

                if (delay > 0)
                {
                    parts.Add($"delay {delay * 100:0}%");
                }

                return new MixSuggestion(track, role, parts.Count == 0 ? "Stays dry" : string.Join(", ", parts), track.Sends.Reverb, reverb, true, parts.Count == 0 ? "Keep the bass dry so the low end stays tight." : null);
            default:
                return new MixSuggestion(track, role, name, 0, 0, false, null);
        }
    }

    private static void Change(MixStep step, Track track, MixRole role)
    {
        switch (step)
        {
            case MixStep.Levels:
                track.Gain = (float)LevelFor(role);
                break;
            case MixStep.Pan:
                track.Pan = (float)PanFor(role);
                break;
            case MixStep.Room:
                var eq = track.Effects.Effects.OfType<Equalizer>().First();
                eq.Set(0, LowCutFor(role)!.Value);
                eq.Enabled = true;
                track.Effects.Touch();
                break;
            case MixStep.Compress:
                var comp = track.Effects.Effects.OfType<Compressor>().First();
                var values = CompressorFor(role)!;
                for (var i = 0; i < values.Length; i++)
                {
                    comp.Set(i, values[i]);
                }

                comp.Enabled = true;
                track.Effects.Touch();
                break;
            case MixStep.Space:
                var (reverb, delay) = SendsFor(role);
                track.Sends.Reverb = (float)reverb;
                track.Sends.Delay = (float)delay;
                break;
        }
    }
}
