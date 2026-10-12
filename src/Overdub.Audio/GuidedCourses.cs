using System.Text.Json;

namespace Overdub.Audio;

public sealed record GuidedStep(string Id, string Title, string Lesson, int Minutes, string CheckText, string Key, int Needed);

public sealed record GuidedCourse(string Id, string Title, string Blurb, IReadOnlyList<GuidedStep> Steps)
{
    public int Minutes => Steps.Sum(s => s.Minutes);
}

public static class GuidedCourses
{
    private const string Numerals = "Start here: understand your song";
    private const string BassLine = "Why this bass line works";
    private const string Explorer = "Scale and mode explorer";
    private const string Circle = "Circle of fifths and progression builder";
    private const string Ear = "Ear trainer";
    private const string Quiz = "Theory quiz";
    private const string Rhythm = "Rhythm and time signatures";
    private const string Challenges = "Practice challenges";
    private const string Structure = "Song structure by style";
    private const string Arrangement = "Arrangement: build a song that grows";
    private const string Mixing = "Mixing basics";

    private static string Opened(string lesson) => $"opened:{lesson}";

    public static IReadOnlyList<GuidedCourse> All { get; } =
    [
        new("bass", "Bass foundations", "From knowing nothing about your song to playing lines that fit it. Each step is a short lesson with a check at the end.",
        [
            new("bass-1", "See your song as numbers", Numerals, 6, "open the lesson", Opened(Numerals), 1),
            new("bass-2", "Why a bass line works", BassLine, 8, "open the lesson", Opened(BassLine), 1),
            new("bass-3", "Hear the notes in a scale", Explorer, 8, "name the 5th of your key", "check:fifth", 1),
            new("bass-4", "Train your ear", Ear, 6, "6 of 10 intervals right", "ear:Intervals", 6),
            new("bass-5", "Know your numerals", Quiz, 5, "7 of 10 right", "quiz", 7),
            new("bass-6", "Count in time", Rhythm, 6, "tap 5 of 8 on time", "tap", 5),
            new("bass-7", "Play it yourself", Challenges, 10, "pass Root and fifth", "challenge:root-fifth", 2),
        ]),
        new("harmony", "Understand harmony", "How chords, keys and scales fit together, so you can see why songs sound the way they do.",
        [
            new("harmony-1", "See your song as numbers", Numerals, 6, "open the lesson", Opened(Numerals), 1),
            new("harmony-2", "Find your way round the circle", Circle, 8, "open the lesson", Opened(Circle), 1),
            new("harmony-3", "Hear the notes in a scale", Explorer, 8, "name the 5th of your key", "check:fifth", 1),
            new("harmony-4", "Know your numerals", Quiz, 5, "7 of 10 right", "quiz", 7),
            new("harmony-5", "Hear major and minor", Ear, 6, "7 of 10 right", "ear:MajorMinor", 7),
            new("harmony-6", "See how songs are built", Structure, 6, "open the lesson", Opened(Structure), 1),
        ]),
        new("rhythm", "Rhythm and groove", "Count the beat, then play in time with the song until it feels steady.",
        [
            new("rhythm-1", "Count in time", Rhythm, 6, "tap 5 of 8 on time", "tap", 5),
            new("rhythm-2", "Why a bass line works", BassLine, 8, "open the lesson", Opened(BassLine), 1),
            new("rhythm-3", "Roots on the beat", Challenges, 10, "pass Roots on the beat", "challenge:roots", 2),
            new("rhythm-4", "Root and fifth", Challenges, 10, "pass Root and fifth", "challenge:root-fifth", 2),
            new("rhythm-5", "See how songs are built", Structure, 6, "open the lesson", Opened(Structure), 1),
        ]),
        new("mix", "Mix and produce", "Arrange the song, then balance it so every part can be heard.",
        [
            new("mix-1", "Know the parts of a song", Structure, 6, "open the lesson", Opened(Structure), 1),
            new("mix-2", "Grow the arrangement", Arrangement, 10, "apply one of the ideas", "arr:any", 1),
            new("mix-3", "Balance the levels", Mixing, 6, "apply the Levels step", "mix:Levels", 1),
            new("mix-4", "Make room for each instrument", Mixing, 6, "apply the EQ step", "mix:Room", 1),
            new("mix-5", "Add space", Mixing, 5, "apply the Space step", "mix:Space", 1),
            new("mix-6", "Finish for sharing", Mixing, 4, "pick a mastering preset", "mix:Finish", 1),
        ]),
    ];
}

public static class GuidedProgress
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private sealed class Data
    {
        public Dictionary<string, int> Best { get; set; } = [];
        public string? LastCourse { get; set; }
        public string? LastStep { get; set; }
    }

    private static string FilePath => Path.Combine(AppPaths.SettingsDirectory, "guided.json");

    public static void Report(string key, int value)
    {
        var data = Load();
        if (data.Best.GetValueOrDefault(key, int.MinValue) >= value)
        {
            return;
        }

        data.Best[key] = value;
        Save(data);
    }

    public static int Best(string key) => Load().Best.GetValueOrDefault(key, 0);

    public static bool IsDone(GuidedStep step)
    {
        var data = Load();
        return data.Best.GetValueOrDefault(step.Key, 0) >= step.Needed || data.Best.GetValueOrDefault($"manual:{step.Id}", 0) >= 1;
    }

    public static void MarkDone(GuidedStep step) => Report($"manual:{step.Id}", 1);

    public static int DoneCount(GuidedCourse course) => course.Steps.Count(IsDone);

    public static GuidedStep? NextStep(GuidedCourse course) => course.Steps.FirstOrDefault(s => !IsDone(s));

    public static void SetLast(GuidedCourse course, GuidedStep step)
    {
        var data = Load();
        data.LastCourse = course.Id;
        data.LastStep = step.Id;
        Save(data);
    }

    public static GuidedCourse? LastCourse()
    {
        var id = Load().LastCourse;
        return GuidedCourses.All.FirstOrDefault(c => c.Id == id);
    }

    public static bool Started => GuidedCourses.All.Any(c => DoneCount(c) > 0) || Load().LastCourse is not null;

    private static Data Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) ?? new Data() : new Data();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            return new Data();
        }
    }

    private static void Save(Data data)
    {
        Directory.CreateDirectory(AppPaths.SettingsDirectory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(data, Json));
    }
}
