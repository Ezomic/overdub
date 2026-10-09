using Overdub.Audio;

namespace Overdub.App;

public sealed class PianoRollModel
{
    public const int LowestPitch = 21;
    public const int HighestPitch = 108;
    public const byte DefaultVelocity = 100;

    private List<MidiNoteData> _snapshot = [];

    public PianoRollModel(IEnumerable<MidiNoteData> notes, double samplesPerBeat, int beatsPerBar, int beatUnit)
    {
        Notes = notes.ToList();
        SamplesPerBeat = samplesPerBeat;
        BeatsPerBar = beatsPerBar;
        BeatUnit = beatUnit;
        Division = 16;
    }

    public List<MidiNoteData> Notes { get; private set; }
    public HashSet<int> Selected { get; } = [];
    public double SamplesPerBeat { get; }
    public int BeatsPerBar { get; }
    public int BeatUnit { get; }
    public int Division { get; set; }
    public int? Key { get; set; }
    public MelodyScale Scale { get; set; } = MelodyScale.Major;
    public bool SnapToScale { get; set; }

    public bool InScale(int pitch) => Key is { } key && MelodyGenerator.ScaleOffsets(Scale).Contains((((pitch - key) % 12) + 12) % 12);

    public bool IsRoot(int pitch) => Key is { } key && (((pitch - key) % 12) + 12) % 12 == 0;

    public int ToScale(int pitch)
    {
        if (!SnapToScale || Key is null || InScale(pitch))
        {
            return pitch;
        }

        for (var distance = 1; distance <= 6; distance++)
        {
            if (InScale(pitch - distance))
            {
                return pitch - distance;
            }

            if (InScale(pitch + distance))
            {
                return pitch + distance;
            }
        }

        return pitch;
    }

    public long GridSamples => Math.Max(1, (long)(SamplesPerBeat * BeatUnit / Division));

    public long Snap(long sample) => (long)(Math.Round((double)sample / GridSamples) * GridSamples);

    public int NoteAt(long sample, int pitch)
    {
        for (var i = Notes.Count - 1; i >= 0; i--)
        {
            var note = Notes[i];
            if (note.Pitch == pitch && sample >= note.Start && sample < note.End)
            {
                return i;
            }
        }

        return -1;
    }

    public void Select(int index, bool additive)
    {
        if (!additive)
        {
            Selected.Clear();
        }

        if (index < 0)
        {
            return;
        }

        if (!Selected.Add(index) && additive)
        {
            Selected.Remove(index);
        }
    }

    public void SelectAll()
    {
        Selected.Clear();
        for (var i = 0; i < Notes.Count; i++)
        {
            Selected.Add(i);
        }
    }

    public int Add(long start, int pitch)
    {
        var snapped = Math.Max(0, Snap(start));
        Notes.Add(new MidiNoteData(snapped, snapped + GridSamples, (byte)Math.Clamp(ToScale(pitch), LowestPitch, HighestPitch), DefaultVelocity));
        Selected.Clear();
        Selected.Add(Notes.Count - 1);
        return Notes.Count - 1;
    }

    public void BeginGesture() => _snapshot = [.. Notes];

    public void MoveSelected(long deltaSamples, int deltaPitch)
    {
        var snappedDelta = (long)(Math.Round((double)deltaSamples / GridSamples) * GridSamples);
        var earliest = Selected.Count == 0 ? 0 : Selected.Min(i => _snapshot[i].Start);
        snappedDelta = Math.Max(snappedDelta, -earliest);
        foreach (var index in Selected)
        {
            var note = _snapshot[index];
            var pitch = (byte)Math.Clamp(ToScale(note.Pitch + deltaPitch), LowestPitch, HighestPitch);
            Notes[index] = note with { Start = note.Start + snappedDelta, End = note.End + snappedDelta, Pitch = pitch };
        }
    }

    public void ResizeSelected(long deltaSamples)
    {
        foreach (var index in Selected)
        {
            var note = _snapshot[index];
            var end = Snap(note.End + deltaSamples);
            Notes[index] = note with { End = Math.Max(note.Start + GridSamples, end) };
        }
    }

    public void SetVelocity(IEnumerable<int> indexes, byte velocity)
    {
        foreach (var index in indexes)
        {
            Notes[index] = Notes[index] with { Velocity = Math.Max((byte)1, velocity) };
        }
    }

    public bool EndGesture() => !_snapshot.SequenceEqual(Notes);

    public void DeleteSelected()
    {
        foreach (var index in Selected.OrderByDescending(i => i))
        {
            Notes.RemoveAt(index);
        }

        Selected.Clear();
    }

    public void Delete(int index)
    {
        Notes.RemoveAt(index);
        Selected.Clear();
    }

    public void Replace(IEnumerable<MidiNoteData> notes)
    {
        Notes = notes.ToList();
        Selected.RemoveWhere(i => i >= Notes.Count);
    }
}
