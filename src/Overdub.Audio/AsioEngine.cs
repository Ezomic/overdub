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
    private float[] _monitor = new float[4096];
    private float[] _mix = new float[4096];
    private int[] _outInt = new int[4096];
    private PlaybackTrack[] _tracks = [];
    private long _position;
    private volatile bool _playing;
    private volatile bool _metronomeEnabled;
    private double _bpm = 120;
    private AsioOut? _asio;
    private volatile bool _monitoring = true;

    public const int MaxInputs = 8;

    public string? DriverName { get; private set; }
    public int SampleRate { get; private set; }
    public int InputCount { get; private set; }
    public int BufferSamples { get; private set; }
    public int OutputLatencySamples { get; private set; }
    public int ManualOffsetSamples { get; set; }

    public int CompensationSamples => OutputLatencySamples + BufferSamples + ManualOffsetSamples;

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
        OutputLatencySamples = _asio.PlaybackLatency;
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
    public long RecordStartSample { get; private set; }

    public bool IsPlaying => _playing;
    public long Position => Volatile.Read(ref _position);
    public TimeSpan PositionTime => SampleRate == 0 ? TimeSpan.Zero : TimeSpan.FromSeconds((double)Position / SampleRate);

    public bool MetronomeEnabled
    {
        get => _metronomeEnabled;
        set => _metronomeEnabled = value;
    }

    public double Bpm
    {
        get => Volatile.Read(ref _bpm);
        set => Volatile.Write(ref _bpm, Math.Clamp(value, 20, 300));
    }

    public IReadOnlyList<PlaybackTrack> Tracks => _tracks;

    public void SetTracks(IEnumerable<PlaybackTrack> tracks) => _tracks = tracks.ToArray();

    public void Play() => _playing = true;

    public void Pause() => _playing = false;

    public void Seek(long sample) => Volatile.Write(ref _position, Math.Max(0, sample));

    public void StopTransport()
    {
        _playing = false;
        Seek(0);
    }

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

        RecordStartSample = Position;
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
        OutputLatencySamples = 0;
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
        var frames = e.SamplesPerBuffer;
        EnsureBuffers(frames);
        Array.Clear(_monitor, 0, frames);

        for (var channel = 0; channel < inputs; channel++)
        {
            var peak = ConvertToFloat(e.InputBuffers[channel], frames, e.AsioSampleType, _scratch);
            _peaks[channel] = Math.Max(_peaks[channel], peak);
            _clipped[channel] |= peak >= 0.999f;
            _recorders[channel]?.Write(_scratch, frames);
            if (_monitoring)
            {
                for (var i = 0; i < frames; i++)
                {
                    _monitor[i] += _scratch[i];
                }
            }
        }

        Array.Clear(_mix, 0, frames);
        if (_playing)
        {
            var position = Volatile.Read(ref _position);
            MixTracks(position, frames);
            if (_metronomeEnabled)
            {
                MixClick(position, frames);
            }

            Interlocked.Add(ref _position, frames);
        }

        for (var i = 0; i < frames; i++)
        {
            _mix[i] += _monitor[i];
        }

        for (var channel = 0; channel < e.OutputBuffers.Length; channel++)
        {
            WriteOutput(e.OutputBuffers[channel], frames, e.AsioSampleType);
        }

        e.WrittenToOutputBuffers = true;
    }

    private void EnsureBuffers(int frames)
    {
        if (_scratch.Length >= frames)
        {
            return;
        }

        _scratch = new float[frames];
        _monitor = new float[frames];
        _mix = new float[frames];
        _outInt = new int[frames];
    }

    private void MixTracks(long position, int frames) => Mixer.Mix(_tracks, position, _mix, frames);

    private void MixClick(long position, int frames)
    {
        var samplesPerBeat = SampleRate * 60.0 / Bpm;
        var clickLength = SampleRate / 50;
        for (var i = 0; i < frames; i++)
        {
            var at = position + i;
            var beat = (long)(at / samplesPerBeat);
            var offset = (int)(at - (beat * samplesPerBeat));
            if (offset >= clickLength)
            {
                continue;
            }

            var frequency = beat % 4 == 0 ? 1500.0 : 1000.0;
            var envelope = 1f - ((float)offset / clickLength);
            _mix[i] += (float)Math.Sin(2 * Math.PI * frequency * offset / SampleRate) * envelope * 0.4f;
        }
    }

    private void WriteOutput(IntPtr destination, int frames, AsioSampleType type)
    {
        if (type == AsioSampleType.Float32LSB)
        {
            Marshal.Copy(_mix, 0, destination, frames);
            return;
        }

        for (var i = 0; i < frames; i++)
        {
            _outInt[i] = (int)(Math.Clamp(_mix[i], -1f, 1f) * 2147483647f);
        }

        Marshal.Copy(_outInt, 0, destination, frames);
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
