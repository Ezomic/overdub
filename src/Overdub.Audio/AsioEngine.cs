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
    private readonly float[][] _inputBufs = new float[MaxInputs][];
    private readonly List<long> _wraps = [];
    private readonly object _wrapsLock = new();
    private long _recordedFrames;
    private readonly float[] _tunerRing = new float[8192];
    private int _tunerWrite;
    private volatile int _tunerChannel = -1;
    private float[] _monitorL = new float[4096];
    private float[] _monitorR = new float[4096];
    private float[] _liveL = new float[4096];
    private float[] _liveR = new float[4096];
    private readonly MixScratch _fxScratch = new();
    private readonly EffectChain?[] _liveSource = new EffectChain?[MaxInputs];
    private readonly EffectChain?[] _liveProcessor = new EffectChain?[MaxInputs];
    private ChannelStrip[] _channels = [];
    private float[] _mixL = new float[4096];
    private float[] _mixR = new float[4096];
    private float[] _synthBuf = new float[4096];
    private int[] _outInt = new int[4096];
    private PlaybackTrack[] _tracks = [];
    private MidiClip[] _midi = [];
    private readonly MidiSequencer _sequencer = new();
    private long _position;
    private volatile bool _playing;
    private LatencyProbe? _probe;
    private volatile bool _countingIn;
    private volatile bool _waitingForInput;
    private long _countInPos;
    private long _countInEnd;
    private double _countInLead;
    private int _countInBeats;
    private volatile bool _metronomeEnabled;
    private double _bpm = 120;
    private AsioOut? _asio;
    private volatile bool _monitoring = true;

    public const int MaxInputs = 8;

    public Synth Synth { get; } = new();
    public DrumKit Drums { get; } = new();
    public MachineLane[] Machines { get; } = [new MachineLane(MachineRole.Guitar), new MachineLane(MachineRole.Bass)];
    private readonly float[][] _machineBufs = [new float[4096], new float[4096]];
    public float DrumGain { get; set; } = 1f;
    public float DrumPan { get; set; }
    private MidiClip[] _drumMidi = [];
    private readonly MidiSequencer _drumSequencer = new();
    private float[] _drumBuf = new float[4096];
    public float SynthGain { get; set; } = 1f;
    public float SynthPan { get; set; }

    public string? DriverName { get; private set; }
    public int SampleRate { get; private set; }
    public int InputCount { get; private set; }
    public int BufferSamples { get; private set; }
    public int OutputLatencySamples { get; private set; }
    public int ManualOffsetSamples { get; set; }

    public int? MeasuredRoundTripSamples { get; private set; }

    public int EstimatedRoundTripSamples => OutputLatencySamples + BufferSamples;

    public int CompensationSamples => (MeasuredRoundTripSamples ?? EstimatedRoundTripSamples) + ManualOffsetSamples;

    private string LatencyKey => $"{DriverName}|{SampleRate}|{BufferSamples}";

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
        Synth.Configure(sampleRate);
        Drums.Configure(sampleRate);
        foreach (var lane in Machines)
        {
            lane.Configure(sampleRate);
        }
        BufferSamples = _asio.FramesPerBuffer;
        OutputLatencySamples = _asio.PlaybackLatency;
        MeasuredRoundTripSamples = LatencyStore.Load(LatencyKey);
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
    public bool IsCountingIn => _countingIn;
    public bool IsWaitingForInput => _waitingForInput;
    public float TriggerThreshold { get; set; } = 0.01f;

    public void ReleaseWait()
    {
        if (_waitingForInput)
        {
            _waitingForInput = false;
            _playing = true;
        }
    }

    public int TunerInput
    {
        get => _tunerChannel;
        set => _tunerChannel = value;
    }
    public bool LoopEnabled { get; set; }
    public long LoopStart { get; set; }
    public long LoopEnd { get; set; }
    public bool PunchEnabled { get; set; }
    public long PunchIn { get; set; }
    public long PunchOut { get; set; }
    public bool PunchCompleted { get; private set; }
    public IReadOnlyList<long> LastRecordingWraps { get; private set; } = [];
    public bool RecordGateOpen { get; private set; }
    public int CountInBars { get; set; }
    public int BeatsPerBar { get; set; } = 4;
    public int BeatUnit { get; set; } = 4;

    public double SamplesPerBeat => SampleRate * 60.0 / Bpm * 4.0 / BeatUnit;
    public double Speed { get; private set; } = 1.0;

    public long Position => (long)(Volatile.Read(ref _position) * Speed);

    public void SetSpeed(double speed)
    {
        var original = Position;
        Speed = Math.Clamp(speed, 0.25, 2.0);
        Seek(original);
    }
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

    public IReadOnlyList<MidiClip> MidiClips => _midi;

    public void SetChannels(IEnumerable<ChannelStrip> channels)
    {
        var array = channels.ToArray();
        _tracks = array.SelectMany(c => c.Clips).ToArray();
        _channels = array;
    }

    public void SetLiveEffects(int input, EffectChain? source, EffectChain? processor)
    {
        _liveProcessor[input] = processor;
        _liveSource[input] = source;
    }

    public void SetMidiClips(IEnumerable<MidiClip> clips) => _midi = clips.ToArray();

    public void SetMachineClips(MachineRole role, IEnumerable<MidiClip> clips) => Machines[(int)role].Clips = clips.ToArray();

    public void SetDrumClips(IEnumerable<MidiClip> clips) => _drumMidi = clips.ToArray();

    public void Play() => _playing = true;

    public void BeginCountIn()
    {
        var samplesPerBeat = SamplesPerBeat;
        var beats = CountInBars * BeatsPerBar;
        var length = beats * samplesPerBeat;
        var buffer = Math.Max(1, BufferSamples);
        _countInBeats = beats;
        _countInEnd = (long)Math.Ceiling(length / buffer) * buffer;
        _countInLead = _countInEnd - length;
        _countInPos = 0;
        _countingIn = true;
    }

    public void CopyTunerWindow(float[] destination)
    {
        var write = Volatile.Read(ref _tunerWrite);
        for (var i = 0; i < destination.Length; i++)
        {
            destination[i] = _tunerRing[(write - destination.Length + i) & (_tunerRing.Length - 1)];
        }
    }

    private void PushTuner(float[] buffer, int frames)
    {
        var write = _tunerWrite;
        for (var i = 0; i < frames; i++)
        {
            _tunerRing[(write + i) & (_tunerRing.Length - 1)] = buffer[i];
        }

        Volatile.Write(ref _tunerWrite, write + frames);
    }

    public async Task<int?> MeasureLatencyAsync(int input, CancellationToken cancellation = default)
    {
        if (_asio is null)
        {
            throw new InvalidOperationException("Open a driver first.");
        }

        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(input, InputCount);
        var wasMonitoring = _monitoring;
        _monitoring = false;
        var probe = new LatencyProbe(SampleRate, input);
        Volatile.Write(ref _probe, probe);
        try
        {
            var result = await probe.Completion.WaitAsync(cancellation);
            if (result is { } samples)
            {
                MeasuredRoundTripSamples = samples;
                LatencyStore.Save(LatencyKey, samples);
            }

            return result;
        }
        finally
        {
            Volatile.Write(ref _probe, null);
            _monitoring = wasMonitoring;
        }
    }

    public void ClearMeasuredLatency()
    {
        MeasuredRoundTripSamples = null;
        LatencyStore.Remove(LatencyKey);
    }

    public void Pause()
    {
        _playing = false;
        _countingIn = false;
        Synth.AllNotesOff();
        Drums.Silence();
        foreach (var lane in Machines)
        {
            lane.AllNotesOff();
        }
    }

    public void Seek(long sample)
    {
        Volatile.Write(ref _position, (long)(Math.Max(0, sample) / Speed));
        Synth.AllNotesOff();
        Drums.Silence();
        foreach (var lane in Machines)
        {
            lane.AllNotesOff();
        }
    }

    public void StopTransport()
    {
        _playing = false;
        _countingIn = false;
        Seek(0);
    }

    public bool ReadClipped(int input)
    {
        var clipped = _clipped[input];
        _clipped[input] = false;
        return clipped;
    }

    public void StartRecording(IReadOnlyDictionary<int, string> pathsByInput, bool waitForInput = false)
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
        _recordedFrames = 0;
        lock (_wrapsLock)
        {
            _wraps.Clear();
        }

        PunchCompleted = false;
        IsRecording = true;
        _waitingForInput = waitForInput;
    }

    public IReadOnlyList<string> StopRecording()
    {
        IsRecording = false;
        _countingIn = false;
        _waitingForInput = false;
        PunchCompleted = false;
        RecordGateOpen = false;
        lock (_wrapsLock)
        {
            LastRecordingWraps = _wraps.ToArray();
        }

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
        MeasuredRoundTripSamples = null;
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
        Array.Clear(_monitorL, 0, frames);
        Array.Clear(_monitorR, 0, frames);

        for (var channel = 0; channel < inputs; channel++)
        {
            var buffer = _inputBufs[channel];
            var peak = ConvertToFloat(e.InputBuffers[channel], frames, e.AsioSampleType, buffer);
            _peaks[channel] = Math.Max(_peaks[channel], peak);
            _clipped[channel] |= peak >= 0.999f;
            if (channel == _tunerChannel)
            {
                PushTuner(buffer, frames);
            }

            if (_monitoring)
            {
                var source = _liveSource[channel];
                var processor = _liveProcessor[channel];
                if (source is { AnyEnabled: true } && processor is not null)
                {
                    if (processor.AppliedVersion != source.Version)
                    {
                        processor.CopyFrom(source);
                    }

                    Array.Copy(buffer, _liveL, frames);
                    Array.Copy(buffer, _liveR, frames);
                    processor.Process(_liveL, _liveR, frames);
                    for (var i = 0; i < frames; i++)
                    {
                        _monitorL[i] += _liveL[i];
                        _monitorR[i] += _liveR[i];
                    }
                }
                else
                {
                    for (var i = 0; i < frames; i++)
                    {
                        _monitorL[i] += buffer[i];
                        _monitorR[i] += buffer[i];
                    }
                }
            }
        }

        if (_waitingForInput)
        {
            CheckTrigger(frames);
        }

        Array.Clear(_mixL, 0, frames);
        Array.Clear(_mixR, 0, frames);
        Array.Clear(_synthBuf, 0, frames);
        Array.Clear(_drumBuf, 0, frames);
        Array.Clear(_machineBufs[0], 0, frames);
        Array.Clear(_machineBufs[1], 0, frames);
        var midiPlayed = false;
        if (_countingIn)
        {
            RenderCountIn(frames);
        }
        else if (_playing)
        {
            PlaySegments(frames);
            midiPlayed = true;
        }
        else
        {
            RecordGateOpen = IsRecording && !_waitingForInput;
            if (IsRecording && !_waitingForInput)
            {
                WriteRecorders(0, frames, Position);
            }
        }

        var probe = Volatile.Read(ref _probe);
        probe?.Process(_inputBufs[probe.Input], _mixL, _mixR, frames);

        if (!midiPlayed)
        {
            Synth.Render(_synthBuf, 0, frames);
            Drums.Render(_drumBuf, 0, frames);
            for (var m = 0; m < Machines.Length; m++)
            {
                Machines[m].Voice.Render(_machineBufs[m], 0, frames);
            }
        }

        Mixer.AddPanned(_synthBuf, SynthGain, SynthPan, _mixL, _mixR, frames);
        Mixer.AddPanned(_drumBuf, DrumGain, DrumPan, _mixL, _mixR, frames);
        for (var m = 0; m < Machines.Length; m++)
        {
            Mixer.AddPanned(_machineBufs[m], Machines[m].Gain, Machines[m].Pan, _mixL, _mixR, frames);
        }
        for (var i = 0; i < frames; i++)
        {
            _mixL[i] = Mixer.SoftLimit(_mixL[i] + _monitorL[i]);
            _mixR[i] = Mixer.SoftLimit(_mixR[i] + _monitorR[i]);
        }

        for (var channel = 0; channel < e.OutputBuffers.Length; channel++)
        {
            WriteOutput(e.OutputBuffers[channel], frames, e.AsioSampleType, channel % 2 == 0 ? _mixL : _mixR);
        }

        e.WrittenToOutputBuffers = true;
    }

    private void EnsureBuffers(int frames)
    {
        if (_scratch.Length >= frames && _inputBufs[0]?.Length >= frames)
        {
            return;
        }

        for (var i = 0; i < MaxInputs; i++)
        {
            _inputBufs[i] = new float[Math.Max(frames, 4096)];
        }

        _scratch = new float[frames];
        _monitorL = new float[frames];
        _monitorR = new float[frames];
        _liveL = new float[frames];
        _liveR = new float[frames];
        _mixL = new float[frames];
        _mixR = new float[frames];
        _synthBuf = new float[frames];
        _drumBuf = new float[frames];
        _machineBufs[0] = new float[frames];
        _machineBufs[1] = new float[frames];
        _outInt = new int[frames];
    }

    private void PlaySegments(int frames)
    {
        var startPosition = Volatile.Read(ref _position);
        var position = startPosition;
        var tracks = _tracks;
        var channels = _channels;
        var midi = _midi;
        var drumMidi = _drumMidi;
        var anySolo = Mixer.AnySolo(tracks, midi) || Mixer.AnySolo([], drumMidi) || Machines.Any(l => Mixer.AnySolo([], l.Clips));
        var speed = Speed;
        var loopStart = (long)(LoopStart / speed);
        var loopEnd = (long)(LoopEnd / speed);
        var punchIn = (long)(PunchIn / speed);
        var punchOut = (long)(PunchOut / speed);
        var loopOn = LoopEnabled && loopEnd > loopStart;
        var punchOn = PunchEnabled && punchOut > punchIn;
        var done = 0;
        while (done < frames)
        {
            var chunk = frames - done;
            if (loopOn && position < loopEnd)
            {
                chunk = (int)Math.Min(chunk, loopEnd - position);
            }

            if (punchOn && position < punchIn)
            {
                chunk = (int)Math.Min(chunk, punchIn - position);
            }
            else if (punchOn && position < punchOut)
            {
                chunk = (int)Math.Min(chunk, punchOut - position);
            }

            Mixer.MixChannels(channels, anySolo, position, _mixL, _mixR, chunk, done, _fxScratch);
            _sequencer.Render(Synth, midi, anySolo, position, _synthBuf, chunk, done);
            _drumSequencer.Render(Drums, drumMidi, anySolo, position, _drumBuf, chunk, done);
            for (var m = 0; m < Machines.Length; m++)
            {
                Machines[m].Sequencer.Render(Machines[m].Voice, Machines[m].Clips, anySolo, position, _machineBufs[m], chunk, done);
            }

            if (_metronomeEnabled)
            {
                MixClick(position, chunk, done);
            }

            var gate = IsRecording && (!punchOn || (position >= punchIn && position < punchOut));
            RecordGateOpen = gate;
            if (gate)
            {
                WriteRecorders(done, chunk, position);
            }

            position += chunk;
            done += chunk;
            if (loopOn && position == loopEnd)
            {
                if (IsRecording)
                {
                    lock (_wrapsLock)
                    {
                        _wraps.Add(_recordedFrames);
                    }
                }

                position = loopStart;
                Synth.AllNotesOff();
            }

            if (punchOn && IsRecording && position >= punchOut)
            {
                PunchCompleted = true;
            }
        }

        Interlocked.CompareExchange(ref _position, position, startPosition);
    }

    private void CheckTrigger(int frames)
    {
        var threshold = TriggerThreshold;
        for (var channel = 0; channel < MaxInputs; channel++)
        {
            if (_recorders[channel] is null)
            {
                continue;
            }

            var buffer = _inputBufs[channel];
            for (var i = 0; i < frames; i++)
            {
                if (Math.Abs(buffer[i]) > threshold)
                {
                    ReleaseWait();
                    return;
                }
            }
        }
    }

    private void WriteRecorders(int offset, int count, long position)
    {
        if (_recordedFrames == 0)
        {
            RecordStartSample = position;
        }

        for (var channel = 0; channel < MaxInputs; channel++)
        {
            _recorders[channel]?.Write(_inputBufs[channel], offset, count);
        }

        _recordedFrames += count;
    }

    private void RenderCountIn(int frames)
    {
        var samplesPerBeat = SamplesPerBeat;
        var clickLength = SampleRate / 50;
        for (var i = 0; i < frames; i++)
        {
            var t = _countInPos + i - _countInLead;
            if (t < 0)
            {
                continue;
            }

            var beat = (long)(t / samplesPerBeat);
            if (beat >= _countInBeats)
            {
                continue;
            }

            var offset = (int)(t - (beat * samplesPerBeat));
            if (offset >= clickLength)
            {
                continue;
            }

            var frequency = beat % BeatsPerBar == 0 ? 1500.0 : 1000.0;
            var envelope = 1f - ((float)offset / clickLength);
            var click = (float)Math.Sin(2 * Math.PI * frequency * offset / SampleRate) * envelope * 0.4f;
            _mixL[i] += click;
            _mixR[i] += click;
        }

        _countInPos += frames;
        if (_countInPos >= _countInEnd)
        {
            _countingIn = false;
            _playing = true;
        }
    }

    private void MixClick(long position, int frames, int destOffset)
    {
        var samplesPerBeat = SamplesPerBeat / Speed;
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

            var frequency = beat % BeatsPerBar == 0 ? 1500.0 : 1000.0;
            var envelope = 1f - ((float)offset / clickLength);
            var click = (float)Math.Sin(2 * Math.PI * frequency * offset / SampleRate) * envelope * 0.4f;
            _mixL[destOffset + i] += click;
            _mixR[destOffset + i] += click;
        }
    }

    private void WriteOutput(IntPtr destination, int frames, AsioSampleType type, float[] source)
    {
        if (type == AsioSampleType.Float32LSB)
        {
            Marshal.Copy(source, 0, destination, frames);
            return;
        }

        for (var i = 0; i < frames; i++)
        {
            _outInt[i] = (int)(Math.Clamp(source[i], -1f, 1f) * 2147483647f);
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
