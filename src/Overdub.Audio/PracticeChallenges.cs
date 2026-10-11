using System.Text.Json;

namespace Overdub.Audio;

public enum ChallengeKind
{
    Roots,
    RootFifth,
    Octave,
    Approach,
    Walk,
    Free,
}

public sealed record ChallengeDef(string Id, ChallengeKind Kind, string Level, string Title, string Description);

public sealed record TargetNote(int Bar, long Start, long End, int Pitch, string Name, string Role, string Chord, bool ExactOctave);

public sealed record NoteResult(string Name, bool Hit, string State);

public sealed record ChallengeResult(int Hits, int Total, int Stars, double AverageOffsetMs, IReadOnlyList<NoteResult> Notes, string Tip, int Extra)
{
    public bool Passed => Stars >= 2;
}

public static class PracticeChallenges
{
    public const int Bars = 4;

    public static IReadOnlyList<ChallengeDef> All { get; } =
    [
        new("roots", ChallengeKind.Roots, "Level 1", "Roots on the beat", "On beats 1 and 3 of every bar, play the root of the chord."),
        new("root-fifth", ChallengeKind.RootFifth, "Level 2", "Root and fifth", "On beats 1 and 3 of every bar, play the root of the chord, then its fifth."),
        new("octave", ChallengeKind.Octave, "Level 3", "Add the octave", "On every beat, play the root, the fifth, the root an octave higher, then the fifth again."),
        new("approach", ChallengeKind.Approach, "Level 4", "Approach notes", "Play the root, fifth and root, then on beat 4 a note a half step below the next chord's root."),
        new("walk", ChallengeKind.Walk, "Level 5", "Walk the bar", "Play the same line your bass machine plays, note for note."),
        new("free", ChallengeKind.Free, "Any time", "Play your own line", "Make up your own line over the song. Overdub tells you which notes fit the key."),
    ];

    public static bool NeedsTargets(ChallengeDef def) => def.Kind != ChallengeKind.Free;

    public static int RootPitch(int root) => 28 + (((root - 4) % 12) + 12) % 12;

    public static int PassCount(int total) => (int)Math.Ceiling(total * 0.75);

    public static int StarsFor(int hits, int total)
    {
        if (total == 0)
        {
            return 0;
        }

        if (hits == total)
        {
            return 3;
        }

        return hits >= PassCount(total) ? 2 : hits >= (int)Math.Ceiling(total * 0.5) ? 1 : 0;
    }

    public static IReadOnlyList<TargetNote> BuildTargets(ChallengeDef def, IReadOnlyList<Chord> chords, long startSample, double samplesPerBeat, int beatsPerBar, bool flats, IReadOnlyList<MidiNoteData>? bassLine = null)
    {
        var targets = new List<TargetNote>();
        var barSamples = samplesPerBeat * beatsPerBar;
        if (def.Kind == ChallengeKind.Walk)
        {
            var end = startSample + (long)(barSamples * Bars);
            foreach (var n in (bassLine ?? []).Where(n => n.Velocity >= 50 && n.Start >= startSample - (long)(samplesPerBeat / 4) && n.Start < end).OrderBy(n => n.Start))
            {
                var bar = Math.Clamp((int)((n.Start - startSample) / barSamples), 0, Bars - 1);
                var chord = chords[bar % chords.Count];
                targets.Add(new TargetNote(bar, n.Start, n.End, n.Pitch, NoteSpelling.NameFor(n.Pitch % 12, flats), "line", ChordName(chord, flats), false));
            }

            return targets;
        }

        var second = Math.Max(1, beatsPerBar / 2);
        var beats = Math.Min(beatsPerBar, 4);
        for (var bar = 0; bar < Bars; bar++)
        {
            var chord = chords[bar % chords.Count];
            var next = chords[(bar + 1) % chords.Count];
            var root = RootPitch(chord.Root);
            var chordName = ChordName(chord, flats);
            (int Beat, int Pitch, string Role, bool Exact)[] plan = def.Kind switch
            {
                ChallengeKind.Roots => [(0, root, "root", false), (second, root, "root", false)],
                ChallengeKind.RootFifth => [(0, root, "root", false), (second, root + 7, "fifth", false)],
                ChallengeKind.Octave => Enumerable.Range(0, beats).Select(b => (b, b switch { 0 => root, 1 => root + 7, 2 => root + 12, _ => root + 7 }, b switch { 0 => "root", 1 => "fifth", 2 => "octave", _ => "fifth" }, b == 2)).ToArray(),
                _ => [(0, root, "root", false), (1, root + 7, "fifth", false), (2, root, "root", false), (Math.Min(3, beatsPerBar - 1), Approach(next), "approach", false)],
            };
            foreach (var (beat, pitch, role, exact) in plan)
            {
                var start = startSample + (long)(((bar * beatsPerBar) + beat) * samplesPerBeat);
                targets.Add(new TargetNote(bar, start, start + (long)(samplesPerBeat * 0.8), pitch, NoteSpelling.NameFor(pitch % 12, flats), role, chordName, exact));
            }
        }

        return targets;
    }

