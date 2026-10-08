using System.Text.Json;

namespace Overdub.Audio;

public sealed record ClipData(string File, long StartSample, long? Offset = null, long? Length = null, int? Lane = null);

public sealed record CompData(long Start, long End, int Lane);

public sealed record EffectData(bool Enabled, double[] Values);

public sealed record MidiEventData(long At, int Note, int Velocity, int Kind = 0, int Value = 0);

public sealed record MidiClipData(List<MidiEventData> Events);

public sealed record TrackData(string Name, bool Mute, bool Solo, float Gain, List<ClipData> Clips, List<MidiClipData>? MidiClips = null, float Pan = 0f, int? Input = null, bool? IsMidi = null, int? Color = null, string? Preset = null, bool? IsBacking = null, Dictionary<string, EffectData>? Effects = null, List<CompData>? Comp = null, PluginData? Plugin = null);

public sealed record PluginData(string Path, string ClassId, string Name, string Vendor, string Category, string SubCategories, bool Enabled, string? Component, string? Controller);

public sealed record ProjectData(int Version, int SampleRate, double Bpm, List<TrackData> Tracks, int BeatsPerBar = 4, int BeatUnit = 4);

public static class ProjectFile
{
    public const string FileName = "project.overdub.json";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Write(string path, ProjectData data) =>
        File.WriteAllText(path, JsonSerializer.Serialize(data, Options));

    public static ProjectData Read(string path)
    {
        var data = JsonSerializer.Deserialize<ProjectData>(File.ReadAllText(path), Options)
            ?? throw new InvalidDataException("The project file is empty.");
        return data.Version == 1 ? data : throw new InvalidDataException($"Project version {data.Version} is not supported.");
    }
}
