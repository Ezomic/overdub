namespace Overdub.Audio;

public enum ScaleRole
{
    Root,
    ChordTone,
    Colour,
    Other,
}

public sealed record ScaleNote(int PitchClass, int Interval, string Name, string Label, ScaleRole Role);

public sealed record ScaleDef(string Name, int[] Intervals, string[] Labels, int ParentShift, int Colour, int CompareOffset, string Sound, string Tip)
{
    public bool IsMinorSound => Intervals.Contains(3);
}

public static class ScaleExplorer
{
    public static IReadOnlyList<ScaleDef> All { get; } =
    [
        new("Major", [0, 2, 4, 5, 7, 9, 11], ["1", "2", "3", "4", "5", "6", "7"], 0, -1, 0,
            "The bright, settled sound most pop and folk songs are built on. Every other scale here is described by how it differs from this one.",
            "Play {r} and walk up the notes one at a time, ending on {r} an octave higher. Then stop on the 7th ({c}) and hear how it wants to resolve."),
        new("Natural minor", [0, 2, 3, 5, 7, 8, 10], ["1", "2", "b3", "4", "5", "b6", "b7"], 9, -1, 0,
            "The darker, sadder cousin of major. It has three flat notes (the 3rd, 6th and 7th) compared with major.",
            "Play from {r} up to the octave and listen for the darker feel. The same notes start on a different root in a major key: that is the relative major."),
        new("Dorian", [0, 2, 3, 5, 7, 9, 10], ["1", "2", "b3", "4", "5", "6", "b7"], 2, 5, -1,
            "A minor scale with a brighter 6th. That one note is the whole difference from natural minor, and it gives funk and soul bass lines their cool, lifted feel.",
            "Play {r}, then lean on the {c} over a {r} minor chord. Hear the lift, then compare with {c2}."),
        new("Mixolydian", [0, 2, 4, 5, 7, 9, 10], ["1", "2", "3", "4", "5", "6", "b7"], 7, 6, 1,
            "A major scale with a flat 7th. It sounds happy but unfinished, the sound of blues rock, funk and classic rock riffs.",
            "Play {r}, then the {c} over a {r} chord and hear the bluesy pull. Then compare with {c2}, the major 7th, which sounds more finished."),
        new("Lydian", [0, 2, 4, 6, 7, 9, 11], ["1", "2", "3", "#4", "5", "6", "7"], 5, 3, -1,
            "A major scale with a raised 4th. It sounds dreamy and floating, a favourite for film music.",
            "Play {r}, then the {c} over a {r} chord and hear the dreamy lift. Then compare with {c2}, the ordinary 4th."),
        new("Phrygian", [0, 1, 3, 5, 7, 8, 10], ["1", "b2", "b3", "4", "5", "b6", "b7"], 4, 1, 1,
            "A minor scale with a flat 2nd. That note just above the root gives it a dark, Spanish or metal sound.",
            "Play {r}, then the {c} (one fret above it) and hear the dark pull. Then compare with {c2}."),
        new("Locrian", [0, 1, 3, 5, 6, 8, 10], ["1", "b2", "b3", "4", "b5", "b6", "b7"], 11, 4, 1,
            "The unstable one. Its 5th is flat, so the root chord never feels settled. It is rare, but useful to know.",
            "Play {r}, then the {c} and hear the unstable edge. Then compare with {c2}, the normal 5th, which sounds solid."),
        new("Minor pentatonic", [0, 3, 5, 7, 10], ["1", "b3", "4", "5", "b7"], 9, -1, 0,
            "Five notes taken from natural minor, leaving out the ones that clash. Almost any order of these notes sounds good, which is why it is the go-to for riffs and solos.",
            "Play {r}, then jump around the notes in any order over a {r} minor chord. It is hard to hit a wrong one."),
        new("Major pentatonic", [0, 2, 4, 7, 9], ["1", "2", "3", "5", "6"], 0, -1, 0,
            "Five notes taken from the major scale, leaving out the 4th and 7th. It sounds open and sweet, common in country and pop melodies.",
            "Play {r}, then jump around the notes in any order over a {r} chord. Everything fits."),
        new("Blues", [0, 3, 5, 6, 7, 10], ["1", "b3", "4", "b5", "5", "b7"], 9, 3, 1,
            "The minor pentatonic plus one extra note, the flat 5th or blue note. Passing through it gives the blues its tension.",
            "Play the scale and linger on the {c}, the blue note. Then slide it up to {c2} for the classic blues move."),
    ];

