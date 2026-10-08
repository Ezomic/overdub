using System.Runtime.InteropServices;

namespace Overdub.Audio.Vst3;

internal static unsafe class Vst3Ids
{
    public static readonly Guid FUnknown = new("00000000-0000-0000-C000-000000000046");
    public static readonly Guid PluginFactory = new("7A4D811C-5211-4A1F-AED9-D2EE0B43BF9F");
    public static readonly Guid PluginFactory2 = new("0007B650-F24B-4C0B-A464-EDB9F00B2ABB");
    public static readonly Guid Component = new("E831FF31-F2D5-4301-928E-BBEE25697802");
    public static readonly Guid AudioProcessor = new("42043F99-B7DA-453C-A569-E79D9AAEC33D");
    public static readonly Guid EditController = new("DCD7BBE3-7742-448D-A874-AACC979C759E");
    public static readonly Guid HostApplication = new("58E595CC-DB2D-4969-8B6A-AF8C36A664E5");
    public static readonly Guid ComponentHandler = new("93A0BEA3-0BD0-45DB-8E89-0B0CC1E46AC6");
    public static readonly Guid ParameterChanges = new("A4779663-0BB6-4A56-B443-84A8466FEB9D");
    public static readonly Guid ParamValueQueue = new("01263A18-ED07-4F6F-98C9-D3564686F9BA");
    public static readonly Guid EventList = new("3A2C4214-3463-49FE-B2E4-F819F5B4CA6E");
    public static readonly Guid BStream = new("C3BF6EA2-3099-4752-9B6B-F9901EE33E9B");
    public static readonly Guid PlugFrame = new("367FAF01-AFA9-4693-8D4D-A2A0ED0882A3");
    public static readonly Guid PlugView = new("5BC32507-D060-49EA-A615-1B522B755B29");
    public static readonly Guid ConnectionPoint = new("70A4156F-6E6E-4026-9891-48BFAA60D8D1");
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PFactoryInfo
{
    public fixed byte Vendor[64];
    public fixed byte Url[256];
    public fixed byte Email[128];
    public int Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PClassInfo2
{
    public fixed byte Cid[16];
    public int Cardinality;
    public fixed byte Category[32];
    public fixed byte Name[64];
    public uint ClassFlags;
    public fixed byte SubCategories[128];
    public fixed byte Vendor[64];
    public fixed byte Version[64];
    public fixed byte SdkVersion[64];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PClassInfo
{
    public fixed byte Cid[16];
    public int Cardinality;
    public fixed byte Category[32];
    public fixed byte Name[64];
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct BusInfo
{
    public int MediaType;
    public int Direction;
    public int ChannelCount;
    public fixed char Name[128];
    public int BusType;
    public uint Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ParameterInfoNative
{
    public uint Id;
    public fixed char Title[128];
    public fixed char ShortTitle[128];
    public fixed char Units[128];
    public int StepCount;
    public double DefaultNormalized;
    public int UnitId;
    public int Flags;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ProcessSetup
{
    public int ProcessMode;
    public int SymbolicSampleSize;
    public int MaxSamplesPerBlock;
    public double SampleRate;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct AudioBusBuffers
{
    public int NumChannels;
    public ulong SilenceFlags;
    public float** ChannelBuffers32;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ProcessData
{
    public int ProcessMode;
    public int SymbolicSampleSize;
    public int NumSamples;
    public int NumInputs;
    public int NumOutputs;
    public AudioBusBuffers* Inputs;
    public AudioBusBuffers* Outputs;
    public IntPtr InputParameterChanges;
    public IntPtr OutputParameterChanges;
    public IntPtr InputEvents;
    public IntPtr OutputEvents;
    public IntPtr ProcessContext;
}

[StructLayout(LayoutKind.Explicit, Size = 48)]
internal struct VstEvent
{
    [FieldOffset(0)] public int BusIndex;
    [FieldOffset(4)] public int SampleOffset;
    [FieldOffset(8)] public double PpqPosition;
    [FieldOffset(16)] public ushort Flags;
    [FieldOffset(18)] public ushort Type;
    [FieldOffset(24)] public short NoteChannel;
    [FieldOffset(26)] public short NotePitch;
    [FieldOffset(28)] public float NoteTuningOrVelocity;
    [FieldOffset(32)] public float NoteVelocityOrId;
    [FieldOffset(36)] public int NoteLength;
    [FieldOffset(40)] public int NoteId;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ViewRect
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;
}

internal static unsafe class Com
{
    public const int Ok = 0;
    public const int False = 1;
    public const int NoInterface = unchecked((int)0x80004002);
    public const int NotImplemented = unchecked((int)0x80004001);

    public static IntPtr* VTable(IntPtr instance) => *(IntPtr**)instance;

    public static uint AddRef(IntPtr instance) => ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(instance)[1])(instance);

    public static uint Release(IntPtr instance) => ((delegate* unmanaged[Stdcall]<IntPtr, uint>)VTable(instance)[2])(instance);

    public static int QueryInterface(IntPtr instance, Guid iid, out IntPtr result)
    {
        IntPtr found;
        var status = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)VTable(instance)[0])(instance, &iid, &found);
        result = status == Ok ? found : IntPtr.Zero;
        return status;
    }

    public static string Ascii(byte* text, int max)
    {
        var length = 0;
        while (length < max && text[length] != 0)
        {
            length++;
        }

        return System.Text.Encoding.ASCII.GetString(text, length);
    }

    public static string Utf16(char* text, int max)
    {
        var length = 0;
        while (length < max && text[length] != 0)
        {
            length++;
        }

        return new string(text, 0, length);
    }
}
