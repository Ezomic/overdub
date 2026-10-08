namespace Overdub.Audio;

public sealed class Track(string name, int? input)
{
    private bool _mute;
    private bool _solo;
    private float _gain = 1f;

    public string Name { get; } = name;
    public int? Input { get; } = input;
    public bool Armed { get; set; }
    public List<Clip> Clips { get; } = [];

    public bool Mute
    {
        get => _mute;
        set
        {
            _mute = value;
            Apply(c => c.Playback.Mute = value);
        }
    }

    public bool Solo
    {
        get => _solo;
        set
        {
            _solo = value;
            Apply(c => c.Playback.Solo = value);
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

    public void AddClip(Clip clip)
    {
        clip.Playback.Mute = _mute;
        clip.Playback.Solo = _solo;
        clip.Playback.Gain = _gain;
        Clips.Add(clip);
    }

    private void Apply(Action<Clip> action)
    {
        foreach (var clip in Clips)
        {
            action(clip);
        }
    }
}
