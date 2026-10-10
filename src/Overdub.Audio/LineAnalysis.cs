namespace Overdub.Audio;

public enum NoteRole
{
    Root,
    Third,
    Fifth,
    Seventh,
    Octave,
    Approach,
    Passing,
}

public sealed record LineNote(int Pitch, double Start, double Length, NoteRole Role, string Name);

public sealed record BarAnalysis(IReadOnlyList<LineNote> Notes, string Title, string Explanation, string Tip);

public static class LineAnalysis
{
    public static string RoleLabel(NoteRole role) => role switch
    {
        NoteRole.Root => "root",
        NoteRole.Third => "third",
        NoteRole.Fifth => "fifth",
        NoteRole.Seventh => "seventh",
        NoteRole.Octave => "octave",
        NoteRole.Approach => "approach",
        _ => "passing",
    };

    public static BarAnalysis Analyse(Chord chord, Chord? next, int bar, string numeral, IReadOnlyList<(int Pitch, double Start, double Length)> played, bool flats)
    {
        var ordered = played.OrderBy(n => n.Start).ToList();
        var rootPitches = ordered.Where(n => Mod(n.Pitch - chord.Root) == 0).Select(n => n.Pitch).ToList();
        var lowestRoot = rootPitches.Count == 0 ? int.MaxValue : rootPitches.Min();
        var tones = chord.Intervals;
        var notes = new List<LineNote>();
        for (var i = 0; i < ordered.Count; i++)
        {
            var (pitch, start, length) = ordered[i];
            var offset = Mod(pitch - chord.Root);
            NoteRole role;
            if (offset == 0)
            {
                role = pitch - lowestRoot >= 12 ? NoteRole.Octave : NoteRole.Root;
            }
            else if (tones.Contains(offset) && offset is 3 or 4)
            {
                role = NoteRole.Third;
            }
            else if (tones.Contains(offset) && offset is 6 or 7)
            {
                role = NoteRole.Fifth;
            }
            else if (tones.Contains(offset) && offset is 10 or 11)
            {
                role = NoteRole.Seventh;
            }
            else if (next is { } following && i >= ordered.Count - 2 && Distance(pitch, following.Root) is >= 1 and <= 2)
            {
                role = NoteRole.Approach;
            }
            else
            {
                role = NoteRole.Passing;
            }

            notes.Add(new LineNote(pitch, start, length, role, NoteSpelling.NameFor(pitch, flats)));
        }

        var name = NoteSpelling.NameFor(chord.Root, flats) + chord.QualitySuffix;
        var title = notes.Count == 0
            ? $"Bar {bar} · {name} ({numeral}): no bass notes"
            : $"Bar {bar} · {name} ({numeral}): {Phrase(notes, chord)}";
        return new BarAnalysis(notes, title, Explain(notes, chord, next, flats), Tip(notes, chord, next, flats));
    }

    private static int Mod(int value) => ((value % 12) + 12) % 12;

    private static int Distance(int pitch, int targetRoot)
    {
        var d = Mod(pitch - targetRoot);
        return Math.Min(d, 12 - d);
    }

    private static string Article(NoteRole role) => role is NoteRole.Approach or NoteRole.Octave ? "an" : "a";

    private static string Phrase(IReadOnlyList<LineNote> notes, Chord chord)
    {
        if (notes.Count > 6)
        {
            var groups = notes.GroupBy(n => n.Role).OrderByDescending(g => g.Count()).ToList();
            return string.Join(", ", groups.Select(g => $"{g.Count()} {RoleLabel(g.Key)}{(g.Count() > 1 ? "s" : "")}"));
        }

        if (notes.Count == 1)
        {
            return $"one {RoleLabel(notes[0].Role)} note";
        }

        var last = notes[^1].Role;
        if (last is NoteRole.Approach or NoteRole.Passing)
        {
            var head = string.Join(", ", notes.Take(notes.Count - 1).Select(n => RoleLabel(n.Role)));
            return last == NoteRole.Passing && Mod(notes[^1].Pitch - chord.Root) == 9 ? $"{head}, then the 6th" : $"{head}, then {Article(last)} {RoleLabel(last)} note";
        }

        return string.Join(", ", notes.Select(n => RoleLabel(n.Role)));
    }

