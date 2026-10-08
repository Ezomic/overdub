using System.Text.Json;

namespace Overdub.Audio;

public static class RecentProjects
{
    private const int Limit = 10;

    private static string FilePath => System.IO.Path.Combine(AppPaths.SettingsDirectory, "recent.json");

    public static IReadOnlyList<string> Load() => Read().Where(File.Exists).ToList();

    public static void Add(string projectPath)
    {
        var full = System.IO.Path.GetFullPath(projectPath);
        var list = Read().Where(p => !string.Equals(p, full, StringComparison.OrdinalIgnoreCase)).Prepend(full).Take(Limit).ToList();
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(list));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static List<string> Read()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(FilePath)) ?? [] : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }
}
