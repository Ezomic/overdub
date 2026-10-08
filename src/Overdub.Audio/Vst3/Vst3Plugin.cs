using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Overdub.Audio.Vst3;

public sealed record Vst3PluginInfo(string Path, string Name, string Vendor, string Category, string SubCategories, Guid ClassId)
{
    public bool IsInstrument => SubCategories.Contains("Instrument", StringComparison.OrdinalIgnoreCase);

    public bool IsEffect => !IsInstrument;
}

public sealed record Vst3Parameter(uint Id, string Title, string Units, int StepCount, double Default, bool IsHidden, bool IsReadOnly, bool IsBypass);

public sealed record Vst3State(byte[] Component, byte[] Controller);

internal sealed unsafe class Vst3Module : IDisposable
{
    private readonly IntPtr _library;

    private Vst3Module(IntPtr library, IntPtr factory)
    {
        _library = library;
        Factory = factory;
    }

    public IntPtr Factory { get; private set; }

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);

    public static Vst3Module Load(string path)
    {
        var library = LoadLibraryEx(path, IntPtr.Zero, 0x8);
        if (library == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not load {System.IO.Path.GetFileName(path)}.");
        }

        if (NativeLibrary.TryGetExport(library, "InitDll", out var init))
        {
            ((delegate* unmanaged<byte>)init)();
        }

        var factory = NativeLibrary.TryGetExport(library, "GetPluginFactory", out var get)
            ? ((delegate* unmanaged<IntPtr>)get)()
            : IntPtr.Zero;
        if (factory == IntPtr.Zero)
        {
            NativeLibrary.Free(library);
            throw new InvalidOperationException($"{System.IO.Path.GetFileName(path)} is not a VST3 plugin.");
        }

        return new Vst3Module(library, factory);
    }

    public IReadOnlyList<Vst3PluginInfo> Describe(string path)
    {
        var result = new List<Vst3PluginInfo>();
        var vendorFallback = string.Empty;
        PFactoryInfo info;
        if (((delegate* unmanaged[Stdcall]<IntPtr, PFactoryInfo*, int>)Com.VTable(Factory)[3])(Factory, &info) == Com.Ok)
        {
            vendorFallback = Com.Ascii(info.Vendor, 64);
        }

        var count = ((delegate* unmanaged[Stdcall]<IntPtr, int>)Com.VTable(Factory)[4])(Factory);
        var hasFactory2 = Com.QueryInterface(Factory, Vst3Ids.PluginFactory2, out var factory2) == Com.Ok;
        for (var i = 0; i < count; i++)
        {
            if (hasFactory2)
            {
                PClassInfo2 details;
                if (((delegate* unmanaged[Stdcall]<IntPtr, int, PClassInfo2*, int>)Com.VTable(factory2)[7])(factory2, i, &details) == Com.Ok
                    && Com.Ascii(details.Category, 32) == "Audio Module Class")
                {
                    var vendor = Com.Ascii(details.Vendor, 64);
                    result.Add(new Vst3PluginInfo(path, Com.Ascii(details.Name, 64), vendor.Length > 0 ? vendor : vendorFallback,
                        "Audio Module Class", Com.Ascii(details.SubCategories, 128), new Guid(new ReadOnlySpan<byte>(details.Cid, 16))));
                }
            }
            else
            {
                PClassInfo basic;
                if (((delegate* unmanaged[Stdcall]<IntPtr, int, PClassInfo*, int>)Com.VTable(Factory)[5])(Factory, i, &basic) == Com.Ok
                    && Com.Ascii(basic.Category, 32) == "Audio Module Class")
                {
                    result.Add(new Vst3PluginInfo(path, Com.Ascii(basic.Name, 64), vendorFallback, "Audio Module Class", string.Empty, new Guid(new ReadOnlySpan<byte>(basic.Cid, 16))));
                }
            }
        }

        if (hasFactory2)
        {
            Com.Release(factory2);
        }

        return result;
    }

    public IntPtr Create(Guid classId, Guid interfaceId)
    {
        IntPtr instance;
        var status = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, Guid*, IntPtr*, int>)Com.VTable(Factory)[6])(Factory, &classId, &interfaceId, &instance);
        return status == Com.Ok ? instance : IntPtr.Zero;
    }

    public void Dispose()
    {
        if (Factory == IntPtr.Zero)
        {
            return;
        }

        Com.Release(Factory);
        Factory = IntPtr.Zero;
        if (NativeLibrary.TryGetExport(_library, "ExitDll", out var exit))
        {
            ((delegate* unmanaged<byte>)exit)();
        }

        NativeLibrary.Free(_library);
    }
}