    private static string Names(IEnumerable<LineNote> notes) => string.Join(" and ", notes.Select(n => n.Name).Distinct());

    private static string Explain(IReadOnlyList<LineNote> notes, Chord chord, Chord? next, bool flats)
    {
        if (notes.Count == 0)
        {
            return "The bass is silent in this bar. Rests are part of a line too: they give the other instruments room.";
        }

        var chordName = NoteSpelling.NameFor(chord.Root, flats) + chord.QualitySuffix;
        var minorChord = chord.Quality is ChordQuality.Minor or ChordQuality.MinorSeventh or ChordQuality.Diminished;
        var parts = new List<string>();
        foreach (var group in notes.GroupBy(n => n.Role))
        {
            var names = Names(group);
            var plural = group.Select(n => n.Name).Distinct().Count() > 1;
            var verb = plural ? "are" : "is";
            switch (group.Key)
            {
                case NoteRole.Root:
                    parts.Add($"The {names} {verb} the root of {chordName}, so it tells everyone which chord this is.");
                    break;
                case NoteRole.Third:
                    parts.Add($"The {names} {verb} the 3rd, the note that decides whether the chord sounds {(minorChord ? "minor (darker)" : "major (brighter)")}.");
                    break;
                case NoteRole.Fifth:
                    parts.Add($"The {names} {verb} its 5th, a note that always sounds safe on this chord.");
                    break;
                case NoteRole.Seventh:
                    parts.Add($"The {names} {verb} the 7th, which adds a bluesy or jazzy colour.");
                    break;
                case NoteRole.Octave:
                    parts.Add($"The higher {names} {verb} the root again, one octave up: it adds bounce without changing the harmony.");
                    break;
                case NoteRole.Approach:
                    parts.Add(ApproachSentence(group.First(), next, flats));
                    break;
                default:
                    if (group.All(n => Mod(n.Pitch - chord.Root) == 9))
                    {
                        parts.Add($"The {names} {verb} the 6th. {(plural ? "They are" : "It is")} not part of the chord, but {(plural ? "sit" : "sits")} in the scale and keep{(plural ? "" : "s")} a walking line moving towards the next chord.");
                        break;
                    }

                    parts.Add($"The {names} {verb} not part of the chord. {(plural ? "They are passing notes that connect" : "It is a passing note that connects")} the notes around {(plural ? "them" : "it")} and {(plural ? "sound" : "sounds")} smooth because {(plural ? "they last" : "it lasts")} only a moment.");
                    break;
            }
        }

        return string.Join(" ", parts);
    }

    private static string ApproachSentence(LineNote note, Chord? next, bool flats)
    {
        if (next is not { } following)
        {
            return $"The {note.Name} is an approach note: it leans towards the next chord.";
        }

        var nextRoot = NoteSpelling.NameFor(following.Root, flats);
        var up = Mod(following.Root - note.Pitch);
        var steps = Math.Min(up, 12 - up);
        var below = up <= 6;
        var size = steps == 1 ? "one half step" : "a whole step";
        return $"The {note.Name} sits {size} {(below ? "below" : "above")} the next chord's root ({nextRoot}) and {(below ? "steps up" : "walks down")} into it. That is called an approach note: it pulls the ear towards the next bar.";
    }

    private static string Tip(IReadOnlyList<LineNote> notes, Chord chord, Chord? next, bool flats)
    {
        var root = NoteSpelling.NameFor(chord.Root, flats);
        var nextRoot = next is { } following ? NoteSpelling.NameFor(following.Root, flats) : null;
        var fifth = NoteSpelling.NameFor(chord.Root + 7, flats);
        var text = $"Mute the bass machine and play just the root ({root}{(nextRoot is null ? "" : $", then {nextRoot} in the next bar")}).";
        text += notes.Any(n => n.Role == NoteRole.Fifth)
            ? $" Then add the fifth ({fifth}). You have learned the line."
            : " Then add the other notes from the line, one at a time.";
        return text;
    }
}
