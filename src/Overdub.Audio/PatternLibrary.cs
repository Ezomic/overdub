using System.Text.Json;

namespace Overdub.Audio;

public sealed record LibraryEntry(string Name, string Path, string Kind);

public sealed record LibraryFile(string Kind, string Name, PatternData? Drum = null, ChordPatternData? Chord = null);

public static class PatternLibrary
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static string Folder => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Overdub", "Patterns");

    public static string DrumKind => "drums";

    public static string ChordKind(MachineRole role) => role.ToString().ToLowerInvariant();

    public static IReadOnlyList<LibraryEntry> List(string kind)
    {
        if (!Directory.Exists(Folder))
        {
            return [];
        }

        var entries = new List<LibraryEntry>();
        foreach (var file in Directory.EnumerateFiles(Folder, "*.pattern.json"))
        {
            try
            {
                var data = JsonSerializer.Deserialize<LibraryFile>(File.ReadAllText(file), Options);
                if (data is not null && data.Kind == kind)
                {
                    entries.Add(new LibraryEntry(data.Name, file, kind));
                }
            }
            catch (JsonException)
            {
            }
        }

        return entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string Save(DrumPattern pattern, string name) =>
        Write(name, new LibraryFile(DrumKind, name, new PatternData(pattern.Id, name, pattern.Bars, pattern.Encode().ToList(), pattern.Feel, pattern.Swing, pattern.Details().ToList())));

    public static string Save(ChordPattern pattern, string name) =>
        Write(name, new LibraryFile(ChordKind(pattern.Role), name, Chord: new ChordPatternData(pattern.Id, name, pattern.Bars, (int)pattern.Style, pattern.Encode(), null, pattern.Feel)));

    public static DrumPattern LoadDrum(string path)
    {
        var data = Read(path).Drum ?? throw new InvalidDataException("This file does not hold a drum pattern.");
        var pattern = DrumPattern.Decode(Guid.NewGuid().ToString("N")[..8], data.Name, data.Bars, data.Lanes);
        pattern.Feel = data.Feel;
        pattern.Swing = Math.Clamp(data.Swing, 0, DrumPattern.SwingNames.Length - 1);
        pattern.ApplyDetails(data.Details);
        return pattern;
    }

    public static ChordPattern LoadChord(string path, MachineRole role)
    {
        var data = Read(path).Chord ?? throw new InvalidDataException("This file does not hold a chord pattern.");
        var pattern = ChordPattern.Decode(Guid.NewGuid().ToString("N")[..8], data.Name, role, data.Bars, (ChordStyle)data.Style, data.Chords);
        pattern.Feel = data.Feel;
        return pattern;
    }

    private static LibraryFile Read(string path) => JsonSerializer.Deserialize<LibraryFile>(File.ReadAllText(path), Options) ?? throw new InvalidDataException("Could not read this pattern file.");

    private static string Write(string name, LibraryFile file)
    {
        Directory.CreateDirectory(Folder);
        var safe = string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim();
        var path = System.IO.Path.Combine(Folder, $"{file.Kind}-{(safe.Length == 0 ? "pattern" : safe)}.pattern.json");
        File.WriteAllText(path, JsonSerializer.Serialize(file, Options));
        return path;
    }
}