public static class Vst3Scanner
{
    public static IReadOnlyList<string> DefaultFolders { get; } =
    [
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "VST3"),
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Common", "VST3"),
    ];

    public static IReadOnlyList<Vst3PluginInfo> Scan(IEnumerable<string>? folders = null)
    {
        var found = new List<Vst3PluginInfo>();
        foreach (var folder in folders ?? DefaultFolders)
        {
            if (!Directory.Exists(folder))
            {
                continue;
            }

            foreach (var file in Directory.EnumerateFiles(folder, "*.vst3", SearchOption.AllDirectories))
            {
                try
                {
                    using var module = Vst3Module.Load(file);
                    found.AddRange(module.Describe(file));
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or DllNotFoundException or BadImageFormatException)
                {
                }
            }
        }

        return found.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed unsafe class Vst3Plugin : IDisposable
{
    private const int KAudio = 0;
    private const int KEvent = 1;
    private const int KInput = 0;
    private const int KOutput = 1;
    private const ulong Stereo = 3;

    private readonly Vst3Module _module;
    private readonly HostApplication _host = new();
    private readonly ComponentHandler _handler = new();
    private readonly ParameterChanges _inputChanges = new();
    private readonly ParameterChanges _outputChanges = new();
    private readonly EventList _inputEvents = new();
    private readonly EventList _outputEvents = new();
    private readonly List<(uint Id, double Value)> _pending = [];
    private readonly object _processLock = new();
    private IntPtr _component;
    private IntPtr _processor;
    private IntPtr _controller;
    private bool _controllerIsComponent;
    private bool _active;
    private float[] _outLeft = new float[4096];
    private float[] _outRight = new float[4096];
    private int _inputBuses;
    private int _outputBuses;
    private bool _eventInput;

    private Vst3Plugin(Vst3Module module, Vst3PluginInfo info)
    {
        _module = module;
        Info = info;
    }

    public Vst3PluginInfo Info { get; }

    public int SampleRate { get; private set; }

    public int LatencySamples { get; private set; }

    public bool IsInstrument => Info.IsInstrument;

    public static Vst3Plugin Load(Vst3PluginInfo info, int sampleRate, int maxBlock = 4096)
    {
        var module = Vst3Module.Load(info.Path);
        var plugin = new Vst3Plugin(module, info);
        try
        {
            plugin.Start(sampleRate, maxBlock);
            return plugin;
        }
        catch
        {
            plugin.Dispose();
            throw;
        }
    }

    public int ParameterCount => _controller == IntPtr.Zero ? 0 : ((delegate* unmanaged[Stdcall]<IntPtr, int>)Com.VTable(_controller)[8])(_controller);

    public Vst3Parameter GetParameter(int index)
    {
        ParameterInfoNative native;
        if (((delegate* unmanaged[Stdcall]<IntPtr, int, ParameterInfoNative*, int>)Com.VTable(_controller)[9])(_controller, index, &native) != Com.Ok)
        {
            throw new InvalidOperationException("Could not read the plugin parameter.");
        }

        var flags = native.Flags;
        return new Vst3Parameter(native.Id, Com.Utf16(native.Title, 128), Com.Utf16(native.Units, 128), native.StepCount, native.DefaultNormalized,
            (flags & 16) != 0, (flags & 2) != 0, (flags & 65536) != 0);
    }

    public double GetNormalized(uint id) => ((delegate* unmanaged[Stdcall]<IntPtr, uint, double>)Com.VTable(_controller)[14])(_controller, id);

    public string Format(uint id, double normalized)
    {
        var buffer = stackalloc char[128];
        return ((delegate* unmanaged[Stdcall]<IntPtr, uint, double, char*, int>)Com.VTable(_controller)[10])(_controller, id, normalized, buffer) == Com.Ok
            ? Com.Utf16(buffer, 128)
            : normalized.ToString("0.00");
    }

    public void SetNormalized(uint id, double value)
    {
        value = Math.Clamp(value, 0, 1);
        ((delegate* unmanaged[Stdcall]<IntPtr, uint, double, int>)Com.VTable(_controller)[15])(_controller, id, value);
        lock (_pending)
        {
            _pending.RemoveAll(p => p.Id == id);
            _pending.Add((id, value));
        }
    }

    public void NoteOn(int pitch, int velocity, int channel = 0, int offset = 0) => QueueEvent(0, pitch, velocity / 127f, channel, offset);

    public void NoteOff(int pitch, int channel = 0, int offset = 0) => QueueEvent(1, pitch, 0f, channel, offset);

    private readonly List<VstEvent> _queuedEvents = [];

    private readonly int[] _noteIds = new int[128];
    private int _nextNoteId = 1;

    private void QueueEvent(ushort type, int pitch, float velocity, int channel, int offset)
    {
        pitch = Math.Clamp(pitch, 0, 127);
        var e = new VstEvent { SampleOffset = offset, Type = type, NoteChannel = (short)channel, NotePitch = (short)pitch };
        lock (_queuedEvents)
        {
            if (type == 0)
            {
                var id = _nextNoteId++;
                _noteIds[pitch] = id;
                e.NoteId = id;
                e.NoteTuningOrVelocity = 0f;
                e.NoteVelocityOrId = velocity;
                e.NoteLength = 0;
            }
            else
            {
                e.NoteTuningOrVelocity = 0.5f;
                e.NoteVelocityOrId = BitConverter.Int32BitsToSingle(_noteIds[pitch] == 0 ? -1 : _noteIds[pitch]);
                e.NoteLength = 0;
            }

            _queuedEvents.Add(e);
        }
    }

    public void Process(float[] left, float[] right, int frames)
    {
        lock (_processLock)
        {
            if (!_active)
            {
                return;
            }

            if (_outLeft.Length < frames)
            {
                _outLeft = new float[frames];
                _outRight = new float[frames];
            }

            _inputChanges.Clear();
            lock (_pending)
            {
                foreach (var (id, value) in _pending)
                {
                    _inputChanges.Add(id, value);
                }

                _pending.Clear();
            }

            _outputChanges.Clear();
            _inputEvents.Events.Clear();
            lock (_queuedEvents)
            {
                _inputEvents.Events.AddRange(_queuedEvents);
                _queuedEvents.Clear();
            }

            _outputEvents.Events.Clear();
            fixed (float* inL = left, inR = right, outL = _outLeft, outR = _outRight)
            {
                var inChannels = stackalloc float*[2];
                inChannels[0] = inL;
                inChannels[1] = inR;
                var outChannels = stackalloc float*[2];
                outChannels[0] = outL;
                outChannels[1] = outR;
                var input = new AudioBusBuffers { NumChannels = 2, ChannelBuffers32 = inChannels };
                var output = new AudioBusBuffers { NumChannels = 2, ChannelBuffers32 = outChannels };
                var data = new ProcessData
                {
                    ProcessMode = 0,
                    SymbolicSampleSize = 0,
                    NumSamples = frames,
                    NumInputs = _inputBuses > 0 ? 1 : 0,
                    NumOutputs = 1,
                    Inputs = _inputBuses > 0 ? &input : null,
                    Outputs = &output,
                    InputParameterChanges = _inputChanges.Pointer,
                    OutputParameterChanges = _outputChanges.Pointer,
                    InputEvents = _inputEvents.Pointer,
                    OutputEvents = _outputEvents.Pointer,
                };
                var status = ((delegate* unmanaged[Stdcall]<IntPtr, ProcessData*, int>)Com.VTable(_processor)[9])(_processor, &data);
                if (status == Com.Ok)
                {
                    Array.Copy(_outLeft, left, frames);
                    Array.Copy(_outRight, right, frames);
                }
            }
        }
    }

    public void FlushParameters() => Process(new float[64], new float[64], 64);

    public Vst3State SaveState()
    {
        lock (_processLock)
        {
            var component = new MemoryStreamObject();
            var controller = new MemoryStreamObject();
            try
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(_component)[13])(_component, component.Pointer);
                if (_controller != IntPtr.Zero)
                {
                    ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(_controller)[7])(_controller, controller.Pointer);
                }

                return new Vst3State(component.ToArray(), controller.ToArray());
            }
            finally
            {
                component.Dispose();
                controller.Dispose();
            }
        }
    }

    public void LoadState(Vst3State state)
    {
        lock (_processLock)
        {
            using var component = new MemoryStreamObject(state.Component);
            if (state.Component.Length > 0)
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(_component)[12])(_component, component.Pointer);
                using var again = new MemoryStreamObject(state.Component);
                if (_controller != IntPtr.Zero)
                {
                    ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(_controller)[5])(_controller, again.Pointer);
                }
            }

            if (_controller != IntPtr.Zero && state.Controller.Length > 0)
            {
                using var controller = new MemoryStreamObject(state.Controller);
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(_controller)[6])(_controller, controller.Pointer);
            }
        }
    }

    public Vst3Editor? CreateEditor()
    {
        if (_controller == IntPtr.Zero)
        {
            return null;
        }

        var name = stackalloc byte[7];
        name[0] = (byte)'e';
        name[1] = (byte)'d';
        name[2] = (byte)'i';
        name[3] = (byte)'t';
        name[4] = (byte)'o';
        name[5] = (byte)'r';
        name[6] = 0;
        var view = ((delegate* unmanaged[Stdcall]<IntPtr, byte*, IntPtr>)Com.VTable(_controller)[17])(_controller, name);
        return view == IntPtr.Zero ? null : new Vst3Editor(view);
    }

    public void Dispose()
    {
        lock (_processLock)
        {
            if (_active)
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)Com.VTable(_processor)[8])(_processor, 0);
                ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)Com.VTable(_component)[11])(_component, 0);
                _active = false;
            }

            if (_controller != IntPtr.Zero)
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, int>)Com.VTable(_controller)[4])(_controller);
                if (!_controllerIsComponent)
                {
                    Com.Release(_controller);
                }

                _controller = IntPtr.Zero;
            }

            if (_processor != IntPtr.Zero)
            {
                Com.Release(_processor);
                _processor = IntPtr.Zero;
            }

            if (_component != IntPtr.Zero)
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, int>)Com.VTable(_component)[4])(_component);
                Com.Release(_component);
                _component = IntPtr.Zero;
            }

            _module.Dispose();
            _host.Dispose();
            _handler.Dispose();
            _inputChanges.Dispose();
            _outputChanges.Dispose();
            _inputEvents.Dispose();
            _outputEvents.Dispose();
        }
    }

    private void Start(int sampleRate, int maxBlock)
    {
        SampleRate = sampleRate;
        _component = _module.Create(Info.ClassId, Vst3Ids.Component);
        if (_component == IntPtr.Zero)
        {
            throw new InvalidOperationException($"{Info.Name} could not be created.");
        }

        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(_component)[3])(_component, _host.Pointer) != Com.Ok)
        {
            throw new InvalidOperationException($"{Info.Name} refused to initialize.");
        }

        if (Com.QueryInterface(_component, Vst3Ids.AudioProcessor, out _processor) != Com.Ok)
        {
            throw new InvalidOperationException($"{Info.Name} has no audio processor.");
        }

        CreateController();
        ConfigureBuses();
        _handler.ParameterEdited += (id, value) =>
        {
            lock (_pending)
            {
                _pending.RemoveAll(p => p.Id == id);
                _pending.Add((id, value));
            }
        };

        var setup = new ProcessSetup { ProcessMode = 0, SymbolicSampleSize = 0, MaxSamplesPerBlock = maxBlock, SampleRate = sampleRate };
        if (((delegate* unmanaged[Stdcall]<IntPtr, ProcessSetup*, int>)Com.VTable(_processor)[7])(_processor, &setup) != Com.Ok)
        {
            throw new InvalidOperationException($"{Info.Name} could not be set up for {sampleRate} Hz.");
        }

        ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)Com.VTable(_component)[11])(_component, 1);
        ((delegate* unmanaged[Stdcall]<IntPtr, byte, int>)Com.VTable(_processor)[8])(_processor, 1);
        LatencySamples = (int)((delegate* unmanaged[Stdcall]<IntPtr, uint>)Com.VTable(_processor)[6])(_processor);
        _active = true;
    }

    private void CreateController()
    {
        if (Com.QueryInterface(_component, Vst3Ids.EditController, out _controller) == Com.Ok)
        {
            _controllerIsComponent = true;
        }
        else
        {
            Guid controllerClass;
            if (((delegate* unmanaged[Stdcall]<IntPtr, Guid*, int>)Com.VTable(_component)[5])(_component, &controllerClass) == Com.Ok)
            {
                _controller = _module.Create(controllerClass, Vst3Ids.EditController);
                if (_controller != IntPtr.Zero)
                {
                    ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(_controller)[3])(_controller, _host.Pointer);
                }
            }
        }

        if (_controller == IntPtr.Zero)
        {
            return;
        }

        if (!_controllerIsComponent
            && Com.QueryInterface(_component, Vst3Ids.ConnectionPoint, out var componentPoint) == Com.Ok
            && Com.QueryInterface(_controller, Vst3Ids.ConnectionPoint, out var controllerPoint) == Com.Ok)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(componentPoint)[3])(componentPoint, controllerPoint);
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(controllerPoint)[3])(controllerPoint, componentPoint);
        }

        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(_controller)[16])(_controller, _handler.Pointer);
        using var state = new MemoryStreamObject();
        if (((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(_component)[13])(_component, state.Pointer) == Com.Ok)
        {
            using var read = new MemoryStreamObject(state.ToArray());
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(_controller)[5])(_controller, read.Pointer);
        }
    }

    private void ConfigureBuses()
    {
        _inputBuses = ((delegate* unmanaged[Stdcall]<IntPtr, int, int, int>)Com.VTable(_component)[7])(_component, KAudio, KInput);
        _outputBuses = ((delegate* unmanaged[Stdcall]<IntPtr, int, int, int>)Com.VTable(_component)[7])(_component, KAudio, KOutput);
        _eventInput = ((delegate* unmanaged[Stdcall]<IntPtr, int, int, int>)Com.VTable(_component)[7])(_component, KEvent, KInput) > 0;

        var inputs = stackalloc ulong[Math.Max(1, _inputBuses)];
        var outputs = stackalloc ulong[Math.Max(1, _outputBuses)];
        for (var i = 0; i < _inputBuses; i++)
        {
            inputs[i] = Stereo;
        }

        for (var i = 0; i < _outputBuses; i++)
        {
            outputs[i] = Stereo;
        }

        ((delegate* unmanaged[Stdcall]<IntPtr, ulong*, int, ulong*, int, int>)Com.VTable(_processor)[3])(_processor, inputs, _inputBuses, outputs, _outputBuses);
        if (_inputBuses > 0)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, int, int, int, byte, int>)Com.VTable(_component)[10])(_component, KAudio, KInput, 0, 1);
        }

        if (_outputBuses > 0)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, int, int, int, byte, int>)Com.VTable(_component)[10])(_component, KAudio, KOutput, 0, 1);
        }

        if (_eventInput)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, int, int, int, byte, int>)Com.VTable(_component)[10])(_component, KEvent, KInput, 0, 1);
        }
    }
}

