using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Overdub.Audio;

public static class AudioEncoder
{
    private static readonly Guid FlacFormat = new("0000F1AC-0000-0010-8000-00AA00389B71");

    public static bool IsEncoded(string path) =>
        System.IO.Path.GetExtension(path).ToLowerInvariant() is ".mp3" or ".flac";

    public static void Convert(string sourceWav, string destination, int mp3BitRate = 192_000)
    {
        MediaFoundationApi.Startup();
        using var reader = new AudioFileReader(sourceWav);
        var extension = System.IO.Path.GetExtension(destination).ToLowerInvariant();
        switch (extension)
        {
            case ".mp3":
                MediaFoundationEncoder.EncodeToMp3(new SampleToWaveProvider16(reader), destination, mp3BitRate);
                break;
            case ".flac":
                EncodeFlac(reader, destination);
                break;
            default:
                throw new NotSupportedException($"Cannot encode {extension} files.");
        }
    }

    private static void EncodeFlac(AudioFileReader reader, string destination)
    {
        var candidates = MediaFoundationEncoder.GetOutputMediaTypes(FlacFormat)
            .Where(t => t.SampleRate == reader.WaveFormat.SampleRate && t.ChannelCount == reader.WaveFormat.Channels)
            .ToList();
        foreach (var bits in new[] { 24, 16 })
        {
            var type = candidates.FirstOrDefault(t => t.BitsPerSample == bits);
            if (type is null)
            {
                continue;
            }

            reader.Position = 0;
            IWaveProvider source = bits == 24 ? new SampleToWaveProvider24(reader) : new SampleToWaveProvider16(reader);
            try
            {
                using var encoder = new MediaFoundationEncoder(type);
                encoder.Encode(destination, source);
                return;
            }
            catch (MediaFoundationException) when (bits == 24)
            {
            }
        }

        throw new InvalidOperationException("This version of Windows has no FLAC encoder for this format. Export a WAV instead.");
    }
}
