using System.Text.Json;

namespace Overdub.Audio;

public sealed class DesignedSound
{
    public const int WaveSize = 512;
    public const double CustomMorph = -1;

    public string Name { get; set; } = "My bass";
    public float[] Wave { get; set; } = Shape(0);
    public double Morph { get; set; }
    public float Sub { get; set; } = 0.5f;
    public float Thickness { get; set; }
    public float Brightness { get; set; } = 0.5f;
    public float Growl { get; set; } = 0.1f;
    public float AttackN { get; set; } = 0.08f;
    public float DecayN { get; set; } = 0.3f;
    public float Sustain { get; set; } = 0.8f;
    public float ReleaseN { get; set; } = 0.2f;

    public double Attack => 0.002 + (0.5 * AttackN * AttackN);

    public double Decay => 0.03 + (1.5 * DecayN * DecayN);

    public double Release => 0.03 + (1.5 * ReleaseN * ReleaseN);

    public static IReadOnlyList<DesignedSound> Starters { get; } =
    [
        new() { Name = "Sub bass", Morph = 0, Wave = Shape(0), Sub = 0.55f, Thickness = 0f, Brightness = 0.35f, Growl = 0.1f, AttackN = 0.1f, DecayN = 0.3f, Sustain = 0.9f, ReleaseN = 0.2f },
        new() { Name = "Reese", Morph = 0.5, Wave = Shape(0.5), Sub = 0.5f, Thickness = 0.7f, Brightness = 0.4f, Growl = 0.35f, AttackN = 0.1f, DecayN = 0.4f, Sustain = 0.8f, ReleaseN = 0.3f },
        new() { Name = "Acid", Morph = 0.5, Wave = Shape(0.5), Sub = 0f, Thickness = 0f, Brightness = 0.3f, Growl = 0.45f, AttackN = 0.02f, DecayN = 0.3f, Sustain = 0.25f, ReleaseN = 0.15f },
        new() { Name = "Funk pluck", Morph = 0.35, Wave = Shape(0.35), Sub = 0.3f, Thickness = 0.1f, Brightness = 0.5f, Growl = 0.15f, AttackN = 0.02f, DecayN = 0.35f, Sustain = 0f, ReleaseN = 0.15f },
        new() { Name = "808", Morph = 0, Wave = Shape(0), Sub = 0.3f, Thickness = 0f, Brightness = 0.3f, Growl = 0.45f, AttackN = 0.02f, DecayN = 0.9f, Sustain = 0f, ReleaseN = 0.3f },
    ];

    public static float[] Shape(double morph)
    {
        morph = Math.Clamp(morph, 0, 1);
        var wave = new float[WaveSize];
        for (var i = 0; i < WaveSize; i++)
        {
            var phase = (double)i / WaveSize;
            var sine = Math.Sin(2 * Math.PI * phase);
            wave[i] = (float)(morph <= 0.5
                ? sine + ((Saw(phase) - sine) * (morph / 0.5))
                : Saw(phase) + ((Square(phase) - Saw(phase)) * ((morph - 0.5) / 0.5)));
        }

        return Normalise(wave);
    }

    public static float[] Triangle()
    {
        var wave = new float[WaveSize];
        for (var i = 0; i < WaveSize; i++)
        {
            var sum = 0.0;
            for (var n = 1; n <= 31; n += 2)
            {
                sum += (n % 4 == 1 ? 1 : -1) * Math.Sin(2 * Math.PI * n * i / WaveSize) / (n * n);
            }

            wave[i] = (float)sum;
        }

        return Normalise(wave);
    }

    public static float[] Smooth(float[] wave)
    {
        var result = new float[wave.Length];
        for (var i = 0; i < wave.Length; i++)
        {
            var sum = 0f;
            for (var k = -3; k <= 3; k++)
            {
                sum += wave[(i + k + wave.Length) % wave.Length];
            }

            result[i] = sum / 7f;
        }

        return Normalise(result);
    }

    public static float[] Normalise(float[] wave)
    {
        var mean = wave.Average();
        var peak = wave.Max(w => Math.Abs(w - mean));
        return peak < 1e-6f ? new float[wave.Length] : wave.Select(w => (w - mean) / peak).ToArray();
    }

    public SynthPatch ToPatch()
    {
        var detunes = Thickness < 0.02f ? new[] { 1.0 } : new[] { 1.0, 1.0 + (0.012 * Thickness), 1.0 - (0.009 * Thickness) };
        var cutoff = 0.015f + (0.985f * MathF.Pow(Brightness, 2.5f));
        var table = new float[2049];
        var wave = Normalise(Wave.Length == WaveSize ? Wave : Shape(0));
        for (var i = 0; i < 2048; i++)
        {
            var position = (double)i * WaveSize / 2048;
            var index = (int)position;
            var fraction = (float)(position - index);
            table[i] = wave[index] + ((wave[(index + 1) % WaveSize] - wave[index]) * fraction);
        }

        table[2048] = table[0];
        return new SynthPatch(Name, table, detunes, (float)Attack, (float)Decay, Sustain, (float)Release, 0f, cutoff, 0.3f, 1.15f)
        {
            SubLevel = Sub,
            Drive = Growl,
        };
    }

    public DesignedSound Copy() => new()
    {
        Name = Name,
        Wave = (float[])Wave.Clone(),
        Morph = Morph,
        Sub = Sub,
        Thickness = Thickness,
        Brightness = Brightness,
        Growl = Growl,
        AttackN = AttackN,
        DecayN = DecayN,
        Sustain = Sustain,
        ReleaseN = ReleaseN,
    };

    private static double Saw(double phase)
    {
        var sum = 0.0;
        for (var n = 1; n <= 60; n++)
        {
            sum += Math.Sin(2 * Math.PI * n * phase) / n;
        }

        return sum / 1.0;
    }

    private static double Square(double phase)
    {
        var sum = 0.0;
        for (var n = 1; n <= 59; n += 2)
        {
            sum += Math.Sin(2 * Math.PI * n * phase) / n;
        }

        return sum * 1.3;
    }
}

public static class DesignedSounds
{
    public const string Prefix = "My sound: ";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static string Directory => Path.Combine(AppPaths.SettingsDirectory, "Sounds");

    public static string PresetName(string name) => Prefix + name;

    public static IReadOnlyList<DesignedSound> List()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return [];
        }

        var sounds = new List<DesignedSound>();
        foreach (var file in System.IO.Directory.GetFiles(Directory, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                if (JsonSerializer.Deserialize<DesignedSound>(File.ReadAllText(file), Json) is { } sound && sound.Wave.Length == DesignedSound.WaveSize)
                {
                    sounds.Add(sound);
                }
            }
            catch (JsonException)
            {
            }
            catch (IOException)
            {
            }
        }

        return sounds;
    }

    public static DesignedSound? Find(string? presetName) =>
        presetName is not null && presetName.StartsWith(Prefix, StringComparison.Ordinal)
            ? List().FirstOrDefault(s => string.Equals(s.Name, presetName[Prefix.Length..], StringComparison.OrdinalIgnoreCase))
            : null;

    public static void Save(DesignedSound sound)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(FileFor(sound.Name), JsonSerializer.Serialize(sound, Json));
    }

    public static void Delete(string name)
    {
        var path = FileFor(name);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string FileFor(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        return Path.Combine(Directory, safe + ".json");
    }
}
