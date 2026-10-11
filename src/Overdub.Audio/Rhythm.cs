namespace Overdub.Audio;

public sealed record TimeSignatureInfo(int Beats, int Unit, int[] Groups, string Words, string Counted, string Description, string Tip, string[] Songs)
{
    public string Name => $"{Beats}/{Unit}";

    public string Title => $"{Name}: {Words}, counted {Counted}";

    public IReadOnlyList<int> GroupStarts
    {
        get
        {
            var starts = new List<int>();
            var at = 0;
            foreach (var group in Groups)
            {
                starts.Add(at);
                at += group;
            }

            return starts;
        }
    }
}

public sealed record ClickStep(int Pitch, int AtMs, int LenMs, int Beat, bool Main);

public static class Rhythm
{
    public static readonly string[] SubdivisionNames = ["Beats", "Doubled", "Triplets"];

    public static IReadOnlyList<TimeSignatureInfo> All { get; } =
    [
        new(4, 4, [2, 2], "four quarter-note beats", "1 2 3 4",
            "The most common meter. Beat 1 is the strongest and beat 3 is the second strongest. Rock, pop and funk are nearly all in 4/4.",
            "Play the root on beat 1 and the fifth or the octave on beat 3.",
            ["Billie Jean (Michael Jackson)", "Another One Bites the Dust (Queen)"]),
        new(3, 4, [3], "three quarter-note beats", "1 2 3",
            "Strong, weak, weak. That is the lilt of a waltz, and it makes a melody feel like it is turning.",
            "Play the root on beat 1 and keep the other two notes lighter.",
            ["The Blue Danube (Strauss)", "My Favorite Things (Rodgers and Hammerstein)"]),
        new(2, 4, [2], "two quarter-note beats", "1 2",
            "A march: left, right. Very simple and driving, and the root and fifth swing back and forth.",
            "Play the root on 1 and the fifth on 2, the classic oom-pah.",
            ["Radetzky March (Strauss)", "Entrance of the Gladiators (Fucik)"]),
        new(5, 4, [3, 2], "five quarter-note beats", "3 + 2",
            "An odd meter felt as a group of 3 followed by a group of 2. It feels like a waltz that is cut short, so it keeps leaning forward.",
            "Count 1 2 3 1 2 and play the root on each 1.",
            ["Take Five (Dave Brubeck)", "Mission: Impossible theme (Lalo Schifrin)"]),
        new(6, 8, [3, 3], "six eighth-note beats", "3 + 3",
            "Two groups of three: it feels like two big beats, each rolling in threes. It is the lilt of ballads and jigs.",
            "Play the root on 1 and 4 and let the notes in between roll.",
            ["House of the Rising Sun (The Animals)", "Nothing Else Matters (Metallica)"]),
        new(7, 8, [2, 2, 3], "seven eighth notes", "2 + 2 + 3",
            "Odd meters are usually felt as short groups of 2 and 3. Here the groups are 2, 2 and 3, so the pulse stumbles forward with a long last group. It sounds restless and driving.",
            "On the bass, play the root on the strong beat of each group (1, 3 and 5), and let the notes in between be short and quiet.",
            ["Money (Pink Floyd), written in 7/4 with the same 2 + 2 + 3 grouping"]),
        new(9, 8, [3, 3, 3], "nine eighth-note beats", "3 + 3 + 3",
            "Three groups of three, a rolling waltz. Some pieces count nine as 2 + 2 + 2 + 3 instead, which feels more jagged.",
            "Play the root on 1, 4 and 7.",
            ["Blue Rondo a la Turk (Dave Brubeck), counted 2 + 2 + 2 + 3"]),
        new(12, 8, [3, 3, 3, 3], "twelve eighth-note beats", "3 + 3 + 3 + 3",
            "Four big beats, each split into three. It is the slow, swaying feel of blues and soul ballads.",
            "Play the root on 1 and 7, with passing notes in between.",
            ["At Last (Etta James)"]),
    ];

    public static TimeSignatureInfo Find(int beats, int unit) => All.FirstOrDefault(s => s.Beats == beats && s.Unit == unit) ?? All[0];

    public static double BeatMs(TimeSignatureInfo info, double bpm) => (info.Unit == 4 ? 60000.0 : 30000.0) / bpm;

    public static IReadOnlyList<ClickStep> Schedule(TimeSignatureInfo info, double bpm, int subdivision, int bars)
    {
        var beatMs = BeatMs(info, bpm);
        var perBeat = subdivision switch { 1 => 2, 2 => 3, _ => 1 };
        var starts = info.GroupStarts;
        var steps = new List<ClickStep>();
        for (var bar = 0; bar < bars; bar++)
        {
            for (var beat = 0; beat < info.Beats; beat++)
            {
                for (var sub = 0; sub < perBeat; sub++)
                {
                    var at = (int)(((bar * info.Beats) + beat + ((double)sub / perBeat)) * beatMs);
                    var main = sub == 0;
                    var pitch = !main ? 67 : beat == 0 ? 84 : starts.Contains(beat) ? 79 : 72;
                    steps.Add(new ClickStep(pitch, at, 70, beat, main));
                }
            }
        }

        return steps;
    }

    public static DesignedSound TickSound { get; } = new()
    {
        Name = "Tick",
        Morph = 0.5,
        Wave = DesignedSound.Shape(0.5),
        Sub = 0f,
        Thickness = 0f,
        Brightness = 0.9f,
        Growl = 0f,
        AttackN = 0f,
        DecayN = 0.1f,
        Sustain = 0f,
        ReleaseN = 0.05f,
    };

    public static IReadOnlyList<(string Name, string Meaning)> NoteLengths(TimeSignatureInfo info) => info.Unit == 4
        ?
        [
            ("Whole note", "four beats"),
            ("Quarter note", "one beat"),
            ("Eighth note", "half a beat"),
            ("Sixteenth note", "a quarter of a beat"),
        ]
        :
        [
            ("Whole note", "eight beats"),
            ("Quarter note", "two beats"),
            ("Eighth note", "one beat"),
            ("Sixteenth note", "half a beat"),
        ];

    public static (int OnTime, int Early, int Late, int Off, double AverageMs) JudgeTaps(IReadOnlyList<double> tapsMs, IReadOnlyList<double> strongMs)
    {
        var onTime = 0;
        var early = 0;
        var late = 0;
        var off = 0;
        var offsets = new List<double>();
        foreach (var tap in tapsMs)
        {
            var nearest = strongMs.OrderBy(s => Math.Abs(s - tap)).FirstOrDefault();
            var offset = tap - nearest;
            if (strongMs.Count == 0 || Math.Abs(offset) > 250)
            {
                off++;
                continue;
            }

            offsets.Add(offset);
            if (Math.Abs(offset) <= 80)
            {
                onTime++;
            }
            else if (offset < 0)
            {
                early++;
            }
            else
            {
                late++;
            }
        }

        return (onTime, early, late, off, offsets.Count == 0 ? 0 : offsets.Average());
    }
}
