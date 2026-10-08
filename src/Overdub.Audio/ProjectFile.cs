using System.Text.Json;

namespace Overdub.Audio;

public sealed record ClipData(string File, long StartSample);

public sealed record TrackData(string Name, bool Mute, bool Solo, float Gain, List<ClipData> Clips);

public sealed record ProjectData(int Version, int SampleRate, double Bpm, List<TrackData> Tracks);

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
