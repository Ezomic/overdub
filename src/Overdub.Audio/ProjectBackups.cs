namespace Overdub.Audio;

public sealed record BackupEntry(string Path, DateTime Time, long Size);

public static class ProjectBackups
{
    private const int Keep = 25;
    private static readonly TimeSpan MinimumGap = TimeSpan.FromMinutes(2);

    public static string FolderFor(string projectPath) => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(projectPath)!, "backups");

    public static void Snapshot(string projectPath, bool force = false, DateTime? now = null)
    {
        if (!File.Exists(projectPath))
        {
            return;
        }

        var folder = FolderFor(projectPath);
        var existing = List(projectPath);
        var moment = now ?? DateTime.Now;
        if (!force && existing.Count > 0 && moment - existing[0].Time < MinimumGap)
        {
            return;
        }

        var bytes = File.ReadAllBytes(projectPath);
        if (existing.Count > 0 && File.ReadAllBytes(existing[0].Path).AsSpan().SequenceEqual(bytes))
        {
            return;
        }

        Directory.CreateDirectory(folder);
        File.WriteAllBytes(System.IO.Path.Combine(folder, $"project-{moment:yyyyMMdd-HHmmss-fff}.overdub.json"), bytes);
        foreach (var old in List(projectPath).Skip(Keep))
        {
            File.Delete(old.Path);
        }
    }

    public static IReadOnlyList<BackupEntry> List(string projectPath)
    {
        var folder = FolderFor(projectPath);
        if (!Directory.Exists(folder))
        {
            return [];
        }

        return Directory.EnumerateFiles(folder, "project-*.overdub.json")
            .Select(f => new FileInfo(f))
            .Select(f => new BackupEntry(f.FullName, f.LastWriteTime, f.Length))
            .OrderByDescending(e => e.Path, StringComparer.Ordinal)
            .ToList();
    }

    public static void Restore(string projectPath, BackupEntry backup)
    {
        Snapshot(projectPath, force: true);
        File.Copy(backup.Path, projectPath, true);
    }
}
