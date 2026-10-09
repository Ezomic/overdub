using System.IO.Compression;

namespace Overdub.Audio;

public sealed record SoundPack(string Id, string Name, string Kind, string Description, string Url, int SizeMb, string Folder);

public static class SoundCatalog
{
    public static IReadOnlyList<SoundPack> Packs { get; } =
    [
        new("emilyguitar", "Emilyguitar", "Electric guitar", "An Epiphone Emily electric guitar, played note by note with round robins and string noises. Pick the clean mapping to start.", "https://github.com/sfzinstruments/karoryfer.emilyguitar/releases/download/v1.001/Karoryfer.Emilyguitar.v1.001.zip", 98, "Emilyguitar"),
        new("growlybass", "Growlybass", "Bass", "A growly electric bass in clean, dirty and angry flavours. Good for rock.", "https://github.com/sfzinstruments/karoryfer.growlybass/releases/download/v1.002/Karoryfer.Growlybass.v1.002.zip", 159, "Growlybass"),
        new("swagbass", "Swagbass", "Bass", "A smooth, round electric bass with finger noises. Good for funk and soul.", "https://github.com/sfzinstruments/karoryfer.swagbass/releases/download/v1.001/Karoryfer.Swagbass.v1.001.zip", 137, "Swagbass"),
        new("fashionbass", "Fashionbass", "Bass", "A bright, clear electric bass with a clean and a shifty mapping.", "https://github.com/sfzinstruments/karoryfer.fashionbass/releases/download/v1.001/Karoryfer.Fashionbass.v1.001.zip", 301, "Fashionbass"),
        new("pastabass", "Pastabass", "Bass", "A warm, deep electric bass with several strings sampled separately.", "https://github.com/sfzinstruments/karoryfer.pastabass/releases/download/v1.101/Karoryfer.Pastabass.v1.101.zip", 300, "Pastabass"),
    ];

    public static bool IsInstalled(SoundPack pack, string? root = null) =>
        File.Exists(System.IO.Path.Combine(root ?? SfzInstrument.Folder, ".installed-" + pack.Id)) ||
        Directory.Exists(System.IO.Path.Combine(root ?? SfzInstrument.Folder, pack.Folder)) && Directory.EnumerateFiles(System.IO.Path.Combine(root ?? SfzInstrument.Folder, pack.Folder), "*.sfz", SearchOption.AllDirectories).Any();

    public static async Task InstallAsync(SoundPack pack, IProgress<double>? progress, CancellationToken cancel, string? root = null)
    {
        root ??= SfzInstrument.Folder;
        Directory.CreateDirectory(root);
        var drive = new DriveInfo(System.IO.Path.GetPathRoot(System.IO.Path.GetFullPath(root))!);
        var needed = pack.SizeMb * 2L * 1024 * 1024;
        if (drive.AvailableFreeSpace < needed)
        {
            throw new IOException($"Not enough free space on {drive.Name}. {pack.Name} needs about {pack.SizeMb * 2} MB while installing.");
        }

        var work = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "overdub-sounds-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(work);
        try
        {
            var zip = System.IO.Path.Combine(work, pack.Id + ".zip");
            using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) })
            using (var response = await http.GetAsync(pack.Url, HttpCompletionOption.ResponseHeadersRead, cancel))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? (pack.SizeMb * 1024L * 1024);
                await using var source = await response.Content.ReadAsStreamAsync(cancel);
                await using var target = File.Create(zip);
                var buffer = new byte[256 * 1024];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancel)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancel);
                    done += read;
                    progress?.Report(Math.Min(0.9, 0.9 * done / total));
                }
            }

            await Task.Run(() => Extract(zip, pack, root, progress), cancel);
            File.WriteAllText(System.IO.Path.Combine(root, ".installed-" + pack.Id), pack.Name);
            progress?.Report(1);
        }
        finally
        {
            try
            {
                Directory.Delete(work, true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static void Extract(string zip, SoundPack pack, string root, IProgress<double>? progress)
    {
        using var archive = ZipFile.OpenRead(zip);
        var tops = archive.Entries.Select(e => e.FullName.Replace('\\', '/').Split('/')[0]).Where(t => t.Length > 0).Distinct().ToList();
        var target = tops.Count == 1 && archive.Entries.Any(e => e.FullName.Replace('\\', '/').Contains('/')) ? root : System.IO.Path.Combine(root, pack.Folder);
        var fullTarget = System.IO.Path.GetFullPath(target) + System.IO.Path.DirectorySeparatorChar;
        Directory.CreateDirectory(target);
        var index = 0;
        foreach (var entry in archive.Entries)
        {
            index++;
            var destination = System.IO.Path.GetFullPath(System.IO.Path.Combine(target, entry.FullName.Replace('/', System.IO.Path.DirectorySeparatorChar)));
            if (!destination.StartsWith(fullTarget, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The download holds a file outside its folder, so it was not installed.");
            }

            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination)!);
            entry.ExtractToFile(destination, true);
            if (index % 25 == 0)
            {
                progress?.Report(0.9 + (0.1 * index / archive.Entries.Count));
            }
        }
    }
}
