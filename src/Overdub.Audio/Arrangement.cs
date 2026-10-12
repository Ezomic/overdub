namespace Overdub.Audio;

public enum ArrangeIdea
{
    StartSmall,
    GrowIntoChorus,
    PullBack,
    MarkChanges,
    EndSoftly,
}

public sealed record ArrangeCell(IReadOnlyList<MidiClip> Clips)
{
    public bool Empty => Clips.Count == 0;

    public bool Active => Clips.Any(c => !c.Mute);
}

public sealed record ArrangePlayer(Track Track, MixRole Role, string Name);

public sealed class ArrangeGrid(IReadOnlyList<SongSection> sections, IReadOnlyList<ArrangePlayer> players, ArrangeCell[,] cells)
{
    public IReadOnlyList<SongSection> Sections { get; } = sections;
    public IReadOnlyList<ArrangePlayer> Players { get; } = players;

    public ArrangeCell this[int player, int section] => cells[player, section];

    public int Energy(int section) => Enumerable.Range(0, Players.Count).Count(p => !cells[p, section].Empty && cells[p, section].Active);

    public SectionKind Kind(int section) => SongStructure.KindOf(Sections[section].Name);
}

public static class Arrangement
{
    public static readonly string[] IdeaNames = ["Start small", "Grow into the chorus", "Pull back for the bridge", "Mark the changes", "End softly"];

    public static readonly string[] ApplyLabels = ["Start my song small", "Grow into the choruses", "Pull back for the bridge", "Mark the changes", "End my song softly"];

    public static readonly string[] Explanations =
    [
        "An intro that is already full has nowhere to go. Let a couple of instruments open the song, then bring the others in one by one. By the time the whole band plays, the listener feels the lift.",
        "The chorus is the payoff of the song, so it should sound bigger than the verse. Keep the verse lighter and let everyone play in the chorus, so the chorus feels like a lift every time it comes back.",
        "A bridge is a change of scene. Taking the drums and the lead out for a few bars makes the music feel suddenly open, and the return of the full band in the last chorus hits harder.",
        "Where one section turns into the next, a drum fill tells the listener that something is about to change. A fill at every section boundary makes the whole song feel planned.",
        "Songs that stop at full volume feel cut off. Let the drums drop out and keep the quieter instruments, so the ending feels like a landing.",
    ];

    public static readonly string[] Changes =
    [
        "In the intro keep only the guitar and the keys. In the first verse bring in the drums and the bass, and leave the lead for the chorus.",
        "In the verses let the lead and the keys sit out, and in the choruses bring everyone in.",
        "In the bridge and any breakdown, take out the drums and the lead.",
        "Add a drum fill at the end of each section, using the fills Overdub already knows how to place.",
        "In the outro let the drums and the lead drop out and keep the bass, guitar and keys.",
    ];

    public static ArrangeGrid Build(IEnumerable<Track> tracks, IEnumerable<SongSection> sections)
    {
        var ordered = sections.OrderBy(s => s.Start).ToList();
        var players = tracks
            .Where(t => t.IsDrums || t.Machine is not null || t.IsKeys)
            .Where(t => t.MidiClips.Any(c => ordered.Any(s => c.StartSample >= s.Start && c.StartSample < s.Start + s.Length)))
            .OrderBy(t => (int)MixingGuide.RoleOf(t))
            .Select(t => new ArrangePlayer(t, MixingGuide.RoleOf(t), MixingGuide.RoleName(MixingGuide.RoleOf(t))))
            .ToList();
        var cells = new ArrangeCell[players.Count, ordered.Count];
        for (var p = 0; p < players.Count; p++)
        {
            for (var s = 0; s < ordered.Count; s++)
            {
                var section = ordered[s];
                cells[p, s] = new ArrangeCell(players[p].Track.MidiClips.Where(c => c.StartSample >= section.Start && c.StartSample < section.Start + section.Length).ToList());
            }
        }

        return new ArrangeGrid(ordered, players, cells);
    }

    public static IReadOnlyList<(int Player, int Section, bool Active)> Plan(ArrangeGrid grid, ArrangeIdea idea)
    {
        var changes = new List<(int, int, bool)>();
        var firstIsIntro = grid.Sections.Count > 0 && grid.Kind(0) == SectionKind.Intro;
        var firstVerse = Enumerable.Range(0, grid.Sections.Count).FirstOrDefault(s => grid.Kind(s) == SectionKind.Verse, -1);
        for (var p = 0; p < grid.Players.Count; p++)
        {
            var role = grid.Players[p].Role;
            for (var s = 0; s < grid.Sections.Count; s++)
            {
                if (grid[p, s].Empty)
                {
                    continue;
                }

                var kind = grid.Kind(s);
                bool? want = idea switch
                {
                    ArrangeIdea.StartSmall => StartSmall(role, kind, s, firstIsIntro, firstVerse),
                    ArrangeIdea.GrowIntoChorus => kind switch
                    {
                        SectionKind.Chorus or SectionKind.Drop => true,
                        SectionKind.Verse => role is not (MixRole.Lead or MixRole.Keys),
                        _ => null,
                    },
                    ArrangeIdea.PullBack => kind is SectionKind.Bridge or SectionKind.Breakdown ? role is not (MixRole.Drums or MixRole.Lead) : null,
                    ArrangeIdea.EndSoftly => kind == SectionKind.Outro ? role is not (MixRole.Drums or MixRole.Lead) : null,
                    _ => null,
                };
                if (want is { } active && grid[p, s].Active != active)
                {
                    changes.Add((p, s, active));
                }
            }
        }

        return changes;
    }

    private static bool? StartSmall(MixRole role, SectionKind kind, int section, bool firstIsIntro, int firstVerse)
    {
        if (firstIsIntro && section == 0)
        {
            return role is MixRole.Guitar or MixRole.Keys;
        }

        if (section == firstVerse)
        {
            return role is not MixRole.Lead && (firstIsIntro || role is not MixRole.Keys);
        }

        return null;
    }

    public static string Verdict(ArrangeGrid grid)
    {
        if (grid.Sections.Count == 0)
        {
            return "";
        }

        var energy = Enumerable.Range(0, grid.Sections.Count).Select(grid.Energy).ToList();
        if (energy.Distinct().Count() == 1)
        {
            return "Flat: every section is just as full, so nothing feels like a climax.";
        }

        var choruses = Enumerable.Range(0, grid.Sections.Count).Where(s => grid.Kind(s) is SectionKind.Chorus or SectionKind.Drop).Select(s => energy[s]).ToList();
        var verses = Enumerable.Range(0, grid.Sections.Count).Where(s => grid.Kind(s) == SectionKind.Verse).Select(s => energy[s]).ToList();
        if (choruses.Count > 0 && verses.Count > 0)
        {
            return choruses.Average() > verses.Average()
                ? "Good: the choruses are fuller than the verses, so the song lifts every time the chorus arrives."
                : "The choruses are no fuller than the verses yet, so they do not lift the song.";
        }

        return "The sections have different amounts of players, so the song has some movement.";
    }
}
