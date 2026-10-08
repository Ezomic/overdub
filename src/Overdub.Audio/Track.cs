namespace Overdub.Audio;

public readonly record struct CompSegment(long Start, long End, int Lane);

public sealed class Track(string name, int? input)
{
    private bool _mute;
    private bool _solo;
    private float _gain = 1f;
    private float _pan;

    public string Name { get; set; } = name;
    public string Id { get; } = Guid.NewGuid().ToString("N")[..8];
    public int ColorIndex { get; set; }
    public int? Input { get; } = input;
    public bool Armed { get; set; }
    public bool IsBacking { get; init; }
    public bool IsDrums { get; init; }
    public bool IsMidi => Input is null && !IsBacking;
    public List<DrumPattern> Patterns { get; } = [];
    public string Preset { get; set; } = "Lead";
    public EffectChain Effects { get; } = new();
    public Vst3.PluginSlot Instrument { get; } = new();
    public Vst3.PluginSlot PluginSlot => IsMidi ? Instrument : Effects.Plugin;
    public EffectChain PlaybackFx { get; } = new();
    public EffectChain LiveFx { get; } = new();
    public List<Clip> Clips { get; } = [];
    public List<MidiClip> MidiClips { get; } = [];
    public List<CompSegment> Comp { get; } = [];

    public int LaneCount => Clips.Count == 0 ? 1 : Clips.Max(c => c.Lane) + 1;

    public void SetComp(int lane, long start, long end)
    {
        var next = new List<CompSegment>();
        foreach (var segment in Comp)
        {
            if (segment.End <= start || segment.Start >= end)
            {
                next.Add(segment);
                continue;
            }

            if (segment.Start < start)
            {
                next.Add(segment with { End = start });
            }

            if (segment.End > end)
            {
                next.Add(segment with { Start = end });
            }
        }

        next.Add(new CompSegment(start, end, lane));
        next.Sort((a, b) => a.Start.CompareTo(b.Start));
        Comp.Clear();
        foreach (var segment in next)
        {
            if (Comp.Count > 0 && Comp[^1].Lane == segment.Lane && Comp[^1].End == segment.Start)
            {
                Comp[^1] = Comp[^1] with { End = segment.End };
            }
            else
            {
                Comp.Add(segment);
            }
        }
    }

    public IReadOnlyList<CompSegment> ActiveRanges()
    {
        var result = new List<CompSegment>();
        if (Clips.All(c => c.Lane == 0))
        {
            return result;
        }

        var edges = new SortedSet<long>();
        foreach (var clip in Clips)
        {
            edges.Add(clip.StartSample);
            edges.Add(clip.EndSample);
        }

        foreach (var segment in Comp)
        {
            edges.Add(segment.Start);
            edges.Add(segment.End);
        }

        var points = edges.ToList();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            var covering = Clips.Where(c => c.StartSample <= from && c.EndSample >= to).ToList();
            if (covering.Count == 0)
            {
                continue;
            }

            var chosen = Comp.Where(s => s.Start <= from && s.End >= to).Select(s => (int?)s.Lane).FirstOrDefault();
            var lane = chosen ?? covering.Max(c => c.Lane);
            if (!covering.Any(c => c.Lane == lane))
            {
                continue;
            }

            if (result.Count > 0 && result[^1].Lane == lane && result[^1].End == from)
            {
                result[^1] = result[^1] with { End = to };
            }
            else
            {
                result.Add(new CompSegment(from, to, lane));
            }
        }

        return result;
    }

    public IReadOnlyList<PlaybackTrack> EffectiveClips()
    {
        if (Clips.All(c => c.Lane == 0))
        {
            return Clips.Select(c => c.Playback).ToArray();
        }

        var edges = new SortedSet<long>();
        foreach (var clip in Clips)
        {
            edges.Add(clip.StartSample);
            edges.Add(clip.EndSample);
        }

        foreach (var segment in Comp)
        {
            edges.Add(segment.Start);
            edges.Add(segment.End);
        }

        var points = edges.ToList();
        var views = new List<PlaybackTrack>();
        var open = new Dictionary<Clip, PlaybackTrack>();
        for (var i = 0; i + 1 < points.Count; i++)
        {
            var from = points[i];
            var to = points[i + 1];
            var covering = Clips.Where(c => c.StartSample <= from && c.EndSample >= to).ToList();
            if (covering.Count == 0)
            {
                continue;
            }

            var chosen = Comp.Where(s => s.Start <= from && s.End >= to).Select(s => (int?)s.Lane).FirstOrDefault();
            var lane = chosen ?? covering.Max(c => c.Lane);
            foreach (var clip in covering.Where(c => c.Lane == lane))
            {
                if (open.TryGetValue(clip, out var view) && view.EndSample == from)
                {
                    view.Length += to - from;
                    continue;
                }

                var source = clip.Playback;
                view = new PlaybackTrack(source.Samples, from)
                {
                    Offset = source.Offset + (from - clip.StartSample),
                    Length = to - from,
                    Right = source.Right,
                    Gain = source.Gain,
                    Pan = source.Pan,
                    Mute = source.Mute,
                    Solo = source.Solo,
                };
                views.Add(view);
                open[clip] = view;
            }
        }

        return views;
    }

    public bool Mute
    {
        get => _mute;
        set
        {
            _mute = value;
            Apply(c => c.Playback.Mute = value);
            MidiClips.ForEach(c => c.Mute = value);
        }
    }

    public bool Solo
    {
        get => _solo;
        set
        {
            _solo = value;
            Apply(c => c.Playback.Solo = value);
            MidiClips.ForEach(c => c.Solo = value);
        }
    }

    public float Gain
    {
        get => _gain;
        set
        {
            _gain = value;
            Apply(c => c.Playback.Gain = value);
        }
    }

    public float Pan
    {
        get => _pan;
        set
        {
            _pan = Math.Clamp(value, -1f, 1f);
            Apply(c => c.Playback.Pan = _pan);
        }
    }

    public void AddClip(Clip clip) => InsertClip(Clips.Count, clip);

    public void InsertClip(int index, Clip clip)
    {
        clip.Playback.Mute = _mute;
        clip.Playback.Solo = _solo;
        clip.Playback.Gain = _gain;
        clip.Playback.Pan = _pan;
        Clips.Insert(index, clip);
    }

    public void AddMidiClip(MidiClip clip) => InsertMidiClip(MidiClips.Count, clip);

    public void InsertMidiClip(int index, MidiClip clip)
    {
        clip.Mute = _mute;
        clip.Solo = _solo;
        MidiClips.Insert(index, clip);
    }

    private void Apply(Action<Clip> action)
    {
        foreach (var clip in Clips)
        {
            action(clip);
        }
    }
}
