using System.Runtime.InteropServices;

namespace Overdub.Audio.Vst3;

internal abstract unsafe class HostObject : IDisposable
{
    private readonly IntPtr _block;
    private GCHandle _handle;
    private int _references = 1;

    protected HostObject(IntPtr vtable, params Guid[] interfaces)
    {
        Interfaces = [Vst3Ids.FUnknown, .. interfaces];
        _handle = GCHandle.Alloc(this);
        _block = Marshal.AllocHGlobal(IntPtr.Size * 2);
        ((IntPtr*)_block)[0] = vtable;
        ((IntPtr*)_block)[1] = GCHandle.ToIntPtr(_handle);
    }

    public IntPtr Pointer => _block;

    protected Guid[] Interfaces { get; }

    public static HostObject? From(IntPtr self) => self == IntPtr.Zero ? null : GCHandle.FromIntPtr(((IntPtr*)self)[1]).Target as HostObject;

    public static IntPtr CreateVTable(int slots, params (int Slot, IntPtr Function)[] methods)
    {
        var table = (IntPtr*)Marshal.AllocHGlobal(IntPtr.Size * slots);
        table[0] = (IntPtr)(delegate* unmanaged<IntPtr, Guid*, IntPtr*, int>)&QueryInterfaceThunk;
        table[1] = (IntPtr)(delegate* unmanaged<IntPtr, uint>)&AddRefThunk;
        table[2] = (IntPtr)(delegate* unmanaged<IntPtr, uint>)&ReleaseThunk;
        foreach (var (slot, function) in methods)
        {
            table[slot] = function;
        }

        return (IntPtr)table;
    }

    public void Dispose()
    {
        if (_handle.IsAllocated)
        {
            _handle.Free();
            Marshal.FreeHGlobal(_block);
        }
    }

    [UnmanagedCallersOnly]
    private static int QueryInterfaceThunk(IntPtr self, Guid* iid, IntPtr* result)
    {
        var instance = From(self);
        if (instance is not null && instance.Interfaces.Contains(*iid))
        {
            Interlocked.Increment(ref instance._references);
            *result = self;
            return Com.Ok;
        }

        *result = IntPtr.Zero;
        return Com.NoInterface;
    }

    [UnmanagedCallersOnly]
    private static uint AddRefThunk(IntPtr self) => (uint)Interlocked.Increment(ref From(self)!._references);

    [UnmanagedCallersOnly]
    private static uint ReleaseThunk(IntPtr self) => (uint)Math.Max(1, Interlocked.Decrement(ref From(self)!._references));
}

internal sealed unsafe class HostApplication : HostObject
{
    private static readonly IntPtr Table = CreateVTable(
        5,
        (3, (IntPtr)(delegate* unmanaged<IntPtr, char*, int>)&GetName),
        (4, (IntPtr)(delegate* unmanaged<IntPtr, Guid*, Guid*, IntPtr*, int>)&CreateInstance));

    public HostApplication()
        : base(Table, Vst3Ids.HostApplication)
    {
    }

    [UnmanagedCallersOnly]
    private static int GetName(IntPtr self, char* name)
    {
        const string text = "Overdub";
        for (var i = 0; i < text.Length; i++)
        {
            name[i] = text[i];
        }

        name[text.Length] = '\0';
        return Com.Ok;
    }

    [UnmanagedCallersOnly]
    private static int CreateInstance(IntPtr self, Guid* classId, Guid* interfaceId, IntPtr* result)
    {
        *result = IntPtr.Zero;
        return Com.NotImplemented;
    }
}

internal sealed unsafe class ComponentHandler : HostObject
{
    private static readonly IntPtr Table = CreateVTable(
        7,
        (3, (IntPtr)(delegate* unmanaged<IntPtr, uint, int>)&BeginEdit),
        (4, (IntPtr)(delegate* unmanaged<IntPtr, uint, double, int>)&PerformEdit),
        (5, (IntPtr)(delegate* unmanaged<IntPtr, uint, int>)&EndEdit),
        (6, (IntPtr)(delegate* unmanaged<IntPtr, int, int>)&RestartComponent));

    public ComponentHandler()
        : base(Table, Vst3Ids.ComponentHandler)
    {
    }

    public event Action<uint, double>? ParameterEdited;

    public event Action<int>? Restarted;

