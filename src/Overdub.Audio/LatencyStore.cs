using System.Text.Json;

namespace Overdub.Audio;

public static class LatencyStore
{
    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Overdub", "latency.json");

    public static int? Load(string key) => ReadAll().TryGetValue(key, out var samples) ? samples : null;

    public static void Save(string key, int samples)
    {
        var all = ReadAll();
        all[key] = samples;
        WriteAll(all);
    }

    public static void Remove(string key)
    {
        var all = ReadAll();
        if (all.Remove(key))
        {
            WriteAll(all);
        }
    }

    private static Dictionary<string, int> ReadAll()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void WriteAll(Dictionary<string, int> all)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(all));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
