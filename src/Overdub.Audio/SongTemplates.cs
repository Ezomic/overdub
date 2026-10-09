namespace Overdub.Audio;

public sealed record SectionPlan(string Name, int Bars, int Progression, string DrumPreset, ChordStyle Guitar, ChordStyle Bass, bool Fill = true);

public sealed record SongTemplate(string Name, string Description, double Bpm, int KeyRoot, IReadOnlyList<IReadOnlyList<(int Offset, ChordQuality Quality)>> Progressions, IReadOnlyList<SectionPlan> Sections)
{
    public int TotalBars => Sections.Sum(s => s.Bars);
}

public static class SongTemplates
{
    private const ChordQuality Maj = ChordQuality.Major;
    private const ChordQuality Min = ChordQuality.Minor;
    private const ChordQuality Dom = ChordQuality.Seventh;

    public static IReadOnlyList<SongTemplate> All { get; } =
    [
        new(
            "Pop song",
            "Intro, verse, chorus, verse, chorus, bridge, last chorus, outro in C at 104 BPM. Verses use vi IV I V, choruses I V vi IV.",
            104,
            0,
            [
                [(9, Min), (5, Maj), (0, Maj), (7, Maj)],
                [(0, Maj), (7, Maj), (9, Min), (5, Maj)],
                [(5, Maj), (7, Maj), (4, Min), (9, Min)],
            ],
            [
                new("Intro", 4, 1, "Half-time", ChordStyle.Pad, ChordStyle.WholeNotes, false),
                new("Verse", 8, 0, "Pop", ChordStyle.Strum, ChordStyle.RootPulse),
                new("Chorus", 8, 1, "Rock", ChordStyle.FolkStrum, ChordStyle.Rock8ths),
                new("Verse 2", 8, 0, "Pop", ChordStyle.Strum, ChordStyle.RootAndFifth),
                new("Chorus 2", 8, 1, "Rock", ChordStyle.FolkStrum, ChordStyle.Rock8ths),
                new("Bridge", 4, 2, "Half-time", ChordStyle.Arpeggio, ChordStyle.WholeNotes),
                new("Last chorus", 8, 1, "Rock", ChordStyle.FolkStrum, ChordStyle.Motown),
                new("Outro", 4, 1, "Half-time", ChordStyle.Pad, ChordStyle.WholeNotes, false),
            ]),
        new(
            "Rock song",
            "Verse, chorus, verse, chorus, solo, chorus in E minor at 126 BPM, with palm-mutable strums and driving eighth-note bass.",
            126,
            4,
            [
                [(0, Min), (8, Maj), (3, Maj), (10, Maj)],
                [(8, Maj), (3, Maj), (10, Maj), (0, Min)],
            ],
            [
                new("Verse", 8, 0, "Rock", ChordStyle.Strum, ChordStyle.Rock8ths),
                new("Chorus", 8, 1, "Rock", ChordStyle.FolkStrum, ChordStyle.Octaves),
                new("Verse 2", 8, 0, "Rock", ChordStyle.Strum, ChordStyle.Rock8ths),
                new("Chorus 2", 8, 1, "Rock", ChordStyle.FolkStrum, ChordStyle.Octaves),
                new("Solo", 8, 0, "Rock", ChordStyle.Pad, ChordStyle.Rock8ths),
                new("Last chorus", 8, 1, "Metal", ChordStyle.FolkStrum, ChordStyle.Octaves),
            ]),
        new(
            "12-bar blues",
            "Three rounds of the classic twelve bars in A at 96 BPM, with a walking bass.",
            96,
            9,
            [
                [(0, Dom), (0, Dom), (0, Dom), (0, Dom)],
                [(5, Dom), (5, Dom), (0, Dom), (0, Dom)],
                [(7, Dom), (5, Dom), (0, Dom), (7, Dom)],
            ],
            [
                new("First round", 4, 0, "Reggae", ChordStyle.Strum, ChordStyle.Walking, false),
                new("First round, IV", 4, 1, "Reggae", ChordStyle.Strum, ChordStyle.Walking, false),
                new("First round, turnaround", 4, 2, "Reggae", ChordStyle.Strum, ChordStyle.WalkingApproach),
                new("Second round", 4, 0, "Pop", ChordStyle.FolkStrum, ChordStyle.Walking, false),
                new("Second round, IV", 4, 1, "Pop", ChordStyle.FolkStrum, ChordStyle.Walking, false),
                new("Second round, turnaround", 4, 2, "Pop", ChordStyle.FolkStrum, ChordStyle.WalkingApproach),
            ]),
        new(
            "Slow ballad",
            "Intro, verse, chorus, verse, chorus, outro in G at 72 BPM, with arpeggios and long bass notes.",
            72,
            7,
            [
                [(0, Maj), (9, Min), (5, Maj), (7, Maj)],
                [(5, Maj), (0, Maj), (7, Maj), (9, Min)],
            ],
            [
                new("Intro", 4, 0, "Half-time", ChordStyle.Arpeggio, ChordStyle.WholeNotes, false),
                new("Verse", 8, 0, "Half-time", ChordStyle.Arpeggio, ChordStyle.WholeNotes),
                new("Chorus", 8, 1, "Pop", ChordStyle.Strum, ChordStyle.RootAndFifth),
                new("Verse 2", 8, 0, "Half-time", ChordStyle.Arpeggio, ChordStyle.RootAndFifth),
                new("Chorus 2", 8, 1, "Pop", ChordStyle.Strum, ChordStyle.Motown),
                new("Outro", 4, 0, "Half-time", ChordStyle.Pad, ChordStyle.WholeNotes, false),
            ]),
    ];
}
