using System.Security.Cryptography;

namespace Overdub.Audio;

public sealed record FlacAudio(int[][] Channels, int SampleRate, int BitsPerSample, bool Md5Verified);

public static class FlacDecoder
{
    private static readonly int[] SampleRates = [0, 88200, 176400, 192000, 8000, 16000, 22050, 24000, 32000, 44100, 48000, 96000];
    private static readonly int[] SampleSizes = [0, 8, 12, 0, 16, 20, 24, 32];

    public static bool IsFlac(string path) => path.EndsWith(".flac", StringComparison.OrdinalIgnoreCase);

    public static FlacAudio Decode(string path) => Decode(File.ReadAllBytes(path));

    public static FlacAudio Decode(byte[] data)
    {
        var reader = new BitReader(data);
        if (reader.ReadBits(32) != 0x664C6143)
        {
            throw new InvalidDataException("Not a FLAC file.");
        }

        var channels = 2;
        var bps = 16;
        var rate = 44100;
        long total = 0;
        var md5 = new byte[16];
        bool last;
        do
        {
            last = reader.ReadBits(1) == 1;
            var type = (int)reader.ReadBits(7);
            var length = (int)reader.ReadBits(24);
            if (type == 0)
            {
                reader.ReadBits(16);
                reader.ReadBits(16);
                reader.ReadBits(24);
                reader.ReadBits(24);
                rate = (int)reader.ReadBits(20);
                channels = (int)reader.ReadBits(3) + 1;
                bps = (int)reader.ReadBits(5) + 1;
                total = (long)reader.ReadBits(36);
                for (var i = 0; i < 16; i++)
                {
                    md5[i] = (byte)reader.ReadBits(8);
                }
            }
            else
            {
                reader.SkipBytes(length);
            }
        }
        while (!last);

        var capacity = total > 0 && total < int.MaxValue ? (int)total : 1 << 20;
        var output = new List<int>[channels];
        for (var c = 0; c < channels; c++)
        {
            output[c] = new List<int>(capacity);
        }

        var block = new int[channels][];
        while (!reader.AtEnd)
        {
            if (!reader.FindSync())
            {
                break;
            }

            DecodeFrame(reader, bps, block, output, ref rate);
        }

        var result = output.Select(o => o.ToArray()).ToArray();
        return new FlacAudio(result, rate, bps, VerifyMd5(result, bps, md5));
    }

    public static (float[] Mono, int Rate) DecodeMono(string path)
    {
        var audio = Decode(path);
        var length = audio.Channels.Min(c => c.Length);
        var scale = 1f / (1 << (audio.BitsPerSample - 1));
        var mono = new float[length];
        for (var i = 0; i < length; i++)
        {
            var sum = 0f;
            foreach (var channel in audio.Channels)
            {
                sum += channel[i];
            }

            mono[i] = sum * scale / audio.Channels.Length;
        }

        return (mono, audio.SampleRate);
    }

    private static void DecodeFrame(BitReader reader, int streamBps, int[][] block, List<int>[] output, ref int rate)
    {
        reader.ReadBits(1);
        var blockSizeCode = (int)reader.ReadBits(4);
        var rateCode = (int)reader.ReadBits(4);
        var channelCode = (int)reader.ReadBits(4);
        var sizeCode = (int)reader.ReadBits(3);
        reader.ReadBits(1);
        reader.ReadUtf8Number();
        var blockSize = blockSizeCode switch
        {
            1 => 192,
            >= 2 and <= 5 => 576 << (blockSizeCode - 2),
            6 => (int)reader.ReadBits(8) + 1,
            7 => (int)reader.ReadBits(16) + 1,
            _ => 256 << (blockSizeCode - 8),
        };
        switch (rateCode)
        {
            case 12:
                rate = (int)reader.ReadBits(8) * 1000;
                break;
            case 13:
                rate = (int)reader.ReadBits(16);
                break;
            case 14:
                rate = (int)reader.ReadBits(16) * 10;
                break;
            case > 0 and < 12:
                rate = SampleRates[rateCode];
                break;
        }

        var bps = sizeCode == 0 ? streamBps : SampleSizes[sizeCode];
        reader.ReadBits(8);
        var channels = channelCode < 8 ? channelCode + 1 : 2;
        for (var c = 0; c < channels; c++)
        {
            if (block[c] is null || block[c].Length < blockSize)
            {
                block[c] = new int[blockSize];
            }

            var extra = (channelCode == 8 && c == 1) || (channelCode == 9 && c == 0) || (channelCode == 10 && c == 1) ? 1 : 0;
            DecodeSubframe(reader, bps + extra, blockSize, block[c]);
        }

        reader.AlignToByte();
        reader.ReadBits(16);
        switch (channelCode)
        {
            case 8:
                for (var i = 0; i < blockSize; i++)
                {
                    block[1][i] = block[0][i] - block[1][i];
                }

                break;
            case 9:
                for (var i = 0; i < blockSize; i++)
                {
                    block[0][i] += block[1][i];
                }

                break;
            case 10:
                for (var i = 0; i < blockSize; i++)
                {
                    var side = block[1][i];
                    var mid = (block[0][i] << 1) | (side & 1);
                    block[0][i] = (mid + side) >> 1;
                    block[1][i] = (mid - side) >> 1;
                }

                break;
        }

        for (var c = 0; c < channels && c < output.Length; c++)
        {
            output[c].AddRange(new ArraySegment<int>(block[c], 0, blockSize));
        }
    }