    [UnmanagedCallersOnly]
    private static int BeginEdit(IntPtr self, uint id) => Com.Ok;

    [UnmanagedCallersOnly]
    private static int PerformEdit(IntPtr self, uint id, double value)
    {
        (From(self) as ComponentHandler)?.ParameterEdited?.Invoke(id, value);
        return Com.Ok;
    }

    [UnmanagedCallersOnly]
    private static int EndEdit(IntPtr self, uint id) => Com.Ok;

    [UnmanagedCallersOnly]
    private static int RestartComponent(IntPtr self, int flags)
    {
        (From(self) as ComponentHandler)?.Restarted?.Invoke(flags);
        return Com.Ok;
    }
}

internal sealed unsafe class ParamQueue : HostObject
{
    private static readonly IntPtr Table = CreateVTable(
        7,
        (3, (IntPtr)(delegate* unmanaged<IntPtr, uint>)&GetParameterId),
        (4, (IntPtr)(delegate* unmanaged<IntPtr, int>)&GetPointCount),
        (5, (IntPtr)(delegate* unmanaged<IntPtr, int, int*, double*, int>)&GetPoint),
        (6, (IntPtr)(delegate* unmanaged<IntPtr, int, double, int*, int>)&AddPoint));

    public ParamQueue(uint id)
        : base(Table, Vst3Ids.ParamValueQueue)
    {
        Id = id;
    }

    public uint Id { get; }

    public List<(int Offset, double Value)> Points { get; } = [];

    [UnmanagedCallersOnly]
    private static uint GetParameterId(IntPtr self) => ((ParamQueue)From(self)!).Id;

    [UnmanagedCallersOnly]
    private static int GetPointCount(IntPtr self) => ((ParamQueue)From(self)!).Points.Count;

    [UnmanagedCallersOnly]
    private static int GetPoint(IntPtr self, int index, int* offset, double* value)
    {
        var queue = (ParamQueue)From(self)!;
        if (index < 0 || index >= queue.Points.Count)
        {
            return Com.False;
        }

        *offset = queue.Points[index].Offset;
        *value = queue.Points[index].Value;
        return Com.Ok;
    }

    [UnmanagedCallersOnly]
    private static int AddPoint(IntPtr self, int offset, double value, int* index)
    {
        var queue = (ParamQueue)From(self)!;
        queue.Points.Add((offset, value));
        *index = queue.Points.Count - 1;
        return Com.Ok;
    }
}

internal sealed unsafe class ParameterChanges : HostObject
{
    private static readonly IntPtr Table = CreateVTable(
        6,
        (3, (IntPtr)(delegate* unmanaged<IntPtr, int>)&GetParameterCount),
        (4, (IntPtr)(delegate* unmanaged<IntPtr, int, IntPtr>)&GetParameterData),
        (5, (IntPtr)(delegate* unmanaged<IntPtr, uint*, int*, IntPtr>)&AddParameterData));

    private readonly List<ParamQueue> _queues = [];

    public ParameterChanges()
        : base(Table, Vst3Ids.ParameterChanges)
    {
    }

    public void Clear()
    {
        foreach (var queue in _queues)
        {
            queue.Dispose();
        }

        _queues.Clear();
    }

    public void Add(uint id, double value)
    {
        var queue = _queues.FirstOrDefault(q => q.Id == id);
        if (queue is null)
        {
            queue = new ParamQueue(id);
            _queues.Add(queue);
        }

        queue.Points.Clear();
        queue.Points.Add((0, value));
    }

    [UnmanagedCallersOnly]
    private static int GetParameterCount(IntPtr self) => ((ParameterChanges)From(self)!)._queues.Count;

    [UnmanagedCallersOnly]
    private static IntPtr GetParameterData(IntPtr self, int index)
    {
        var changes = (ParameterChanges)From(self)!;
        return index >= 0 && index < changes._queues.Count ? changes._queues[index].Pointer : IntPtr.Zero;
    }

    [UnmanagedCallersOnly]
    private static IntPtr AddParameterData(IntPtr self, uint* id, int* index)
    {
        var changes = (ParameterChanges)From(self)!;
        var queue = changes._queues.FirstOrDefault(q => q.Id == *id);
        if (queue is null)
        {
            queue = new ParamQueue(*id);
            changes._queues.Add(queue);
        }

        *index = changes._queues.IndexOf(queue);
        return queue.Pointer;
    }
}

