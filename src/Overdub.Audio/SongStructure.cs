namespace Overdub.Audio;

public enum SectionKind
{
    Intro,
    Verse,
    Chorus,
    Bridge,
    Solo,
    Breakdown,
    Outro,
    Round,
    Head,
    Turnaround,
    Build,
    Drop,
    Other,
}

public sealed record StyleGuide(string Name, string Template, string Overview);

public sealed record SectionGuide(SectionKind Kind, string Job, string Typical, string Detail, string Drums, string Guitar, string Bass, string Keys, string Tip, string Missing);

public static class SongStructure
{
    public static IReadOnlyList<StyleGuide> Styles { get; } =
    [
        new("Pop", "Pop song", "Pop alternates verses, which tell the story, with choruses, which are the hook. A bridge near the end gives contrast before the last chorus."),
        new("Rock", "Rock song", "Rock follows the same verse and chorus pattern but swaps the bridge for an instrumental solo, then finishes with a big last chorus."),
        new("12-bar blues", "12-bar blues", "The 12-bar blues is a loop. Every round uses the same twelve-bar chord pattern, and the song grows by changing how the band plays each round."),
        new("Funk", "Funk groove", "Funk grooves on one or two chords, so the structure is simple. The breakdown, where the band thins out to bass and drums, is the heart of it."),
        new("Jazz", "Jazz ii-V-I", "Jazz plays the tune (the head), then solos over its chords, then plays the head again. Here the head repeats with a turnaround that leads back to the start."),
        new("Metal", "Metal riff", "Metal builds on a heavy riff. The verse and chorus pattern is followed by a breakdown before a last chorus that hits harder."),
        new("House", "House track", "Dance music is built for the floor: long intros so a DJ can mix in, builds that raise tension, and drops that release it."),
        new("Reggae", "Reggae one drop", "Reggae keeps its verses and choruses relaxed, then ends with a dub outro where the band strips back and the bass and drums take over."),
    ];

    public static SectionKind KindOf(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("intro"))
        {
            return SectionKind.Intro;
        }

        if (n.Contains("outro"))
        {
            return SectionKind.Outro;
        }

        if (n.Contains("chorus") || n.Contains("hook"))
        {
            return SectionKind.Chorus;
        }

        if (n.Contains("verse"))
        {
            return SectionKind.Verse;
        }

        if (n.Contains("bridge"))
        {
            return SectionKind.Bridge;
        }

        if (n.Contains("solo"))
        {
            return SectionKind.Solo;
        }

        if (n.Contains("break"))
        {
            return SectionKind.Breakdown;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(n, @"\bround\b"))
        {
            return SectionKind.Round;
        }

        if (n.Contains("turnaround"))
        {
            return SectionKind.Turnaround;
        }

        if (n.Contains("head"))
        {
            return SectionKind.Head;
        }

        if (n.Contains("build"))
        {
            return SectionKind.Build;
        }

