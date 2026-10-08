namespace Overdub.Audio.Vst3;

public sealed class PluginSlot : IDisposable
{
    private readonly List<Vst3Plugin> _retired = [];
    private Vst3PluginInfo? _info;
    private Vst3State? _state;
    private int _stateVersion;
    private Vst3Plugin? _instance;
    private Vst3PluginInfo? _loadedInfo;
    private int _loadedRate;
    private int _loadedStateVersion;

    public bool Enabled { get; set; } = true;

    public string? Error { get; private set; }

    public Vst3PluginInfo? Info => _info;

    public Vst3State? State => _state;

    public Vst3Plugin? Instance => _instance;

    public bool Active => Enabled && _info is not null;

    public void Choose(Vst3PluginInfo? info)
    {
        _info = info;
        _state = null;
        _stateVersion++;
        Enabled = true;
        Error = null;
    }

    public void Restore(Vst3PluginInfo info, Vst3State? state, bool enabled)
    {
        _info = info;
        _state = state;
        _stateVersion++;
        Enabled = enabled;
        Error = null;
    }

    public void CopyFrom(PluginSlot source)
    {
        Enabled = source.Enabled;
        _info = source._info;
        _state = source._state;
        _stateVersion = source._stateVersion;
    }

    public void Sync(int sampleRate)
    {
        DisposeRetired();
        if (_info is null)
        {
            Retire();
            return;
        }

        var reload = _instance is null || _loadedInfo != _info || _loadedRate != sampleRate;
        if (reload)
        {
            Retire();
            if (sampleRate <= 0)
            {
                return;
            }

            try
            {
                var plugin = Vst3Plugin.Load(_info, sampleRate);
                if (_state is not null)
                {
                    plugin.LoadState(_state);
                }

                _loadedInfo = _info;
                _loadedRate = sampleRate;
                _loadedStateVersion = _stateVersion;
                Error = null;
                Volatile.Write(ref _instance, plugin);
            }
            catch (Exception ex)
            {
                Error = $"Could not load {_info.Name}: {ex.Message}";
            }

            return;
        }

        if (_loadedStateVersion != _stateVersion && _state is not null)
        {
            _instance!.LoadState(_state);
            _loadedStateVersion = _stateVersion;
        }
    }

    public bool Capture()
    {
        var plugin = _instance;
        if (plugin is null)
        {
            return false;
        }

        plugin.FlushParameters();
        var captured = plugin.SaveState();
        if (_state is not null && captured.Component.AsSpan().SequenceEqual(_state.Component) && captured.Controller.AsSpan().SequenceEqual(_state.Controller))
        {
            return false;
        }

        _state = captured;
        _stateVersion++;
        _loadedStateVersion = _stateVersion;
        return true;
    }

    public void Process(float[] left, float[] right, int frames)
    {
        var plugin = Volatile.Read(ref _instance);
        if (Enabled && plugin is not null)
        {
            plugin.Process(left, right, frames);
        }
    }

    public void Dispose()
    {
        Retire();
        DisposeRetired();
    }

    private void Retire()
    {
        var old = Interlocked.Exchange(ref _instance, null);
        if (old is not null)
        {
            _retired.Add(old);
        }

        _loadedInfo = null;
    }

    private void DisposeRetired()
    {
        foreach (var plugin in _retired)
        {
            plugin.Dispose();
        }

        _retired.Clear();
    }
}
