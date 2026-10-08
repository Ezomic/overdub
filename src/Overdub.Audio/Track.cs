namespace Overdub.Audio;

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
    public bool IsMidi => Input is null;
    public List<Clip> Clips { get; } = [];
    public List<MidiClip> MidiClips { get; } = [];

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