public sealed unsafe class Vst3Editor : IDisposable
{
    private readonly PlugFrame _frame = new();
    private IntPtr _view;
    private bool _attached;

    internal Vst3Editor(IntPtr view)
    {
        _view = view;
        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(view)[12])(view, _frame.Pointer);
        _frame.ResizeRequested += (w, h) => SizeRequested?.Invoke(w, h);
    }

    public event Action<int, int>? SizeRequested;

    public (int Width, int Height) Size
    {
        get
        {
            ViewRect rect;
            ((delegate* unmanaged[Stdcall]<IntPtr, ViewRect*, int>)Com.VTable(_view)[9])(_view, &rect);
            return (rect.Right - rect.Left, rect.Bottom - rect.Top);
        }
    }

    public bool Attach(IntPtr hwnd)
    {
        var type = stackalloc byte[5];
        type[0] = (byte)'H';
        type[1] = (byte)'W';
        type[2] = (byte)'N';
        type[3] = (byte)'D';
        type[4] = 0;
        if (((delegate* unmanaged[Stdcall]<IntPtr, byte*, int>)Com.VTable(_view)[3])(_view, type) != Com.Ok)
        {
            return false;
        }

        _attached = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, byte*, int>)Com.VTable(_view)[4])(_view, hwnd, type) == Com.Ok;
        return _attached;
    }

    public void Resize(int width, int height)
    {
        var rect = new ViewRect { Left = 0, Top = 0, Right = width, Bottom = height };
        ((delegate* unmanaged[Stdcall]<IntPtr, ViewRect*, int>)Com.VTable(_view)[10])(_view, &rect);
    }

    public void Dispose()
    {
        if (_view == IntPtr.Zero)
        {
            return;
        }

        if (_attached)
        {
            ((delegate* unmanaged[Stdcall]<IntPtr, int>)Com.VTable(_view)[5])(_view);
        }

        ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int>)Com.VTable(_view)[12])(_view, IntPtr.Zero);
        Com.Release(_view);
        _view = IntPtr.Zero;
        _frame.Dispose();
    }
}
