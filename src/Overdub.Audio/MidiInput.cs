using NAudio.Midi;

namespace Overdub.Audio;

public sealed class MidiInput : IDisposable
{
    private MidiIn? _device;

    public event Action<byte, byte>? NoteReceived;
    public event Action<MidiKind, int>? ControlReceived;

    public bool IsOpen => _device is not null;
    public string? DeviceName { get; private set; }

    public static IReadOnlyList<string> GetDeviceNames() =>
        Enumerable.Range(0, MidiIn.NumberOfDevices).Select(i => MidiIn.DeviceInfo(i).ProductName).ToList();

    public void Open(int deviceIndex)
    {
        Close();
        _device = new MidiIn(deviceIndex);
        _device.MessageReceived += OnMessage;
        _device.Start();
        DeviceName = MidiIn.DeviceInfo(deviceIndex).ProductName;
    }

    public void Close()
    {
        if (_device is null)
        {
            return;
        }

        _device.Stop();
        _device.MessageReceived -= OnMessage;
        _device.Dispose();
        _device = null;
        DeviceName = null;
    }

    public void Dispose() => Close();

    private void OnMessage(object? sender, MidiInMessageEventArgs e)
    {
        switch (e.MidiEvent)
        {
            case NoteOnEvent on:
                NoteReceived?.Invoke((byte)on.NoteNumber, (byte)on.Velocity);
                break;
            case ControlChangeEvent { Controller: MidiController.Sustain } sustain:
                ControlReceived?.Invoke(MidiKind.Sustain, sustain.ControllerValue);
                break;
            case PitchWheelChangeEvent wheel:
                ControlReceived?.Invoke(MidiKind.PitchBend, wheel.Pitch - 8192);
                break;
            case NoteEvent { CommandCode: MidiCommandCode.NoteOff } off:
                NoteReceived?.Invoke((byte)off.NoteNumber, 0);
                break;
        }
    }
}
