using System.Text.Json;

namespace Overdub.Audio;

public static class PreviewSettings
{
    private sealed record Data(float Volume, bool Auto);

    private static string FilePath => Path.Combine(AppPaths.SettingsDirectory, "preview.json");
    private static Data? _data;

    public static float Volume
    {
        get => Current.Volume;
        set => Store(Current with { Volume = Math.Clamp(value, 0f, 1f) });
    }

    public static bool Auto
    {
        get => Current.Auto;
        set => Store(Current with { Auto = value });
    }

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
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                _data = null;
            }

            return _data ??= new Data(0.8f, false);
        }
    }

    private static void Store(Data data)
    {
        _data = data;
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
        }
        catch (IOException)
        {
        }
    }
}
