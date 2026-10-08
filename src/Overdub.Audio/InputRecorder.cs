using System.Collections.Concurrent;
using NAudio.Wave;

namespace Overdub.Audio;

internal sealed class InputRecorder : IDisposable
{
    private readonly BlockingCollection<float[]> _queue = new(boundedCapacity: 1024);
    private readonly WaveFileWriter _writer;
    private readonly Thread _thread;

    public InputRecorder(string path, int sampleRate)
    {
        _writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1));
        _thread = new Thread(Drain) { IsBackground = true, Name = "Overdub recorder" };
        _thread.Start();
    }

    public string Path => _writer.Filename;

    public void Write(float[] samples, int count)
    {
        var copy = new float[count];
        Array.Copy(samples, copy, count);
        _queue.TryAdd(copy);
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        _thread.Join();
        _writer.Dispose();
    }

    private void Drain()
    {
        foreach (var block in _queue.GetConsumingEnumerable())
        {
            _writer.WriteSamples(block, 0, block.Length);
        }
    }
}
