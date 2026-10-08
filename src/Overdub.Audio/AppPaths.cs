namespace Overdub.Audio;

public static class AppPaths
{
    public static string SettingsDirectory =>
        Environment.GetEnvironmentVariable("OVERDUB_SETTINGS_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Overdub");
}