    private static string RolePhrase(string role) => role switch
    {
        "root" => "the root of the chord",
        "fifth" => "the fifth of the chord",
        "octave" => "the root an octave higher",
        "approach" => "an approach note, a half step below the next chord's root",
        _ => "a note of your bass machine's line",
    };

    private static int Approach(Chord next)
    {
        var target = RootPitch(next.Root);
        return target - 1 < 28 ? target + 11 : target - 1;
    }

    private static string ChordName(Chord chord, bool flats) => NoteSpelling.NameFor(chord.Root, flats) + chord.QualitySuffix;

    public static string BarSummary(IReadOnlyList<TargetNote> targets, int bar)
    {
        var names = targets.Where(t => t.Bar == bar).Select(t => t.Name).ToList();
        return names.Count switch
        {
            0 => "no notes",
            2 => $"{names[0]} then {names[1]}",
            > 6 => $"{names.Count} notes",
            _ => string.Join(", ", names),
        };
    }

    public static string Description(ChallengeDef def, int total) => def.Kind == ChallengeKind.Free
        ? def.Description
        : $"{def.Description} Hit at least {PassCount(total)} of the {total} notes to pass. The bass machine is muted so you can hear yourself.";

    public static ChallengeResult Score(IReadOnlyList<TargetNote> targets, IReadOnlyList<MidiNoteData> played, int sampleRate)
    {
        var midi = targets.Select(t => new MidiNoteData(t.Start, t.End, (byte)t.Pitch, 100)).ToList();
        var report = PlayingChecker.Compare(midi, played, sampleRate);
        var results = new List<NoteResult>();
        var offsets = new List<double>();
        var hits = 0;
        string? tip = null;
        for (var i = 0; i < targets.Count; i++)
        {
            var target = targets[i];
            var check = report.Notes.First(n => n.Target.Start == midi[i].Start && n.Target.Pitch == midi[i].Pitch);
            var hit = check.Outcome is NoteOutcome.OnTime or NoteOutcome.Early or NoteOutcome.Late || (check.Outcome == NoteOutcome.WrongOctave && !target.ExactOctave);
            if (hit)
            {
                hits++;
                if (check.Outcome != NoteOutcome.WrongOctave)
                {
                    offsets.Add(check.OffsetMs);
                }

                results.Add(new NoteResult(target.Name, true, check.Outcome == NoteOutcome.Late ? "late" : check.Outcome == NoteOutcome.Early ? "early" : "hit"));
                continue;
            }

            var state = check.Outcome switch
            {
                NoteOutcome.WrongNote => $"played {NoteSpelling.NameFor(check.PlayedPitch!.Value % 12, false)}",
                NoteOutcome.WrongOctave => "wrong octave",
                _ => "missed",
            };
            results.Add(new NoteResult(target.Name, false, state));
            tip ??= $"In bar {target.Bar + 1} ({target.Chord}) the {target.Name} is {RolePhrase(target.Role)}. Try it slowly, then try again.";
        }

        var missed = results.Count(r => r.State == "missed");
        if (targets.Count > 0 && missed > targets.Count / 2)
        {
            tip = "I did not hear many notes. Check the bass input level and play each note clearly, one at a time.";
        }

        tip ??= "Clean run. Try the next challenge.";
        return new ChallengeResult(hits, targets.Count, StarsFor(hits, targets.Count), offsets.Count == 0 ? 0 : offsets.Average(), results, tip, report.Extra);
    }

    public static ChallengeResult ScoreFree(IReadOnlyList<MidiNoteData> played, SongKey key)
    {
        var scale = Theory.Scale(key.Minor).Select(i => (key.Root + i) % 12).ToHashSet();
        var flats = NoteSpelling.KeyUsesFlats(key.Root, key.Minor);
        var notes = played.Take(16).Select(n => new NoteResult(NoteSpelling.NameFor(n.Pitch % 12, flats), scale.Contains(n.Pitch % 12), scale.Contains(n.Pitch % 12) ? "in key" : "outside")).ToList();
        var inKey = notes.Count(n => n.Hit);
        var stars = notes.Count < 4 ? 0 : StarsFor(inKey, notes.Count);
        var tip = notes.Count < 4
            ? "I heard fewer than four clear notes. Check the bass input level and play each note one at a time."
            : inKey == notes.Count
                ? $"Every note fits {key.Name}. Try adding a note outside the key on purpose and hear how it pulls."
                : $"{notes.Count - inKey} note{(notes.Count - inKey == 1 ? " falls" : "s fall")} outside {key.Name}. They are not wrong, but they sound tense unless you resolve them.";
        return new ChallengeResult(inKey, notes.Count, stars, 0, notes, tip, 0);
    }
}

public static class ChallengeProgress
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private static string FilePath => Path.Combine(AppPaths.SettingsDirectory, "challenges.json");

    public static int Best(string id) => Load().GetValueOrDefault(id, -1);

    public static void Record(string id, int stars)
    {
        var all = Load();
        if (all.GetValueOrDefault(id, -1) >= stars)
        {
            return;
        }

        all[id] = stars;
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(all, Json));
    }

    private static Dictionary<string, int> Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath)) ?? [] : [];
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return [];
        }
    }
}