internal sealed unsafe class EventList : HostObject
{
    private static readonly IntPtr Table = CreateVTable(
        6,
        (3, (IntPtr)(delegate* unmanaged<IntPtr, int>)&GetEventCount),
        (4, (IntPtr)(delegate* unmanaged<IntPtr, int, VstEvent*, int>)&GetEvent),
        (5, (IntPtr)(delegate* unmanaged<IntPtr, VstEvent*, int>)&AddEvent));

    public EventList()
        : base(Table, Vst3Ids.EventList)
    {
    }

    public List<VstEvent> Events { get; } = [];

    [UnmanagedCallersOnly]
    private static int GetEventCount(IntPtr self) => ((EventList)From(self)!).Events.Count;

    [UnmanagedCallersOnly]
    private static int GetEvent(IntPtr self, int index, VstEvent* result)
    {
        var list = (EventList)From(self)!;
        if (index < 0 || index >= list.Events.Count)
        {
            return Com.False;
        }

        *result = list.Events[index];
        return Com.Ok;
    }

    [UnmanagedCallersOnly]
    private static int AddEvent(IntPtr self, VstEvent* value)
    {
        ((EventList)From(self)!).Events.Add(*value);
        return Com.Ok;
    }
}

internal sealed unsafe class MemoryStreamObject : HostObject
{
    private static readonly IntPtr Table = CreateVTable(
        7,
        (3, (IntPtr)(delegate* unmanaged<IntPtr, byte*, int, int*, int>)&Read),
        (4, (IntPtr)(delegate* unmanaged<IntPtr, byte*, int, int*, int>)&Write),
        (5, (IntPtr)(delegate* unmanaged<IntPtr, long, int, long*, int>)&Seek),
        (6, (IntPtr)(delegate* unmanaged<IntPtr, long*, int>)&Tell));

    private readonly MemoryStream _stream;

    public MemoryStreamObject(byte[]? content = null)
        : base(Table, Vst3Ids.BStream)
    {
        _stream = content is null ? new MemoryStream() : new MemoryStream(content);
    }

    public byte[] ToArray() => _stream.ToArray();

    [UnmanagedCallersOnly]
    private static int Read(IntPtr self, byte* buffer, int count, int* read)
    {
        var stream = ((MemoryStreamObject)From(self)!)._stream;
        var span = new Span<byte>(buffer, count);
        var n = stream.Read(span);
        if (read is not null)
        {
            *read = n;
        }

        return Com.Ok;
    }

    [UnmanagedCallersOnly]
    private static int Write(IntPtr self, byte* buffer, int count, int* written)
    {
        var stream = ((MemoryStreamObject)From(self)!)._stream;
        stream.Write(new ReadOnlySpan<byte>(buffer, count));
        if (written is not null)
        {
            *written = count;
        }

        return Com.Ok;
    }

    [UnmanagedCallersOnly]
    private static int Seek(IntPtr self, long position, int mode, long* result)
    {
        var stream = ((MemoryStreamObject)From(self)!)._stream;
        var origin = mode switch
        {
            1 => SeekOrigin.Current,
            2 => SeekOrigin.End,
            _ => SeekOrigin.Begin,
        };
        var target = stream.Seek(position, origin);
        if (result is not null)
        {
            *result = target;
        }

        return Com.Ok;
    }

    [UnmanagedCallersOnly]
    private static int Tell(IntPtr self, long* position)
    {
        *position = ((MemoryStreamObject)From(self)!)._stream.Position;
        return Com.Ok;
    }
}

internal sealed unsafe class PlugFrame : HostObject
{
    private static readonly IntPtr Table = CreateVTable(
        4,
        (3, (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, ViewRect*, int>)&ResizeView));

    public PlugFrame()
        : base(Table, Vst3Ids.PlugFrame)
    {
    }

    public event Action<int, int>? ResizeRequested;

    [UnmanagedCallersOnly]
    private static int ResizeView(IntPtr self, IntPtr view, ViewRect* size)
    {
        (From(self) as PlugFrame)?.ResizeRequested?.Invoke(size->Right - size->Left, size->Bottom - size->Top);
        return Com.Ok;
    }
}