    private static void DecodeSubframe(BitReader reader, int bps, int blockSize, int[] samples)
    {
        reader.ReadBits(1);
        var type = (int)reader.ReadBits(6);
        var wasted = 0;
        if (reader.ReadBits(1) == 1)
        {
            wasted = 1;
            while (reader.ReadBits(1) == 0)
            {
                wasted++;
            }
        }

        bps -= wasted;
        if (type == 0)
        {
            var value = reader.ReadSigned(bps);
            Array.Fill(samples, value, 0, blockSize);
        }
        else if (type == 1)
        {
            for (var i = 0; i < blockSize; i++)
            {
                samples[i] = reader.ReadSigned(bps);
            }
        }
        else if (type >= 8 && type <= 12)
        {
            var order = type - 8;
            for (var i = 0; i < order; i++)
            {
                samples[i] = reader.ReadSigned(bps);
            }

            DecodeResidual(reader, blockSize, order, samples);
            RestoreFixed(samples, blockSize, order);
        }
        else if (type >= 32)
        {
            var order = type - 31;
            for (var i = 0; i < order; i++)
            {
                samples[i] = reader.ReadSigned(bps);
            }

            var precision = (int)reader.ReadBits(4) + 1;
            var shift = reader.ReadSigned(5);
            var coefficients = new int[order];
            for (var i = 0; i < order; i++)
            {
                coefficients[i] = reader.ReadSigned(precision);
            }

            DecodeResidual(reader, blockSize, order, samples);
            for (var i = order; i < blockSize; i++)
            {
                long sum = 0;
                for (var j = 0; j < order; j++)
                {
                    sum += (long)coefficients[j] * samples[i - 1 - j];
                }

                samples[i] += (int)(sum >> shift);
            }
        }
        else
        {
            throw new InvalidDataException("Unknown FLAC subframe type.");
        }

        if (wasted > 0)
        {
            for (var i = 0; i < blockSize; i++)
            {
                samples[i] <<= wasted;
            }
        }
    }

    private static void RestoreFixed(int[] samples, int blockSize, int order)
    {
        for (var i = order; i < blockSize; i++)
        {
            samples[i] += order switch
            {
                0 => 0,
                1 => samples[i - 1],
                2 => (2 * samples[i - 1]) - samples[i - 2],
                3 => (3 * samples[i - 1]) - (3 * samples[i - 2]) + samples[i - 3],
                _ => (4 * samples[i - 1]) - (6 * samples[i - 2]) + (4 * samples[i - 3]) - samples[i - 4],
            };
        }
    }

    private static void DecodeResidual(BitReader reader, int blockSize, int order, int[] samples)
    {
        var method = (int)reader.ReadBits(2);
        var parameterBits = method == 0 ? 4 : 5;
        var escape = method == 0 ? 15 : 31;
        var partitionOrder = (int)reader.ReadBits(4);
        var partitions = 1 << partitionOrder;
        var partitionSize = blockSize >> partitionOrder;
        var index = order;
        for (var p = 0; p < partitions; p++)
        {
            var count = p == 0 ? partitionSize - order : partitionSize;
            var parameter = (int)reader.ReadBits(parameterBits);
            if (parameter == escape)
            {
                var raw = (int)reader.ReadBits(5);
                for (var i = 0; i < count; i++)
                {
                    samples[index++] = raw == 0 ? 0 : reader.ReadSigned(raw);
                }

                continue;
            }

            for (var i = 0; i < count; i++)
            {
                samples[index++] = reader.ReadRice(parameter);
            }
        }
    }