    public static ScaleDef Find(string name) => All.First(s => s.Name == name);

    public static IReadOnlyList<ScaleNote> Notes(ScaleDef scale, int root)
    {
        var flats = ParentFlats(scale, root);
        var third = scale.Intervals.Where(i => i is 3 or 4).Cast<int?>().FirstOrDefault();
        var fifth = scale.Intervals.Contains(7) ? 7 : 6;
        var notes = new List<ScaleNote>();
        for (var i = 0; i < scale.Intervals.Length; i++)
        {
            var interval = scale.Intervals[i];
            var role = i == 0 ? ScaleRole.Root
                : i == scale.Colour ? ScaleRole.Colour
                : interval == third || interval == fifth ? ScaleRole.ChordTone
                : ScaleRole.Other;
            var useFlat = flats || (scale.Name == "Blues" && interval == 6);
            notes.Add(new ScaleNote(Mod(root + interval), interval, NoteSpelling.NameFor(Mod(root + interval), useFlat), scale.Labels[i], role));
        }

        return notes;
    }

    public static string Tip(ScaleDef scale, int root)
    {
        var notes = Notes(scale, root);
        var colour = scale.Colour >= 0 ? notes[scale.Colour] : notes[^1];
        var flats = ParentFlats(scale, root);
        var other = NoteSpelling.NameFor(Mod(colour.PitchClass + scale.CompareOffset), flats || scale.CompareOffset < 0);
        return scale.Tip.Replace("{r}", notes[0].Name).Replace("{c}", colour.Name).Replace("{c2}", other);
    }

    public static bool SameNotesAs(ScaleDef scale, int root, SongKey key)
    {
        var mine = scale.Intervals.Select(i => Mod(root + i)).OrderBy(p => p);
        var theirs = Theory.Scale(key.Minor).Select(i => Mod(key.Root + i)).OrderBy(p => p);
        return mine.SequenceEqual(theirs);
    }

    public static ScaleDef? CompareWith(ScaleDef scale) => scale.Name switch
    {
        "Natural minor" => Find("Major"),
        "Major" => Find("Natural minor"),
        _ when scale.Colour < 0 => null,
        _ => scale.IsMinorSound ? Find("Natural minor") : Find("Major"),
    };

    public static IReadOnlyList<(int Pitch, int AtMs, int LenMs)> Melody(ScaleDef scale, int root, int stepMs = 320)
    {
        var start = 36 + Mod(root);
        var pitches = scale.Intervals.Select(i => start + i).Append(start + 12).ToList();
        return pitches.Select((p, i) => (p, i * stepMs, stepMs + 120)).ToList();
    }

    public static IReadOnlyList<(int Pitch, int AtMs, int LenMs)> OverDrone(ScaleDef scale, int root, int stepMs = 420)
    {
        var melody = Melody(scale, root, stepMs).Select(n => (n.Pitch + 12, n.AtMs, n.LenMs)).ToList();
        var total = melody[^1].Item2 + melody[^1].Item3;
        var drone = (36 + Mod(root), 0, total);
        return melody.Prepend(drone).ToList();
    }

    public static IReadOnlyList<(int Pitch, int AtMs, int LenMs)> Compare(ScaleDef first, ScaleDef second, int root)
    {
        var a = Melody(first, root);
        var offset = a[^1].AtMs + a[^1].LenMs + 500;
        return a.Concat(Melody(second, root).Select(n => (n.Pitch, n.AtMs + offset, n.LenMs))).ToList();
    }

    private static bool ParentFlats(ScaleDef scale, int root)
    {
        var parent = Mod(root - scale.ParentShift);
        return NoteSpelling.Mode switch { 1 => false, 2 => true, _ => NoteSpelling.KeyUsesFlats(parent, false) };
    }

    private static int Mod(int value) => ((value % 12) + 12) % 12;
}
