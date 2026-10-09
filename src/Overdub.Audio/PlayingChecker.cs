namespace Overdub.Audio;

public enum NoteOutcome
{
    OnTime,
    Early,
    Late,
    WrongOctave,
    WrongNote,
    Missed,
}

public sealed record NoteCheck(MidiNoteData Target, NoteOutcome Outcome, double OffsetMs, int? PlayedPitch);

public sealed record CheckReport(IReadOnlyList<NoteCheck> Notes, int Extra);

public static class PlayingChecker
{
    private const double OnTimeMs = 70;
    private const double WindowMs = 260;

    public static CheckReport Compare(IReadOnlyList<MidiNoteData> target, IReadOnlyList<MidiNoteData> played, int sampleRate)
    {
        var used = new HashSet<int>();
        var results = new List<NoteCheck>();
        var toMs = 1000.0 / sampleRate;
        foreach (var t in target.OrderBy(n => n.Start))
        {
            var best = -1;
            var bestScore = double.MaxValue;
            for (var i = 0; i < played.Count; i++)
            {
                var offset = (played[i].Start - t.Start) * toMs;
                if (used.Contains(i) || Math.Abs(offset) > WindowMs)
                {
                    continue;
                }

                var samePitchClass = played[i].Pitch % 12 == t.Pitch % 12;
                var score = Math.Abs(offset) + (samePitchClass ? 0 : 1000);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = i;
                }
            }

            if (best < 0)
            {
                results.Add(new NoteCheck(t, NoteOutcome.Missed, 0, null));
                continue;
            }

            used.Add(best);
            var p = played[best];
            var ms = (p.Start - t.Start) * toMs;
            if (p.Pitch % 12 != t.Pitch % 12)
            {
                results.Add(new NoteCheck(t, NoteOutcome.WrongNote, ms, p.Pitch));
            }
            else if (p.Pitch != t.Pitch)
            {
                results.Add(new NoteCheck(t, NoteOutcome.WrongOctave, ms, p.Pitch));
            }
            else
            {
                results.Add(new NoteCheck(t, ms > OnTimeMs ? NoteOutcome.Late : ms < -OnTimeMs ? NoteOutcome.Early : NoteOutcome.OnTime, ms, p.Pitch));
            }
        }

        return new CheckReport(results, played.Count - used.Count);
    }

    public static string Summarize(CheckReport report, Func<long, int> barOf, Func<int, string> barName, string[] noteNames)
    {
        var notes = report.Notes;
        if (notes.Count == 0)
        {
            return "The bass machine has no notes where your clip is.";
        }

        var good = notes.Count(n => n.Outcome is NoteOutcome.OnTime or NoteOutcome.Early or NoteOutcome.Late or NoteOutcome.WrongOctave);
        var right = notes.Count(n => n.Outcome == NoteOutcome.OnTime);
        var timed = notes.Where(n => n.Outcome is NoteOutcome.OnTime or NoteOutcome.Early or NoteOutcome.Late).ToList();
        var lines = new List<string>
        {
            $"You hit {good} of {notes.Count} notes, and {right} were on time (within {OnTimeMs:0} ms).",
        };
        if (timed.Count > 0)
        {
            var average = timed.Average(n => n.OffsetMs);
            lines.Add(Math.Abs(average) < 15 ? "Your timing is centred on the beat." : $"On average you were {Math.Abs(average):0} ms {(average > 0 ? "late (behind the beat)" : "early (ahead of the beat)")}.");
        }

        var missed = notes.Count(n => n.Outcome == NoteOutcome.Missed);
        var wrong = notes.Count(n => n.Outcome == NoteOutcome.WrongNote);
        var octave = notes.Count(n => n.Outcome == NoteOutcome.WrongOctave);
        lines.Add($"Missed {missed}, wrong note {wrong}, wrong octave {octave}, extra notes {report.Extra}.");
        lines.Add("");
        foreach (var group in notes.GroupBy(n => barOf(n.Target.Start)).OrderBy(g => g.Key))
        {
            var bar = group.ToList();
            var issues = new List<string>();
            foreach (var n in bar.Where(n => n.Outcome != NoteOutcome.OnTime))
            {
                var name = noteNames[n.Target.Pitch % 12];
                issues.Add(n.Outcome switch
                {
                    NoteOutcome.Missed => $"missed {name}",
                    NoteOutcome.WrongNote => $"played {noteNames[n.PlayedPitch!.Value % 12]} instead of {name}",
                    NoteOutcome.WrongOctave => $"{name} in the wrong octave",
                    NoteOutcome.Early => $"{name} {Math.Abs(n.OffsetMs):0} ms early",
                    _ => $"{name} {Math.Abs(n.OffsetMs):0} ms late",
                });
            }

            lines.Add($"Bar {group.Key + 1} {barName(group.Key)}: {bar.Count(n => n.Outcome == NoteOutcome.OnTime)} of {bar.Count} clean" + (issues.Count == 0 ? "." : ". " + string.Join(", ", issues) + "."));
        }

        return string.Join(Environment.NewLine, lines);
    }
}