    private static bool VerifyMd5(int[][] channels, int bps, byte[] expected)
    {
        if (expected.All(b => b == 0))
        {
            return false;
        }

        var bytesPerSample = (bps + 7) / 8;
        var length = channels.Min(c => c.Length);
        var buffer = new byte[Math.Min(length, 65536) * channels.Length * bytesPerSample];
        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var filled = 0;
        for (var i = 0; i < length; i++)
        {
            foreach (var channel in channels)
            {
                var value = channel[i];
                for (var b = 0; b < bytesPerSample; b++)
                {
                    buffer[filled++] = (byte)(value >> (8 * b));
                }
            }

            if (filled == buffer.Length)
            {
                md5.AppendData(buffer, 0, filled);
                filled = 0;
            }
        }

        md5.AppendData(buffer, 0, filled);
        return md5.GetHashAndReset().AsSpan().SequenceEqual(expected);
    }

    private sealed class BitReader(byte[] data)
    {
        private long _bit;
        private ulong _cache;
        private int _cached;

        public bool AtEnd => _bit >= (long)data.Length * 8;

        private void Refill()
        {
            while (_cached <= 56)
            {
                var index = (_bit + _cached) >> 3;
                var next = index < data.Length ? data[index] : (byte)0;
                _cache |= (ulong)next << (56 - _cached);
                _cached += 8;
            }
        }

        public ulong ReadBits(int count)
        {
            if (count == 0)
            {
                return 0;
            }

            if (count > 32)
            {
                var high = ReadBits(count - 32);
                return (high << 32) | ReadBits(32);
            }

            if (_cached < count)
            {
                Refill();
            }

            var value = _cache >> (64 - count);
            _cache <<= count;
            _cached -= count;
            _bit += count;
            return value;
        }

        private int ReadUnary()
        {
            var zeros = 0;
            while (true)
            {
                if (_cached == 0)
                {
                    Refill();
                }

                var leading = System.Numerics.BitOperations.LeadingZeroCount(_cache);
                if (leading < _cached)
                {
                    zeros += leading;
                    var consumed = leading + 1;
                    _cache <<= consumed;
                    _cached -= consumed;
                    _bit += consumed;
                    return zeros;
                }

                zeros += _cached;
                _bit += _cached;
                _cache = 0;
                _cached = 0;
                if (AtEnd)
                {
                    return zeros;
                }
            }
        }

        public int ReadSigned(int count)
        {
            if (count == 0)
            {
                return 0;
            }

            var value = (long)ReadBits(count);
            return (int)((value << (64 - count)) >> (64 - count));
        }

        public int ReadRice(int parameter)
        {
            var quotient = ReadUnary();
            var value = ((long)quotient << parameter) | (long)ReadBits(parameter);
            return (int)((value >> 1) ^ -(value & 1));
        }

        public void ReadUtf8Number()
        {
            var first = (int)ReadBits(8);
            var extra = first >= 0xFE ? 6 : first >= 0xFC ? 5 : first >= 0xF8 ? 4 : first >= 0xF0 ? 3 : first >= 0xE0 ? 2 : first >= 0xC0 ? 1 : 0;
            for (var i = 0; i < extra; i++)
            {
                ReadBits(8);
            }
        }

        private void Seek(long bit)
        {
            _bit = bit;
            _cache = 0;
            _cached = 0;
            var partial = (int)(bit & 7);
            if (partial != 0)
            {
                var index = bit >> 3;
                var current = index < data.Length ? data[index] : (byte)0;
                _cache = (ulong)((current << partial) & 0xFF) << 56;
                _cached = 8 - partial;
            }
        }

        public void AlignToByte() => Seek((_bit + 7) & ~7L);

        public void SkipBytes(int count) => Seek(_bit + ((long)count * 8));

        public bool FindSync()
        {
            AlignToByte();
            var index = _bit >> 3;
            while (index + 1 < data.Length)
            {
                if (data[index] == 0xFF && (data[index + 1] & 0xFC) == 0xF8)
                {
                    Seek((index << 3) + 15);
                    return true;
                }

                index++;
            }

            Seek((long)data.Length * 8);
            return false;
        }
    }
}
