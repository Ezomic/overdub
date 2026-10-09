using System.Text.RegularExpressions;

namespace Overdub.Audio;

public sealed record SoundProgram(string Pack, string PackFolder, string Kind, string Description, string Title, string Name, string Path)
{
    public bool IsBass => Kind.Contains("bass", StringComparison.OrdinalIgnoreCase) || Pack.Contains("bass", StringComparison.OrdinalIgnoreCase);

    public bool IsDrumKit => Kind == "Drums" || Pack.Contains("drum", StringComparison.OrdinalIgnoreCase) || Pack.Contains("kit", StringComparison.OrdinalIgnoreCase);

    public bool IsGuitar => Kind.Contains("guitar", StringComparison.OrdinalIgnoreCase) || Pack.Contains("guitar", StringComparison.OrdinalIgnoreCase);

    public bool Fits(MachineRole? role) => role switch
    {
        MachineRole.Bass => IsBass,
        MachineRole.Guitar => !IsBass && !IsDrumKit,
        MachineRole.Lead => !IsBass && !IsDrumKit,
        _ => true,
    };
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

    private static readonly string[] PreferredGuitar = ["Emilyguitar: Clean", "Black and Green Guitars: Green twang", "Shinyguitar: Acoustic"];
    private static readonly string[] PreferredLead = ["Emilyguitar: Clean", "Black and Green Guitars: Black twang", "Shinyguitar: Acoustic"];
    private static readonly string[] PreferredBass = ["Swagbass: Clean", "Growlybass: Clean", "Fashionbass: Clean", "Pastabass: Spaghetti", "Big Little Bass: Pluck"];

    public static string? Preferred(MachineRole role)
    {
        var wanted = role switch
        {
            MachineRole.Bass => PreferredBass,
            MachineRole.Lead => PreferredLead,
            _ => PreferredGuitar,
        };
        var programs = Discover();
        foreach (var candidate in wanted)
        {
            var hit = programs.FirstOrDefault(p => string.Equals($"{p.Pack}: {p.Title}", candidate, StringComparison.OrdinalIgnoreCase));
            if (hit is not null)
            {
                return hit.Name;
            }
        }

        return null;
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
            var texts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

                texts[file] = text;
                foreach (Match match in IncludePattern().Matches(text))
                {
                    included.Add(System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file)!, match.Groups[1].Value.Replace('/', System.IO.Path.DirectorySeparatorChar))));
                }
            }

            var pack = SoundCatalog.Packs.FirstOrDefault(p => string.Equals(p.Folder, folder, StringComparison.OrdinalIgnoreCase));
            var packName = pack?.Name ?? folder.Replace('_', ' ');
            var kind = pack?.Kind ?? "Other";
            var description = pack?.Description ?? "Your own pack in Documents\\Overdub\\Instruments, made from a recording or copied in by hand.";
            var titles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool Standalone(string f)
            {
                try
                {
                    return !included.Contains(System.IO.Path.GetFullPath(f)) && texts.TryGetValue(f, out var text) && IsStandalone(f, text);
                }
                catch (Exception ex) when (ex is IOException or ArgumentException or RegexMatchTimeoutException)
                {
                    return false;
                }
            }

            var programsDir = System.IO.Path.Combine(packDir, "Programs");
            var candidates = Directory.Exists(programsDir)
                ? files.Where(f => string.Equals(System.IO.Path.GetDirectoryName(f), programsDir, StringComparison.OrdinalIgnoreCase)).ToList()
                : files;
            foreach (var file in candidates.Where(Standalone).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
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

    private static bool IsStandalone(string file, string text)
    {
        var defines = new Dictionary<string, string>(StringComparer.Ordinal);
        string Resolve(string value) => defines.OrderByDescending(d => d.Key.Length).Aggregate(value, (v, d) => v.Replace(d.Key, d.Value, StringComparison.Ordinal));
        var defaultPath = "";
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine;
            var comment = line.IndexOf("//", StringComparison.Ordinal);
            if (comment >= 0)
            {
                line = line[..comment];
            }

            var define = DefinePattern().Match(line);
            if (define.Success)
            {
                defines[define.Groups[1].Value] = define.Groups[2].Value.Trim();
                continue;
            }

            var path = DefaultPathPattern().Match(line);
            if (path.Success)
            {
                defaultPath = Resolve(path.Groups[1].Value);
            }

            var sample = SamplePattern().Match(line);
            if (sample.Success)
            {
                return SfzInstrument.ResolveSample(System.IO.Path.GetDirectoryName(file)!, defaultPath, Resolve(sample.Groups[1].Value)) is not null;
            }
        }

        return text.Contains("#include", StringComparison.Ordinal);
    }

    private static string CleanTitle(string stem, string folder)
    {
        var key = new string(folder.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        if (string.Equals(new string(stem.Where(char.IsLetterOrDigit).ToArray()), key, StringComparison.OrdinalIgnoreCase))
        {
            return "Main";
        }

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

    [GeneratedRegex("#define\\s+(\\$\\w+)\\s+(\\S+)")]
    private static partial Regex DefinePattern();

    [GeneratedRegex("(?m)(?:^|[\\s>])sample=(.+?)(?=\\s+\\w+=|\\s*$)")]
    private static partial Regex SamplePattern();

    [GeneratedRegex("(?m)(?:^|[\\s>])default_path=(\\S+)")]
    private static partial Regex DefaultPathPattern();

    [GeneratedRegex("^\\d+[-_ ]*")]
    private static partial Regex LeadingNumber();
}
