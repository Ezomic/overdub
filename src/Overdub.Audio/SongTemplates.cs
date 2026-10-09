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
        new(
            "Funk groove",
            "Intro, verse, chorus, verse, chorus, breakdown, last chorus in E at 100 BPM on a one-chord vamp, with sixteenth-note funk and slap bass.",
            100,
            4,
            [
                [(0, Dom), (0, Dom), (5, Dom), (0, Dom)],
                [(0, Dom), (5, Dom), (7, Dom), (5, Dom)],
            ],
            [
                new("Intro", 4, 0, "Funk", ChordStyle.Strum, ChordStyle.Funk, false),
                new("Verse", 8, 0, "Funk", ChordStyle.Strum, ChordStyle.Funk16),
                new("Chorus", 8, 1, "Funk", ChordStyle.FolkStrum, ChordStyle.Slap),
                new("Verse 2", 8, 0, "Funk", ChordStyle.Strum, ChordStyle.Funk16),
                new("Chorus 2", 8, 1, "Funk", ChordStyle.FolkStrum, ChordStyle.Slap),
                new("Breakdown", 4, 0, "Half-time", ChordStyle.Pad, ChordStyle.Syncopated),
                new("Last chorus", 8, 1, "Funk", ChordStyle.FolkStrum, ChordStyle.Slap),
            ]),
        new(
            "Reggae one drop",
            "Verse, chorus, verse, chorus, dub outro in A minor at 76 BPM, with off-beat chords and a sparse, melodic bass.",
            76,
            9,
            [
                [(0, Min), (0, Min), (5, Maj), (7, Maj)],
                [(0, Min), (7, Maj), (5, Maj), (0, Min)],
            ],
            [
                new("Verse", 8, 0, "Reggae", ChordStyle.Strum, ChordStyle.Reggae),
                new("Chorus", 8, 1, "Reggae", ChordStyle.FolkStrum, ChordStyle.Reggae),
                new("Verse 2", 8, 0, "Reggae", ChordStyle.Strum, ChordStyle.RootAndFifth),
                new("Chorus 2", 8, 1, "Reggae", ChordStyle.FolkStrum, ChordStyle.Reggae),
                new("Dub outro", 8, 0, "Reggae", ChordStyle.Pad, ChordStyle.WholeNotes, false),
            ]),
        new(
            "Punk rock",
            "Short and fast: verse, chorus, verse, chorus, bridge, last chorus in D at 168 BPM with straight eighth-note bass.",
            168,
            2,
            [
                [(0, Maj), (5, Maj), (7, Maj), (5, Maj)],
                [(0, Maj), (7, Maj), (5, Maj), (7, Maj)],
                [(9, Min), (5, Maj), (0, Maj), (7, Maj)],
            ],
            [
                new("Verse", 8, 0, "Rock", ChordStyle.Strum, ChordStyle.Rock8ths),
                new("Chorus", 8, 1, "Rock", ChordStyle.FolkStrum, ChordStyle.Rock8ths),
                new("Verse 2", 8, 0, "Rock", ChordStyle.Strum, ChordStyle.Rock8ths),
                new("Chorus 2", 8, 1, "Rock", ChordStyle.FolkStrum, ChordStyle.Rock8ths),
                new("Bridge", 4, 2, "Half-time", ChordStyle.Strum, ChordStyle.Octaves),
                new("Last chorus", 8, 1, "Rock", ChordStyle.FolkStrum, ChordStyle.Rock8ths),
            ]),
        new(
            "Metal riff",
            "Intro, verse, chorus, verse, chorus, breakdown, last chorus in E minor at 150 BPM with driving root eighths.",
            150,
            4,
            [
                [(0, Min), (0, Min), (3, Maj), (0, Min)],
                [(8, Maj), (3, Maj), (10, Maj), (0, Min)],
                [(0, Min), (1, Maj), (0, Min), (6, Maj)],
            ],
            [
                new("Intro", 4, 0, "Metal", ChordStyle.Strum, ChordStyle.Rock8ths, false),
                new("Verse", 8, 0, "Metal", ChordStyle.Strum, ChordStyle.Rock8ths),
                new("Chorus", 8, 1, "Metal", ChordStyle.FolkStrum, ChordStyle.Octaves),
                new("Verse 2", 8, 0, "Metal", ChordStyle.Strum, ChordStyle.Rock8ths),
                new("Chorus 2", 8, 1, "Metal", ChordStyle.FolkStrum, ChordStyle.Octaves),
                new("Breakdown", 4, 2, "Half-time", ChordStyle.Strum, ChordStyle.RootPulse),
                new("Last chorus", 8, 1, "Metal", ChordStyle.FolkStrum, ChordStyle.Octaves),
            ]),
        new(
            "Disco groove",
            "Intro, verse, chorus, verse, chorus, break, last chorus in F minor at 118 BPM with four on the floor and octave bass.",
            118,
            5,
            [
                [(0, Min), (8, Maj), (3, Maj), (10, Maj)],
                [(0, Min), (5, Min), (8, Maj), (7, Dom)],
            ],
            [
                new("Intro", 4, 0, "Disco", ChordStyle.Strum, ChordStyle.Octaves, false),
                new("Verse", 8, 0, "Disco", ChordStyle.Strum, ChordStyle.Octaves),
                new("Chorus", 8, 1, "Disco", ChordStyle.FolkStrum, ChordStyle.Funk),
                new("Verse 2", 8, 0, "Disco", ChordStyle.Strum, ChordStyle.Octaves),
                new("Chorus 2", 8, 1, "Disco", ChordStyle.FolkStrum, ChordStyle.Funk),
                new("Break", 4, 0, "Half-time", ChordStyle.Pad, ChordStyle.WholeNotes),
                new("Last chorus", 8, 1, "Disco", ChordStyle.FolkStrum, ChordStyle.Funk),
            ]),
        new(
            "Country shuffle",
            "Verse, chorus, verse, chorus, solo, chorus in G at 112 BPM with a root and fifth bass.",
            112,
            7,
            [
                [(0, Maj), (0, Maj), (5, Maj), (7, Dom)],
                [(0, Maj), (5, Maj), (7, Maj), (0, Maj)],
            ],
            [
                new("Verse", 8, 0, "Pop", ChordStyle.FolkStrum, ChordStyle.RootAndFifth),
                new("Chorus", 8, 1, "Pop", ChordStyle.FolkStrum, ChordStyle.Walking),
                new("Verse 2", 8, 0, "Pop", ChordStyle.FolkStrum, ChordStyle.RootAndFifth),
                new("Chorus 2", 8, 1, "Pop", ChordStyle.FolkStrum, ChordStyle.Walking),
                new("Solo", 8, 0, "Pop", ChordStyle.Strum, ChordStyle.RootAndFifth),
                new("Last chorus", 8, 1, "Pop", ChordStyle.FolkStrum, ChordStyle.Walking),
            ]),
        new(
            "Jazz ii-V-I",
            "Two choruses of ii V I, then a minor turnaround in C at 132 BPM, with a walking bass.",
            132,
            0,
            [
                [(2, Min), (7, Dom), (0, Maj), (0, Maj)],
                [(4, Min), (9, Dom), (2, Min), (7, Dom)],
            ],
            [
                new("Head", 8, 0, "Bossa nova", ChordStyle.Arpeggio, ChordStyle.Walking, false),
                new("Head 2", 8, 0, "Bossa nova", ChordStyle.Arpeggio, ChordStyle.WalkingApproach),
                new("Turnaround", 8, 1, "Bossa nova", ChordStyle.Arpeggio, ChordStyle.Walking, false),
                new("Last head", 8, 0, "Bossa nova", ChordStyle.Arpeggio, ChordStyle.WalkingApproach),
            ]),
        new(
            "Bossa nova",
            "Intro, A section, B section, A section, outro in A minor at 120 BPM with arpeggiated guitar and a root and fifth bass.",
            120,
            9,
            [
                [(0, Min), (2, Min), (7, Dom), (0, Maj)],
                [(5, Maj), (4, Dom), (9, Min), (2, Dom)],
            ],
            [
                new("Intro", 4, 0, "Bossa nova", ChordStyle.Arpeggio, ChordStyle.RootAndFifth, false),
                new("A", 8, 0, "Bossa nova", ChordStyle.Arpeggio, ChordStyle.RootAndFifth),
                new("B", 8, 1, "Bossa nova", ChordStyle.Arpeggio, ChordStyle.Syncopated),
                new("A 2", 8, 0, "Bossa nova", ChordStyle.Arpeggio, ChordStyle.RootAndFifth),
                new("Outro", 4, 0, "Bossa nova", ChordStyle.Pad, ChordStyle.WholeNotes, false),
            ]),
        new(
            "Hip-hop loop",
            "A four-bar loop built up over intro, verse, hook, verse, hook, outro in C minor at 90 BPM, with a simple sub bass.",
            90,
            0,
            [
                [(0, Min), (8, Maj), (3, Maj), (10, Maj)],
                [(0, Min), (0, Min), (8, Maj), (7, Maj)],
            ],
            [
                new("Intro", 4, 0, "Hip-hop", ChordStyle.Pad, ChordStyle.WholeNotes, false),
                new("Verse", 8, 0, "Hip-hop", ChordStyle.Pad, ChordStyle.RootPulse),
                new("Hook", 8, 1, "Hip-hop", ChordStyle.Arpeggio, ChordStyle.Syncopated),
                new("Verse 2", 8, 0, "Hip-hop", ChordStyle.Pad, ChordStyle.RootPulse),
                new("Hook 2", 8, 1, "Hip-hop", ChordStyle.Arpeggio, ChordStyle.Syncopated),
                new("Outro", 4, 0, "Hip-hop", ChordStyle.Pad, ChordStyle.WholeNotes, false),
            ]),
        new(
            "House track",
            "Intro, build, drop, break, drop, outro in A minor at 124 BPM with four on the floor and a pulsing bass.",
            124,
            9,
            [
                [(0, Min), (0, Min), (8, Maj), (7, Maj)],
                [(0, Min), (5, Maj), (8, Maj), (10, Maj)],
            ],
            [
                new("Intro", 8, 0, "House", ChordStyle.Pad, ChordStyle.RootPulse, false),
                new("Build", 8, 0, "House", ChordStyle.Arpeggio, ChordStyle.RootPulse),
                new("Drop", 16, 1, "House", ChordStyle.Arpeggio, ChordStyle.Rock8ths),
                new("Break", 8, 0, "Half-time", ChordStyle.Pad, ChordStyle.WholeNotes, false),
                new("Drop 2", 16, 1, "House", ChordStyle.Arpeggio, ChordStyle.Rock8ths),
                new("Outro", 8, 0, "House", ChordStyle.Pad, ChordStyle.RootPulse, false),
            ]),
        new(
            "Motown soul",
            "Intro, verse, chorus, verse, chorus, bridge, last chorus in Bb at 116 BPM with a rolling Motown bass.",
            116,
            10,
            [
                [(0, Maj), (9, Min), (2, Min), (7, Dom)],
                [(5, Maj), (7, Dom), (0, Maj), (9, Min)],
                [(2, Min), (7, Dom), (0, Maj), (5, Maj)],
            ],
            [
                new("Intro", 4, 0, "Pop", ChordStyle.Strum, ChordStyle.Motown, false),
                new("Verse", 8, 0, "Pop", ChordStyle.Strum, ChordStyle.Motown),
                new("Chorus", 8, 1, "Pop", ChordStyle.FolkStrum, ChordStyle.Motown),
                new("Verse 2", 8, 0, "Pop", ChordStyle.Strum, ChordStyle.Motown),
                new("Chorus 2", 8, 1, "Pop", ChordStyle.FolkStrum, ChordStyle.Motown),
                new("Bridge", 4, 2, "Half-time", ChordStyle.Arpeggio, ChordStyle.RootAndFifth),
                new("Last chorus", 8, 1, "Pop", ChordStyle.FolkStrum, ChordStyle.Motown),
            ]),
        new(
            "Bass practice loop",
            "One short loop of four chords in E minor at 90 BPM, repeated eight times, for drilling a bass line over simple drums.",
            90,
            4,
            [
                [(0, Min), (8, Maj), (3, Maj), (10, Maj)],
            ],
            [
                new("Loop 1", 4, 0, "Rock", ChordStyle.Pad, ChordStyle.WholeNotes, false),
                new("Loop 2", 4, 0, "Rock", ChordStyle.Pad, ChordStyle.RootPulse, false),
                new("Loop 3", 4, 0, "Rock", ChordStyle.Pad, ChordStyle.RootAndFifth, false),
                new("Loop 4", 4, 0, "Rock", ChordStyle.Pad, ChordStyle.Rock8ths, false),
                new("Loop 5", 4, 0, "Rock", ChordStyle.Pad, ChordStyle.Walking, false),
                new("Loop 6", 4, 0, "Rock", ChordStyle.Pad, ChordStyle.Syncopated, false),
                new("Loop 7", 4, 0, "Rock", ChordStyle.Pad, ChordStyle.Motown, false),
                new("Loop 8", 4, 0, "Rock", ChordStyle.Pad, ChordStyle.ArpeggioDrill, false),
            ]),
    ];
}
