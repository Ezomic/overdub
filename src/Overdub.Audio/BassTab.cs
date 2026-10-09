using System.Text;

namespace Overdub.Audio;

public static class BassTab
{
    private static readonly int[] Open = [43, 38, 33, 28];
    private static readonly string[] Names = ["G", "D", "A", "E"];
    private const int Slot = 2;
    private const int StepsPerBar = 16;

    public static (int String, int Fret) Position(int pitch)
    {
        for (var s = Open.Length - 1; s >= 0; s--)
        {
            var fret = pitch - Open[s];
            if (fret is >= 0 and <= 5)
            {
                return (s, fret);
            }
        }

        var best = (String: Open.Length - 1, Fret: int.MaxValue);
        for (var s = 0; s < Open.Length; s++)
        {
            var fret = pitch - Open[s];
            if (fret >= 0 && fret < best.Fret)
            {
                best = (s, fret);
            }
        }

        return best.Fret == int.MaxValue ? (Open.Length - 1, 0) : best;
    }

    public static string Render(ChordPattern pattern, int barsPerLine = 2)
    {
        const int rate = 1920;
        const double bpm = 120;
        var sixteenth = rate * 60.0 / bpm / 4;
        var notes = new MidiClip(pattern.ToEvents(rate, bpm, 4)).NoteData();
        return RenderNotes(notes, 0, sixteenth, pattern.Bars, b => pattern[b].Name, barsPerLine);
    }

    public static string RenderNotes(IReadOnlyList<MidiNoteData> notes, long origin, double sixteenthSamples, int bars, Func<int, string> chordAt, int barsPerLine = 2)
    {
        bars = Math.Max(1, bars);
        var grid = Enumerable.Range(0, Open.Length).Select(_ => Enumerable.Range(0, bars).Select(_ => Enumerable.Repeat('-', StepsPerBar * Slot).ToArray()).ToArray()).ToArray();
        foreach (var note in notes)
        {
            var step = (int)Math.Round((note.Start - origin) / sixteenthSamples);
            if (step < 0 || step >= bars * StepsPerBar)
            {
                continue;
            }

            var bar = step / StepsPerBar;
            var slot = step % StepsPerBar;
            var (s, fret) = Position(note.Pitch);
            var text = note.Velocity < 50 ? "x" : fret.ToString();
            for (var i = 0; i < text.Length && (slot * Slot) + i < StepsPerBar * Slot; i++)
            {
                grid[s][bar][(slot * Slot) + i] = text[i];
            }
        }

        var builder = new StringBuilder();
        for (var first = 0; first < bars; first += barsPerLine)
        {
            var last = Math.Min(bars, first + barsPerLine);
            builder.Append("  ");
            for (var b = first; b < last; b++)
            {
                builder.Append(chordAt(b).PadRight((StepsPerBar * Slot) + 1));
            }

            builder.AppendLine();
            builder.Append("  ");
            for (var b = first; b < last; b++)
            {
                for (var slot = 0; slot < StepsPerBar; slot++)
                {
                    builder.Append(slot % 4 == 0 ? (slot / 4 + 1).ToString() : slot % 4 == 2 ? "&" : " ").Append(' ');
                }

                builder.Append(' ');
            }

            builder.AppendLine();
            for (var s = 0; s < Open.Length; s++)
            {
                builder.Append(Names[s]).Append('|');
                for (var b = first; b < last; b++)
                {
                    builder.Append(grid[s][b]).Append('|');
                }

                builder.AppendLine();
            }

            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }
}
