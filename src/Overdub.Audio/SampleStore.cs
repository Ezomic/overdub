using System.IO.MemoryMappedFiles;
using System.Security.Cryptography;
using System.Text;

namespace Overdub.Audio;

public abstract class SampleData
{
    public static readonly SampleData Empty = new ArraySampleData([], 44100);

    public abstract int Length { get; }
    public abstract int Rate { get; }
    public abstract float At(int index);
}

public sealed class ArraySampleData(float[] data, int rate) : SampleData
{
    public override int Length => data.Length;
    public override int Rate => rate;
    public override float At(int index) => data[index];
}

public sealed unsafe class MappedSampleData : SampleData, IDisposable
{
    private const float Scale = 1f / 32768f;
    private readonly MemoryMappedFile _file;
    private readonly MemoryMappedViewAccessor _view;
    private readonly short* _samples;

    public MappedSampleData(string path, int rate, int length, long dataOffset)
    {
        _file = MemoryMappedFile.CreateFromFile(path, FileMode.Open, null, 0, MemoryMappedFileAccess.Read);
        _view = _file.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        byte* pointer = null;
        _view.SafeMemoryMappedViewHandle.AcquirePointer(ref pointer);
        _samples = (short*)(pointer + _view.PointerOffset + dataOffset);
        Rate = rate;
        Length = length;
    }

    public override int Length { get; }
    public override int Rate { get; }
    public override float At(int index) => _samples[index] * Scale;

    public void Dispose()
    {
        _view.SafeMemoryMappedViewHandle.ReleasePointer();
        _view.Dispose();
        _file.Dispose();
    }
}

public static class SampleCache
{
    private const int HeaderBytes = 16;
    private static readonly byte[] Magic = "OVDS"u8.ToArray();

    public static string Directory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Overdub", "SampleCache");

    public static string CachePath(string samplePath)
    {
        var info = new FileInfo(samplePath);
        var key = $"{Path.GetFullPath(samplePath).ToLowerInvariant()}|{info.Length}|{info.LastWriteTimeUtc.Ticks}";
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(Directory, hash + ".pcm");
    }

    public static SampleData? TryOpen(string samplePath)
    {
        var path = CachePath(samplePath);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            Span<byte> header = stackalloc byte[HeaderBytes];
            using (var stream = File.OpenRead(path))
            {
                if (stream.Read(header) < HeaderBytes || !header[..4].SequenceEqual(Magic))
                {
                    return null;
                }
            }

            var rate = BitConverter.ToInt32(header[4..8]);
            var length = BitConverter.ToInt32(header[8..12]);
            if (length <= 0 || new FileInfo(path).Length < HeaderBytes + ((long)length * 2))
            {
                return null;
            }

            return new MappedSampleData(path, rate, length, HeaderBytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static SampleData Store(string samplePath, float[] mono, int rate)
    {
        var path = CachePath(samplePath);
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var temp = path + "." + Environment.ProcessId + ".tmp";
            using (var stream = File.Create(temp))
            {
                var header = new byte[HeaderBytes];
                Magic.CopyTo(header, 0);
                BitConverter.GetBytes(rate).CopyTo(header, 4);
                BitConverter.GetBytes(mono.Length).CopyTo(header, 8);
                stream.Write(header);
                var buffer = new byte[Math.Min(mono.Length, 65536) * 2];
                var at = 0;
                while (at < mono.Length)
                {
                    var count = Math.Min(buffer.Length / 2, mono.Length - at);
                    for (var i = 0; i < count; i++)
                    {
                        var value = (short)Math.Clamp(Math.Round(mono[at + i] * 32767f), short.MinValue, short.MaxValue);
                        buffer[i * 2] = (byte)value;
                        buffer[(i * 2) + 1] = (byte)(value >> 8);
                    }

                    stream.Write(buffer, 0, count * 2);
                    at += count;
                }
            }

            File.Move(temp, path, true);
            return TryOpen(samplePath) ?? new ArraySampleData(mono, rate);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ArraySampleData(mono, rate);
        }
    }

    public static long SizeBytes()
    {
        try
        {
            return System.IO.Directory.Exists(Directory) ? new DirectoryInfo(Directory).EnumerateFiles("*.pcm").Sum(f => f.Length) : 0;
        }
        catch (IOException)
        {
            return 0;
        }
    }

    public static void Clear()
    {
        if (!System.IO.Directory.Exists(Directory))
        {
            return;
        }

        foreach (var file in System.IO.Directory.EnumerateFiles(Directory, "*.pcm"))
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
