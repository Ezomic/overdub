namespace Overdub.Audio;

public sealed class MachineLane
{
    public Synth Synth { get; } = new();
    public MidiSequencer Sequencer { get; } = new();
    public MidiClip[] Clips { get; set; } = [];
    public float Gain { get; set; } = 1f;
    public float Pan { get; set; }
}

public sealed record MachineMix(IReadOnlyList<MidiClip> Clips, float Gain, float Pan, string Preset);
