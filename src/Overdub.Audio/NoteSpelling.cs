using System.Text.Json;

namespace Overdub.Audio;

public static class NoteSpelling
{
    public static readonly string[] ModeNames = ["Auto", "Sharps", "Flats"];

    private static readonly string[] Flats = ["C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B"];
    private static readonly HashSet<int> FlatMajors = [5, 10, 3, 8, 1, 6];
    private static readonly HashSet<int> FlatMinors = [2, 7, 0, 5, 10, 3];

    private sealed record Data(int Mode, bool Numerals = false);

    private static string FilePath => Path.Combine(AppPaths.SettingsDirectory, "spelling.json");
    private static int? _mode;
    private static bool? _numerals;
    private static long _checkedAt;
    private static bool _autoFlats;
    private static SongKey? _key;

    public static Func<SongKey?>? KeyProvider { get; set; }

    public static int Mode
    {
        get
        {
            if (_mode is { } cached)
            {
                return cached;
            }

            try
            {
                _mode = File.Exists(FilePath) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath))?.Mode : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _mode = null;
            }

            _mode = Math.Clamp(_mode ?? 0, 0, ModeNames.Length - 1);
            return _mode.Value;
        }
        set
        {
            _mode = Math.Clamp(value, 0, ModeNames.Length - 1);
            Volatile.Write(ref _checkedAt, 0);
            Save();
        }
    }

    public static bool ShowNumerals
    {
        get
        {
            if (_numerals is { } cached)
            {
                return cached;
            }

            try
            {
                _numerals = File.Exists(FilePath) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath))?.Numerals : null;
            }
            catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
            {
                _numerals = null;
            }

            _numerals ??= false;
            return _numerals.Value;
        }
        set
        {
            _numerals = value;
            Save();
        }
    }

    public static SongKey? CurrentKey
    {
        get
        {
            Refresh();
            return _key;
        }
    }

    public static string ChordName(Chord chord, string plain)
    {
        if (ShowNumerals && CurrentKey is { } key)
        {
            return Theory.Numeral(chord, key.Root, key.Minor);
        }

        return plain;
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.SettingsDirectory);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(new Data(Mode, ShowNumerals)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private static void Refresh()
    {
        var now = Environment.TickCount64;
        if (now - Volatile.Read(ref _checkedAt) <= 750)
        {
            return;
        }

        Volatile.Write(ref _checkedAt, now);
        try
        {
            _key = KeyProvider?.Invoke();
            _autoFlats = _key is { } key && KeyUsesFlats(key.Root, key.Minor);
        }
        catch (InvalidOperationException)
        {
        }
    }

    public static bool KeyUsesFlats(int root, bool minor) => (minor ? FlatMinors : FlatMajors).Contains(((root % 12) + 12) % 12);

    public static bool UseFlats
    {
        get
        {
            var mode = Mode;
            if (mode != 0)
            {
                return mode == 2;
            }

            Refresh();
            return _autoFlats;
        }
    }

    public static string Name(int pitchClass) => NameFor(pitchClass, UseFlats);

    public static string NameFor(int pitchClass, bool flatKey)
    {
        var index = ((pitchClass % 12) + 12) % 12;
        var flats = Mode switch { 1 => false, 2 => true, _ => flatKey };
        return flats ? Flats[index] : Chord.Roots[index];
    }

    public static string KeyName(SongKey key) => $"{NameFor(key.Root, KeyUsesFlats(key.Root, key.Minor))} {(key.Minor ? "minor" : "major")}";
}
