namespace Overdub.Audio;

public sealed class Sends
{
    public float Reverb { get; set; }
    public float Delay { get; set; }
    public bool Any => Reverb > 0.001f || Delay > 0.001f;
}

public sealed class EchoDelay
{
    private float[] _left = new float[1];
    private float[] _right = new float[1];
    private float _lowL;
    private float _lowR;
    private int _position;

    public void Configure(int sampleRate)
    {
        _left = new float[sampleRate * 3];
        _right = new float[sampleRate * 3];
        _position = 0;
        _lowL = 0;
        _lowR = 0;
    }

    public void Process(float[] left, float[] right, int frames, double delaySamples)
    {
        var length = _left.Length;
        var delay = (int)Math.Clamp(delaySamples, 1, length - 1);
        for (var i = 0; i < frames; i++)
        {
            var read = (_position - delay + length) % length;
            var outL = _left[read];
            var outR = _right[read];
            _lowL += 0.45f * (outL - _lowL);
            _lowR += 0.45f * (outR - _lowR);
            _left[_position] = left[i] + (_lowR * 0.42f);
            _right[_position] = right[i] + (_lowL * 0.42f);
            left[i] = outL;
            right[i] = outR;
            _position = (_position + 1) % length;
        }
    }
}

public sealed class SendBus
{
    private readonly Reverb _reverb = new();
    private readonly EchoDelay _echo = new();
    private float[] _reverbLeft = new float[4096];
    private float[] _reverbRight = new float[4096];
    private float[] _delayLeft = new float[4096];
    private float[] _delayRight = new float[4096];

    public SendBus()
    {
        _reverb.Initialize();
        _reverb.Set(0, 0.75);
        _reverb.Set(1, 0.5);
        _reverb.Set(2, 1.0);
        _reverb.Enabled = true;
    }

    public void Configure(int sampleRate)
    {
        _reverb.Configure(sampleRate);
        _echo.Configure(sampleRate);
    }

    public void Clear(int frames)
    {
        if (_reverbLeft.Length < frames)
        {
            _reverbLeft = new float[frames];
            _reverbRight = new float[frames];
            _delayLeft = new float[frames];
            _delayRight = new float[frames];
        }

        Array.Clear(_reverbLeft, 0, frames);
        Array.Clear(_reverbRight, 0, frames);
        Array.Clear(_delayLeft, 0, frames);
        Array.Clear(_delayRight, 0, frames);
    }

    public void Add(float[] left, float[] right, int sourceOffset, int destinationOffset, int frames, Sends? sends)
    {
        if (sends is not { Any: true })
        {
            return;
        }

        for (var i = 0; i < frames; i++)
        {
            var l = left[sourceOffset + i];
            var r = right[sourceOffset + i];
            _reverbLeft[destinationOffset + i] += l * sends.Reverb;
            _reverbRight[destinationOffset + i] += r * sends.Reverb;
            _delayLeft[destinationOffset + i] += l * sends.Delay;
            _delayRight[destinationOffset + i] += r * sends.Delay;
        }
    }

    public void AddMono(float[] mono, float gain, int frames, Sends? sends)
    {
        if (sends is not { Any: true })
        {
            return;
        }

        for (var i = 0; i < frames; i++)
        {
            var x = mono[i] * gain;
            _reverbLeft[i] += x * sends.Reverb;
            _reverbRight[i] += x * sends.Reverb;
            _delayLeft[i] += x * sends.Delay;
            _delayRight[i] += x * sends.Delay;
        }
    }

    public void Render(float[] left, float[] right, int frames, double samplesPerBeat)
    {
        _reverb.Process(_reverbLeft, _reverbRight, frames);
        _echo.Process(_delayLeft, _delayRight, frames, samplesPerBeat * 0.75);
        for (var i = 0; i < frames; i++)
        {
            left[i] += _reverbLeft[i] + _delayLeft[i];
            right[i] += _reverbRight[i] + _delayRight[i];
        }
    }
}
