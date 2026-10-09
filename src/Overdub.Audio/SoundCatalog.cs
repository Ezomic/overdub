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
        new("blackgreen", "Black and Green Guitars", "Electric guitar", "Two electric guitars with twang, staccato, hammer-on and keyswitch mappings. Pick a twang or sustain mapping to start.", "https://github.com/sfzinstruments/karoryfer.black-and-green-guitars/releases/download/v1.000/Karoryfer_Black_And_Green_Guitars_1000.zip", 459, "Black and Green Guitars"),
        new("shinyguitar", "Shinyguitar", "Acoustic guitar", "A steel-string acoustic guitar with single-string and strumming mappings, release noises and string noises.", "https://github.com/sfzinstruments/karoryfer.shinyguitar/releases/download/v1.002/Karoryfer.Shinyguitar.v1.002.zip", 351, "Shinyguitar"),
        new("biglittlebass", "Big Little Bass", "Bass", "A big bass guitar played like a small one: light, bright plucks. Good for pop and indie.", "https://github.com/sfzinstruments/karoryfer.big-little-bass/releases/download/v1.000/Big_Little_Bass_1000.zip", 251, "Big Little Bass"),
        new("blackblue", "Black and Blue Basses", "Bass", "Two electric basses, a dark one and a baby blue one, with warm and plucked mappings. The biggest pack here.", "https://github.com/sfzinstruments/karoryfer.black-and-blue-basses/releases/download/v1.002/Black_And_Blue_Basses_1002.zip", 961, "Black and Blue Basses"),
        new("meatbass", "Meatbass", "Upright bass", "A double bass played bowed and plucked. Pick the pizz mapping for walking lines.", "https://github.com/sfzinstruments/karoryfer.meatbass/releases/download/v1.001/Karoryfer.Meatbass.v1.001.zip", 243, "Meatbass"),
        new("sneakybass", "Sneakybass", "Upright bass", "A double bass plucked quietly, with ghost notes, mutes and finger noises. Good for jazz and bossa nova.", "https://github.com/sfzinstruments/karoryfer.sneakybass/releases/download/v1.000/Sneakybass_v1.000.zip", 324, "Sneakybass"),
        new("ergo", "Ergo", "Upright bass", "An electric upright bass, bowed and plucked. Smooth and even.", "https://github.com/sfzinstruments/karoryfer.ergo/releases/download/v1.001/Karoryfer.Ergo_EUB.v1.001.zip", 191, "Ergo_EUB"),
        new("smolkenbass", "D. Smolken double bass", "Upright bass", "A Rubner double bass, bowed and plucked, with a switched mapping for both.", "https://github.com/sfzinstruments/dsmolken.double-bass/releases/download/v1.001/DSmolken.double_bass.v1.001.zip", 252, "dsmolken_double_bass"),
        new("scarypiano", "Scarypiano", "Piano", "A grand piano built from the University of Iowa samples, with a slightly haunted character. Play it from the Keys track.", "https://github.com/sfzinstruments/karoryfer.scarypiano/releases/download/v1.002/Karoryfer.Scarypiano.v1.002.zip", 342, "Scarypiano"),
        new("clavecin", "Clavecin", "Harpsichord", "A small harpsichord: bright, plucked keys for baroque or chamber-pop colours.", "https://github.com/sfzinstruments/Clavecin/archive/refs/heads/master.zip", 3, "Clavecin-master"),
        new("orgue", "Orgue d'eglise", "Organ", "A church organ with several stops. Slow pads and big chords.", "https://github.com/sfzinstruments/OrgueEglise/archive/refs/heads/master.zip", 26, "OrgueEglise-master"),
        new("marimba", "Marimba", "Mallets", "A marimba with three velocity layers. Warm, woody and percussive.", "https://github.com/sfzinstruments/Terkelsen.Marimba/archive/refs/heads/master.zip", 23, "Terkelsen.Marimba-master"),
        new("bearsax", "Bear Sax", "Saxophone", "A baritone sax, solo and in a duo, with a growly low end. Good for riffs and horn lines.", "https://github.com/sfzinstruments/karoryfer.bear-sax/releases/download/v1.004/Karoryfer.Bear_Sax.v1.004.zip", 125, "Bear_Sax"),
        new("weresax", "Weresax", "Saxophone", "An alto sax recorded soft and loud through two microphones. Smoother than the Bear Sax.", "https://github.com/sfzinstruments/karoryfer.weresax/releases/download/v1.003/Karoryfer.Weresax.v.1.003.zip", 188, "Weresax"),
        new("wartuba", "War Tuba", "Brass", "A tuba, solo up to a trio, legato and poly. Big brass bass notes.", "https://github.com/sfzinstruments/karoryfer.war-tuba/releases/download/v1.002/Karoryfer_War_Tuba_v1002.zip", 104, "War_tuba"),
        new("cello", "Bigcat cello", "Strings", "A cello, bowed with velocity or mod wheel layers, and plucked. Works for violin-like lines an octave up.", "https://github.com/sfzinstruments/karoryfer-bigcat.cello/releases/download/v1.001/Karoryfer_Bigcat_cello.v1.001.zip", 126, "Karoryfer_Bigcat_cello"),
        new("stringcyborgs", "String Cyborgs", "Strings", "Processed string ensembles and pads built from bowed samples. More cinematic than classical.", "https://github.com/sfzinstruments/karoryfer.string-cyborgs/releases/download/v1.001/Karoryfer.String_Cyborgs.v1.001.zip", 60, "String_Cyborgs"),
        new("squidpipes", "Squidpipes", "Wind", "Bagpipes, plus two synth-like instruments made from the same reeds.", "https://github.com/sfzinstruments/karoryfer.squidpipes/releases/download/v1.001/karoryfer.squidpipes-v1.001.zip", 42, "karoryfer.squidpipes-master"),
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