        return n.Contains("drop") ? SectionKind.Drop : SectionKind.Other;
    }

    public static SectionGuide GuideFor(SectionKind kind) => Guides.First(g => g.Kind == kind);

    private static readonly SectionGuide[] Guides =
    [
        new(SectionKind.Intro, "Sets the mood before the singing starts", "usually 4 to 8 bars",
            "The intro tells the listener the key, the tempo and the feel, without giving everything away. It is short and often quieter than what follows.",
            "Light or half-time, no big fills yet", "A quiet pad or arpeggio that sets the key", "Long notes, or silence to leave room", "Soft sustained chords",
            "Keep it simple: whole notes on the root, then come in properly when the verse starts.", "An intro sets the mood before the first verse."),
        new(SectionKind.Verse, "Tells the story", "usually 8 bars",
            "The words change in each verse while the music stays the same, so the band plays steady and leaves room for the voice.",
            "A steady groove with small fills", "Lighter strumming", "Root and fifth, supportive", "Sparse chords",
            "Stay on the roots and fifths, and save the movement for the chorus.", "A verse carries the story and the words."),
        new(SectionKind.Chorus, "The hook: the part people sing back", "usually 8 bars",
            "Everything builds towards the chorus. It repeats the title and the main melody, and it is the loudest, fullest part, so the band plays bigger here than in the verse.",
            "Fuller beat, open hi-hat, a fill before it", "Strums wider, plays on every beat", "Locks to the kick, more movement", "Sustained chords that lift the sound",
            "In the verse play the root and fifth, then open up in the chorus with octaves and a fill into the next section.", "A chorus is the hook that people remember."),
        new(SectionKind.Bridge, "A change of scene", "usually 4 to 8 bars",
            "The bridge uses different chords for a few bars so the last chorus feels fresh when it returns.",
            "Pull back, or switch to half-time", "Arpeggios or a new rhythm", "Long notes that build", "Pads that hold the harmony",
            "Go quiet or change the rhythm, then build a fill into the last chorus.", "A bridge before the last chorus makes the ending feel new."),
        new(SectionKind.Solo, "Space for one instrument to take the lead", "usually 8 bars",
            "A solo is played over the verse or chorus chords. The rest of the band supports it and gets out of its way.",
            "Steady, a little busier than the verse", "Plays the solo, or backs it with chords", "Lock in with the drums and stay out of the way", "Sustained support",
            "Play simple and tight. The solo is the star.", "A solo gives an instrument a turn in the spotlight."),
        new(SectionKind.Breakdown, "The band drops out", "usually 4 bars",
            "A breakdown strips the music back so the return of the full band feels bigger. It builds anticipation.",
            "Half-time, or just a pulse", "Held chords, or silent", "Syncopated, with gaps", "A held pad",
            "Leave space. The gaps are the point, and the return feels huge because of them.", "A breakdown drops the band out so the return feels bigger."),
        new(SectionKind.Outro, "Winds the song down", "usually 4 to 8 bars",
            "The outro gives the song an ending instead of stopping suddenly. It often repeats the last chord or fades.",
            "Half-time, or fading out", "A quiet pad", "Long root notes", "A held chord",
            "Hold the root and let it ring until the end.", "An outro winds the song down instead of stopping suddenly."),
        new(SectionKind.Round, "One trip through the 12 bars", "12 bars",
            "A blues round is the chord pattern I for four bars, IV for two, I for two, then V, IV and I. Rounds repeat, and the band plays each one a little differently.",
            "A shuffle that stays steady", "Chords on the off-beats, or a riff", "A walking bass in quarter notes", "Chords that stay out of the bass",
            "Walk up to each chord change with an approach note, and keep the walk steady all the way through.", "Rounds repeat the 12 bars with a changing feel."),
        new(SectionKind.Head, "The tune", "usually 8 to 32 bars",
            "Jazz states the main melody, the head, then plays solos over its chords, then returns to the head at the end.",
            "Brushes or a light ride cymbal", "Soft chords and comping", "A walking bass line", "Comping chords",
            "Walk the changes in quarter notes and land on the root at each new chord.", "The head states the tune."),
        new(SectionKind.Turnaround, "Leads back to the start", "usually 4 to 8 bars",
            "A turnaround is a short passage that moves the harmony so the tune can start again.",
            "Light, with a small fill at the end", "Comping, and a stronger last chord", "Walking toward the first chord", "Comping",
            "Aim your last note at the root of the first chord of the next round.", "A turnaround leads back to the start."),
        new(SectionKind.Build, "Raises the tension", "usually 8 bars",
            "A build adds layers one at a time: more hats, a bass pulse, an arpeggio. The longer it goes, the bigger the drop feels.",
            "Adds hats, then a snare roll", "An arpeggio rising in pitch", "A pulse that gets busier", "A pad that rises in pitch",
            "Add one thing at a time and keep the notes short so the drop hits hard.", "A build raises tension before the drop."),
        new(SectionKind.Drop, "The payoff", "usually 16 bars",
            "The drop is where everything hits at once: the full groove, the heaviest bass and the main hook.",
            "Four on the floor with open hats", "A bright arpeggio", "Pulsing roots locked to the kick", "Full chords",
            "Lock every note to the kick drum and keep the bass steady for the whole drop.", "A drop is the payoff after a build."),
        new(SectionKind.Other, "A part of the song", "varies",
            "Songs are built from parts that repeat and contrast. The name of this part is yours.",
            "Follows the part before it", "Follows the part before it", "Follows the part before it", "Follows the part before it",
            "Listen to what the rest of the band is doing and match it.", "This part is yours to name."),
    ];
}
