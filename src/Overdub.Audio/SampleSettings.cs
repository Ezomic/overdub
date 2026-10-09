using System.Text.Json;

namespace Overdub.Audio;

public static class SampleSettings
{
    public static readonly string[] QualityNames = ["Full", "Lighter", "Lightest"];

    private sealed record Data(int Quality);

    private static string FilePath => Path.Combine(AppPaths.SettingsDirectory, "samples.json");
    private static Data? _data;

    public static int Quality
    {
        get => Math.Clamp(Current.Quality, 0, QualityNames.Length - 1);
        set
        {
            _data = new Data(Math.Clamp(value, 0, QualityNames.Length - 1));
            try
            {
                Directory.CreateDirectory(AppPaths.SettingsDirectory);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(_data));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    public static int LayerStride => Quality + 1;

    public static string Describe(int quality) => quality switch
    {
        1 => "Keeps every second velocity layer: half the load time and memory on big pianos, hardly audible.",
        2 => "Keeps every third velocity layer: a third of the load time and memory. Fine for sketching.",
        _ => "Loads every velocity layer a pack has. Big pianos take long to load and use a lot of memory.",
    };

    private static Data Current
    {
        get
        {
            if (_data is not null)
            {
                return _data;
            }

            try
            {
                _data = File.Exists(FilePath) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath)) : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _data = null;
            }

            return _data ??= new Data(0);
        }
    }
}
