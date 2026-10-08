using System.Runtime.InteropServices;
using NAudio.Wave;
using NAudio.Wave.Asio;

namespace Overdub.Audio;

public sealed class AsioEngine : IDisposable
{
    private readonly float[] _peaks = new float[MaxInputs];
    private readonly bool[] _clipped = new bool[MaxInputs];
    private readonly InputRecorder?[] _recorders = new InputRecorder?[MaxInputs];
    private float[] _scratch = new float[4096];
    private AsioOut? _asio;
    private volatile bool _monitoring = true;

    public const int MaxInputs = 8;

    public string? DriverName { get; private set; }
    public int SampleRate { get; private set; }
    public int InputCount { get; private set; }
    public int BufferSamples { get; private set; }

    public bool SampleTypeSupported { get; private set; } = true;

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

    public bool IsRecording { get; private set; }

    public bool ReadClipped(int input)
    {
        var clipped = _clipped[input];
        _clipped[input] = false;
        return clipped;
    }

    public void StartRecording(IReadOnlyDictionary<int, string> pathsByInput)
    {
        if (_asio is null)
        {
            throw new InvalidOperationException("Open a driver first.");
        }

        if (IsRecording)
        {
            throw new InvalidOperationException("Already recording.");
        }

        foreach (var (input, path) in pathsByInput)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(input, InputCount);
            _recorders[input] = new InputRecorder(path, SampleRate);
        }

        IsRecording = true;
    }

    public IReadOnlyList<string> StopRecording()
    {
        IsRecording = false;
        var paths = new List<string>();
        for (var i = 0; i < _recorders.Length; i++)
        {
            var recorder = _recorders[i];
            _recorders[i] = null;
            if (recorder is null)
            {
                continue;
            }

            paths.Add(recorder.Path);
            recorder.Dispose();
        }

        return paths;
    }

    public void Close()
    {
        StopRecording();
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
        SampleTypeSupported = e.AsioSampleType is AsioSampleType.Int32LSB or AsioSampleType.Float32LSB;
        if (!SampleTypeSupported)
        {
            return;
        }

        var inputs = Math.Min(e.InputBuffers.Length, InputCount);
        if (_scratch.Length < e.SamplesPerBuffer)
        {
            _scratch = new float[e.SamplesPerBuffer];
        }

        for (var channel = 0; channel < inputs; channel++)
        {
            var peak = ConvertToFloat(e.InputBuffers[channel], e.SamplesPerBuffer, e.AsioSampleType, _scratch);
            _peaks[channel] = Math.Max(_peaks[channel], peak);
            _clipped[channel] |= peak >= 0.999f;
            _recorders[channel]?.Write(_scratch, e.SamplesPerBuffer);
        }

        if (!_monitoring || inputs == 0)
        {
            return;
        }

        for (var channel = 0; channel < e.OutputBuffers.Length; channel++)
        {
            var source = e.InputBuffers[Math.Min(channel, inputs - 1)];
            CopyMemory(e.OutputBuffers[channel], source, (uint)(e.SamplesPerBuffer * 4));
        }

        e.WrittenToOutputBuffers = true;
    }

    private static float ConvertToFloat(IntPtr buffer, int samples, AsioSampleType type, float[] destination)
    {
        var peak = 0f;
        for (var i = 0; i < samples; i++)
        {
            var raw = Marshal.ReadInt32(buffer, i * 4);
            var value = type == AsioSampleType.Int32LSB ? raw / 2147483648f : BitConverter.Int32BitsToSingle(raw);
            destination[i] = value;
            peak = Math.Max(peak, Math.Abs(value));
        }

        return peak;
    }

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
