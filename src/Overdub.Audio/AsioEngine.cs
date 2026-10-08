using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace Overdub.Audio;

public sealed class AsioEngine : IDisposable
{
    private readonly float[] _peaks = new float[MaxInputs];
    private AsioOut? _asio;
    private bool _monitoring = true;

    public const int MaxInputs = 8;

    public string? DriverName { get; private set; }
    public int SampleRate { get; private set; }
    public int InputCount { get; private set; }
    public int BufferSamples { get; private set; }

    public double LatencyMilliseconds => SampleRate == 0 ? 0 : BufferSamples * 1000.0 / SampleRate;

    public bool Monitoring
    {
        get => _monitoring;
        set => _monitoring = value;
    }

    public static IReadOnlyList<string> GetDriverNames() => AsioOut.GetDriverNames();

    public void Open(string driverName, int sampleRate = 44100)
    {
        Close();

        _asio = new AsioOut(driverName);
        InputCount = Math.Min(_asio.DriverInputChannelCount, MaxInputs);
        _asio.InputChannelOffset = 0;
        _asio.AudioAvailable += OnAudioAvailable;
        _asio.InitRecordAndPlayback(new SilenceProvider(sampleRate, _asio.DriverOutputChannelCount), InputCount, sampleRate);

        DriverName = driverName;
        SampleRate = sampleRate;
        BufferSamples = _asio.FramesPerBuffer;
    }

    public void Start() => (_asio ?? throw new InvalidOperationException("Open a driver first.")).Play();

    public void Stop() => _asio?.Stop();

    public float ReadPeak(int input)
    {
        var peak = _peaks[input];
        _peaks[input] = 0;
        return peak;
    }

    public void Close()
    {
        if (_asio is null)
        {
            return;
        }

        _asio.AudioAvailable -= OnAudioAvailable;
        _asio.Dispose();
        _asio = null;
        DriverName = null;
        SampleRate = 0;
        InputCount = 0;
        BufferSamples = 0;
    }

    public void Dispose() => Close();

    private void OnAudioAvailable(object? sender, AsioAudioAvailableEventArgs e)
    {
        var inputs = Math.Min(e.InputBuffers.Length, InputCount);
        for (var channel = 0; channel < inputs; channel++)
        {
            _peaks[channel] = Math.Max(_peaks[channel], MeasurePeak(e.InputBuffers[channel], e.SamplesPerBuffer, e.AsioSampleType));
        }

        if (!_monitoring)
        {
            return;
        }

        var bytesPerSample = BytesPerSample(e.AsioSampleType);
        for (var channel = 0; channel < e.OutputBuffers.Length; channel++)
        {
            var source = e.InputBuffers[Math.Min(channel, inputs - 1)];
            CopyMemory(e.OutputBuffers[channel], source, (uint)(e.SamplesPerBuffer * bytesPerSample));
        }

        e.WrittenToOutputBuffers = true;
    }

    private static float MeasurePeak(IntPtr buffer, int samples, AsioSampleType type)
    {
        var peak = 0f;
        switch (type)
        {
            case AsioSampleType.Int32LSB:
                for (var i = 0; i < samples; i++)
                {
                    peak = Math.Max(peak, Math.Abs(Marshal.ReadInt32(buffer, i * 4) / 2147483648f));
                }

                break;
            case AsioSampleType.Float32LSB:
                for (var i = 0; i < samples; i++)
                {
                    peak = Math.Max(peak, Math.Abs(BitConverter.Int32BitsToSingle(Marshal.ReadInt32(buffer, i * 4))));
                }

                break;
            default:
                throw new NotSupportedException($"ASIO sample type {type} is not supported yet.");
        }

        return peak;
    }

    private static int BytesPerSample(AsioSampleType type) => type switch
    {
        AsioSampleType.Int32LSB or AsioSampleType.Float32LSB => 4,
        _ => throw new NotSupportedException($"ASIO sample type {type} is not supported yet."),
    };

    [DllImport("kernel32.dll", EntryPoint = "RtlMoveMemory")]
    private static extern void CopyMemory(IntPtr destination, IntPtr source, uint length);

    private sealed class SilenceProvider(int sampleRate, int channels) : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        public int Read(Span<byte> buffer)
        {
            buffer.Clear();
            return buffer.Length;
        }
    }
}
