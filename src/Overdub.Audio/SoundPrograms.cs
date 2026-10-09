using System.Text.RegularExpressions;

namespace Overdub.Audio;

public sealed record SoundProgram(string Pack, string PackFolder, string Kind, string Description, string Title, string Name, string Path)
{
    public bool IsBass => Kind.Contains("bass", StringComparison.OrdinalIgnoreCase) || Pack.Contains("bass", StringComparison.OrdinalIgnoreCase);
}

public static partial class SoundPrograms
{
    private static readonly object Gate = new();
    private static IReadOnlyList<SoundProgram> _cached = [];
    private static DateTime _scanned = DateTime.MinValue;

    public static IReadOnlyList<SoundProgram> Discover()
    {
        lock (Gate)
        {
            if ((DateTime.UtcNow - _scanned).TotalSeconds < 3)
            {
                return _cached;
            }

            _cached = Scan();
            _scanned = DateTime.UtcNow;
            return _cached;
        }
    }

    public static void Invalidate()
    {
        lock (Gate)
        {
            _scanned = DateTime.MinValue;
        }
    }

    public static string? FindPath(string? name)
    {
        if (name is null)
        {
            return null;
        }

        var programs = Discover();
        return programs.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))?.Path
            ?? programs.FirstOrDefault(p => string.Equals(LegacyName(p.Path), name, StringComparison.OrdinalIgnoreCase))?.Path;
    }

    public static string DisplayTitle(string? presetName)
    {
        if (presetName is null)
        {
            return "";
        }

        var program = Discover().FirstOrDefault(p => string.Equals(p.Name, presetName, StringComparison.OrdinalIgnoreCase));
        return program is null ? presetName : $"{program.Pack}: {program.Title}";
    }

    private static string LegacyName(string path)
    {
        var folder = new DirectoryInfo(System.IO.Path.GetDirectoryName(path)!).Name;
        return $"{folder}: {System.IO.Path.GetFileNameWithoutExtension(path).Replace('_', ' ')} (sampled)";
    }

    private static List<SoundProgram> Scan()
    {
        var root = SfzInstrument.Folder;
        var result = new List<SoundProgram>();
        if (!Directory.Exists(root))
        {
            return result;
        }

        foreach (var packDir in Directory.EnumerateDirectories(root).OrderBy(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var folder = new DirectoryInfo(packDir).Name;
            var files = Directory.EnumerateFiles(packDir, "*.sfz", SearchOption.AllDirectories).ToList();
            var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                string text;
                try
                {
                    text = File.ReadAllText(file);
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (Match match in IncludePattern().Matches(text))
                {
                    included.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file)!, match.Groups[1].Value.Replace('/', System.IO.Path.DirectorySeparatorChar))));
                }
            }

            var pack = SoundCatalog.Packs.FirstOrDefault(p => string.Equals(p.Folder, folder, StringComparison.OrdinalIgnoreCase));
            var packName = pack?.Name ?? folder.Replace('_', ' ');
            var kind = pack?.Kind ?? "Other";
            var description = pack?.Description ?? "Installed by hand into Documents\\Overdub\\Instruments.";
            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files.Where(f => !included.Contains(System.IO.Path.GetFullPath(f))).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var title = CleanTitle(System.IO.Path.GetFileNameWithoutExtension(file), folder);
                var unique = title;
                for (var n = 2; !titles.Add(unique); n++)
                {
                    unique = $"{title} {n}";
                }

                result.Add(new SoundProgram(packName, folder, kind, description, unique, $"{packName}: {unique} (sampled)", file));
            }
        }

        return result;
    }

    private static string CleanTitle(string stem, string folder)
    {
        var key = new string(folder.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        var words = LeadingNumber().Replace(stem, "").Split(['_', '-', ' '], StringSplitOptions.RemoveEmptyEntries).ToList();
        var prefix = "";
        var consumed = 0;
        for (var i = 0; i < words.Count - 1 && key.StartsWith(prefix + words[i].ToLowerInvariant(), StringComparison.Ordinal); i++)
        {
            prefix += words[i].ToLowerInvariant();
            if (GenericSuffixes.Contains(key[prefix.Length..]))
            {
                consumed = i + 1;
            }
        }

        words.RemoveRange(0, consumed);
        if (words.Count == 1 && consumed == 0 && words[0].Length >= 4 && GenericSuffixes.Contains(key[Math.Min(key.Length, words[0].Length)..]) && key.StartsWith(words[0].ToLowerInvariant(), StringComparison.Ordinal))
        {
            words[0] = "Main";
        }

        var text = string.Join(' ', words);
        return text.Length == 0 ? "Main" : char.ToUpperInvariant(text[0]) + text[1..];
    }

    private static readonly HashSet<string> GenericSuffixes = ["", "guitar", "guitars", "bass", "basses", "doublebass", "eub"];

    [GeneratedRegex("#include\\s+\"([^\"]+)\"")]
    private static partial Regex IncludePattern();

    [GeneratedRegex("^\\d+[-_ ]*")]
    private static partial Regex LeadingNumber();
}
