using NAudio.Wave;
using Overdub.Audio.Vst3;

namespace Overdub.Audio;

public sealed class Session : IDisposable
{
    private readonly Dictionary<Track, string> _recordingPaths = [];
    private readonly object _midiLock = new();
    private int _take;
    private int _created;
    private Track? _midiTrack;
    private List<MidiEvent>? _midiBuffer;
    private volatile int _midiActivity;

    public Session(string directory)
    {
        Directory = directory;
        Midi.NoteReceived += HandleNote;
        Midi.ControlReceived += HandleControl;
    }

    public string Directory { get; private set; }
    public AsioEngine Engine { get; } = new();
    public MidiInput Midi { get; } = new();
    public EditHistory History { get; } = new();
    public double PracticeSpeed { get; private set; } = 1.0;
    public List<Track> Tracks { get; } = [];

    public Track AddTrack(string name, int? input)
    {
        var track = new Track(name, input) { ColorIndex = _created++ };
        Tracks.Add(track);
        ApplyMixerState();
        return track;
    }

    public void RemoveTrack(Track track)
    {
        Tracks.Remove(track);
        PublishClips();
        ApplyMixerState();
    }

    private static string SafeName(string name) =>
        string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));

    public bool CanRecord => Tracks.Any(t => t is { Armed: true, Input: not null }) || HasArmedMidi;

    public bool HasArmedMidi => Tracks.Any(t => t is { Armed: true, IsMidi: true });

    public bool WaitForInput { get; set; }

    public void StartRecording()
    {
        if (PracticeSpeed != 1.0)
        {
            throw new InvalidOperationException("Set the practice speed back to 100% to record.");
        }

        _take++;
        _recordingPaths.Clear();
        var inputs = new Dictionary<int, string>();
        foreach (var track in Tracks.Where(t => t is { Armed: true, Input: not null }))
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = System.IO.Path.Combine(Directory, $"{SafeName(track.Name)}-{track.Id}-take{_take}.wav");
            _recordingPaths[track] = path;
            inputs[track.Input!.Value] = path;
        }

        lock (_midiLock)
        {
            _midiTrack = HasArmedMidi ? Tracks.First(t => t is { Armed: true, IsMidi: true }) : null;
            _midiBuffer = _midiTrack is null ? null : [];
        }

        Engine.StartRecording(inputs, WaitForInput);
        if (WaitForInput)
        {
            return;
        }

        if (Engine.CountInBars > 0)
        {
            Engine.BeginCountIn();
        }
        else
        {
            Engine.Play();
        }
    }

    public void StopRecording()
    {
        var before = Tracks.ToDictionary(t => t, t => (Audio: t.Clips.ToHashSet(), Midi: t.MidiClips.ToHashSet()));
        var start = Math.Max(0, Engine.RecordStartSample - Engine.CompensationSamples);
        var trim = (int)Math.Max(0, Engine.CompensationSamples - Engine.RecordStartSample);
        Engine.StopRecording();
        var wraps = Engine.LastRecordingWraps;
        FinishMidiRecording();
        foreach (var (track, path) in _recordingPaths)
        {
            var playback = PlaybackTrack.FromWav(path, start, Engine.SampleRate);
            if (playback.Samples.Length == 0)
            {
                File.Delete(path);
                continue;
            }

            if (wraps.Count == 0)
            {
                if (trim > 0 && trim < playback.Samples.Length)
                {
                    var trimmed = playback.Samples[trim..];
                    WriteWav(path, trimmed, 0, trimmed.Length);
                    playback = new PlaybackTrack(trimmed, start);
                }

                AddTake(track, new Clip(path, playback));
                continue;
            }

            SplitPasses(track, path, playback.Samples, start, wraps, trim);
        }

        _recordingPaths.Clear();
        PublishClips();
        RecordTakeInHistory(before);
    }

    private void RecordTakeInHistory(Dictionary<Track, (HashSet<Clip> Audio, HashSet<MidiClip> Midi)> before)
    {
        var audio = new List<(Track Track, Clip Clip)>();
        var midi = new List<(Track Track, MidiClip Clip)>();
        foreach (var track in Tracks.Where(before.ContainsKey))
        {
            audio.AddRange(track.Clips.Where(c => !before[track].Audio.Contains(c)).Select(c => (track, c)));
            midi.AddRange(track.MidiClips.Where(c => !before[track].Midi.Contains(c)).Select(c => (track, c)));
        }

        if (audio.Count == 0 && midi.Count == 0)
        {
            return;
        }

        History.Record(
            "Record take",
            () =>
            {
                audio.ForEach(a => a.Track.AddClip(a.Clip));
                midi.ForEach(m => m.Track.AddMidiClip(m.Clip));
                PublishClips();
            },
            () =>
            {
                audio.ForEach(a => a.Track.Clips.Remove(a.Clip));
                midi.ForEach(m => m.Track.MidiClips.Remove(m.Clip));
                PublishClips();
            });
    }

    public float ReadMidiActivity()
    {
        var velocity = _midiActivity;
        _midiActivity = 0;
        return velocity / 127f;
    }

    public long LengthSamples => Math.Max(
        Tracks.SelectMany(t => t.Clips).Select(c => c.EndSample).DefaultIfEmpty(0).Max(),
        Tracks.SelectMany(t => t.MidiClips).Select(c => c.EndSample).DefaultIfEmpty(0).Max());

    public void ApplyMixerState()
    {
        var drums = Tracks.FirstOrDefault(t => t.IsDrums);
        Engine.DrumGain = drums?.Gain ?? 1f;
        Engine.DrumPan = drums?.Pan ?? 0f;
        foreach (var role in Enum.GetValues<MachineRole>())
        {
            var machine = MachineTrack(role);
            var lane = Engine.Machines[(int)role];
            lane.Gain = machine?.Gain ?? 1f;
            lane.Pan = machine?.Pan ?? 0f;
            lane.Synth.SetPreset(machine?.Preset);
        }

        var keys = Tracks.FirstOrDefault(t => t.IsKeys);
        Engine.SynthGain = keys?.Gain ?? 1f;
        Engine.SynthPan = keys?.Pan ?? 0f;
        Engine.Synth.SetPreset(keys?.Preset);
        Engine.Synth.Instrument = keys is { Instrument.Active: true } ? keys.Instrument.Instance : null;
    }

    private static void AddTake(Track track, Clip clip)
    {
        clip.Lane = track.Clips
            .Where(c => c.StartSample < clip.EndSample && clip.StartSample < c.EndSample)
            .Select(c => c.Lane + 1)
            .DefaultIfEmpty(0)
            .Max();
        track.AddClip(clip);
    }

    public void CompTake(Track track, int lane, long start, long end)
    {
        var before = track.Comp.ToList();
        Edit(
            "Comp take",
            () => track.SetComp(lane, start, end),
            () =>
            {
                track.Comp.Clear();
                track.Comp.AddRange(before);
            });
    }

    private void WriteWav(string path, float[] samples, int from, int length)
    {
        using var writer = new NAudio.Wave.WaveFileWriter(path, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(Engine.SampleRate, 1));
        writer.WriteSamples(samples, from, length);
    }

    private void SplitPasses(Track track, string path, float[] samples, long firstStart, IReadOnlyList<long> wraps, int trim)
    {
        var compensation = Engine.CompensationSamples;
        var bounds = new List<long> { Math.Min(trim, samples.Length) };
        bounds.AddRange(wraps.Select(w => Math.Min(samples.Length, w + compensation)));
        bounds.Add(samples.Length);
        var pass = 0;
        for (var i = 0; i + 1 < bounds.Count; i++)
        {
            var from = (int)bounds[i];
            var length = (int)(bounds[i + 1] - from);
            if (length <= 0)
            {
                continue;
            }

            pass++;
            var passPath = System.IO.Path.ChangeExtension(path, null) + $"-pass{pass}.wav";
            WriteWav(passPath, samples, from, length);

            var startSample = i == 0 ? firstStart : Engine.LoopStart;
            var slice = new float[length];
            Array.Copy(samples, from, slice, 0, length);
            AddTake(track, new Clip(passPath, new PlaybackTrack(slice, startSample)));
        }

        File.Delete(path);
    }

    public void Edit(string name, Action doIt, Action undo, EditKind kind = EditKind.Clips, string? mergeKey = null) =>
        History.Execute(name, () => { doIt(); PublishClips(); }, () => { undo(); PublishClips(); }, kind, mergeKey);

    public Track ImportAudio(string sourcePath)
    {
        if (Engine.SampleRate == 0)
        {
            throw new InvalidOperationException("Open an audio device first.");
        }

        using var reader = new AudioFileReader(sourcePath);
        ISampleProvider provider = reader;
        if (reader.WaveFormat.SampleRate != Engine.SampleRate)
        {
            provider = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(reader, Engine.SampleRate);
        }

        var channels = provider.WaveFormat.Channels;
        var data = new List<float[]>();
        var total = 0;
        var chunk = new float[channels * 65536];
        int read;
        while ((read = provider.Read(chunk.AsSpan())) > 0)
        {
            data.Add(chunk[..read]);
            total += read;
        }

        if (total == 0)
        {
            throw new InvalidOperationException("That file contains no audio.");
        }

        var interleaved = new float[total];
        var at = 0;
        foreach (var block in data)
        {
            block.CopyTo(interleaved, at);
            at += block.Length;
        }

        var name = System.IO.Path.GetFileNameWithoutExtension(sourcePath);
        System.IO.Directory.CreateDirectory(Directory);
        var path = System.IO.Path.Combine(Directory, $"{SafeName(name)}-imported.wav");
        for (var n = 2; File.Exists(path); n++)
        {
            path = System.IO.Path.Combine(Directory, $"{SafeName(name)}-imported-{n}.wav");
        }

        var playback = PlaybackTrack.FromInterleaved(interleaved, total, channels, 0, keepStereo: true);
        using (var writer = new NAudio.Wave.WaveFileWriter(path, NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(Engine.SampleRate, playback.Right is null ? 1 : 2)))
        {
            if (playback.Right is { } right)
            {
                var stereo = new float[playback.Samples.Length * 2];
                for (var i = 0; i < playback.Samples.Length; i++)
                {
                    stereo[i * 2] = playback.Samples[i];
                    stereo[(i * 2) + 1] = right[i];
                }

                writer.WriteSamples(stereo, 0, stereo.Length);
            }
            else
            {
                writer.WriteSamples(playback.Samples, 0, playback.Samples.Length);
            }
        }

        var track = new Track(name, null) { ColorIndex = _created++, IsBacking = true };
        var clip = new Clip(path, playback);
        Edit(
            "Import audio",
            () =>
            {
                Tracks.Add(track);
                track.AddClip(clip);
                ApplyMixerState();
            },
            () =>
            {
                Tracks.Remove(track);
                ApplyMixerState();
            },
            EditKind.Tracks);
        return track;
    }

    public void ReplaceMidiClip(Track track, int index, MidiClip replacement, string name)
    {
        var original = track.MidiClips[index];
        if (replacement.Events.Length == 0)
        {
            Edit(name, () => track.MidiClips.RemoveAt(index), () => track.InsertMidiClip(index, original));
            return;
        }

        Edit(
            name,
            () =>
            {
                track.MidiClips.RemoveAt(index);
                track.InsertMidiClip(index, replacement);
            },
            () =>
            {
                track.MidiClips.RemoveAt(index);
                track.InsertMidiClip(index, original);
            });
    }

    public Track AddTrackUndoable(string name, int? input)
    {
        var track = new Track(name, input) { ColorIndex = _created++ };
        Edit("Add track", () => { Tracks.Add(track); ApplyMixerState(); }, () => { Tracks.Remove(track); ApplyMixerState(); }, EditKind.Tracks);
        return track;
    }

    public Track? MachineTrack(MachineRole role) => Tracks.FirstOrDefault(t => t.Machine == role);

    public Track AddMachineTrackUndoable(MachineRole role)
    {
        var track = new Track(role == MachineRole.Guitar ? "Guitar machine" : "Bass machine", null)
        {
            Machine = role,
            ColorIndex = _created++,
            Preset = role == MachineRole.Guitar ? "Pluck" : "Bass",
            Gain = 0.6f,
        };
        track.ChordPatterns.Add(ChordPattern.Starter("A", role));
        Edit("Add machine track", () => { Tracks.Add(track); ApplyMixerState(); }, () => { Tracks.Remove(track); ApplyMixerState(); }, EditKind.Tracks);
        return track;
    }

    public void EditChordPattern(ChordPattern pattern, Action<ChordPattern> change, string name)
    {
        var before = pattern.Clone();
        var after = pattern.Clone();
        change(after);
        Edit(
            name,
            () =>
            {
                pattern.CopyFrom(after);
                RegeneratePatternClips();
            },
            () =>
            {
                pattern.CopyFrom(before);
                RegeneratePatternClips();
            });
    }

    public ChordPattern AddChordPattern(Track track)
    {
        var pattern = new ChordPattern(((char)('A' + track.ChordPatterns.Count)).ToString(), track.Machine!.Value);
        Edit("Add chord pattern", () => track.ChordPatterns.Add(pattern), () => track.ChordPatterns.Remove(pattern));
        return pattern;
    }

    public void PlaceChordPattern(Track track, ChordPattern pattern, long start, int repeats = 1)
    {
        var length = pattern.LengthSamples(Engine.SampleRate, Engine.Bpm, Engine.BeatsPerBar);
        var clips = Enumerable.Range(0, repeats).Select(i => pattern.ToClip(Engine.SampleRate, Engine.Bpm, Engine.BeatsPerBar, start + (i * length))).ToList();
        Edit("Place chord pattern", () => clips.ForEach(track.AddMidiClip), () => clips.ForEach(c => track.MidiClips.Remove(c)));
    }

    public Track? DrumTrack => Tracks.FirstOrDefault(t => t.IsDrums);

    public Track AddDrumTrackUndoable()
    {
        var track = new Track("Drums", null) { IsDrums = true, ColorIndex = _created++ };
        track.Patterns.Add(DrumPattern.Starter("A"));
        Edit("Add drum track", () => { Tracks.Add(track); ApplyMixerState(); }, () => { Tracks.Remove(track); ApplyMixerState(); }, EditKind.Tracks);
        return track;
    }

    public void RegeneratePatternClips()
    {
        if (Engine.SampleRate == 0)
        {
            return;
        }

        foreach (var track in Tracks.Where(t => t.IsDrums))
        {
            for (var i = 0; i < track.MidiClips.Count; i++)
            {
                var clip = track.MidiClips[i];
                var pattern = track.Patterns.FirstOrDefault(p => p.Id == clip.PatternId);
                if (pattern is null)
                {
                    continue;
                }

                var fresh = pattern.ToClip(Engine.SampleRate, Engine.Bpm, clip.Shift);
                fresh.Mute = clip.Mute;
                fresh.Solo = clip.Solo;
                track.MidiClips[i] = fresh;
            }
        }

        foreach (var track in Tracks.Where(t => t.Machine is not null))
        {
            for (var i = 0; i < track.MidiClips.Count; i++)
            {
                var clip = track.MidiClips[i];
                var pattern = track.ChordPatterns.FirstOrDefault(p => p.Id == clip.PatternId);
                if (pattern is null)
                {
                    continue;
                }

                var fresh = pattern.ToClip(Engine.SampleRate, Engine.Bpm, Engine.BeatsPerBar, clip.Shift);
                fresh.Mute = clip.Mute;
                fresh.Solo = clip.Solo;
                track.MidiClips[i] = fresh;
            }
        }
    }

    public void EditPattern(Track track, DrumPattern pattern, Action<DrumPattern> change, string name)
    {
        var before = pattern.Clone();
        var after = pattern.Clone();
        change(after);
        Edit(
            name,
            () =>
            {
                pattern.CopyFrom(after);
                RegeneratePatternClips();
            },
            () =>
            {
                pattern.CopyFrom(before);
                RegeneratePatternClips();
            });
    }

    public DrumPattern AddPattern(Track track)
    {
        var pattern = new DrumPattern(((char)('A' + track.Patterns.Count)).ToString());
        Edit("Add drum pattern", () => track.Patterns.Add(pattern), () => track.Patterns.Remove(pattern));
        return pattern;
    }

    public void PlacePattern(Track track, DrumPattern pattern, long start, int repeats = 1)
    {
        var length = pattern.LengthSamples(Engine.SampleRate, Engine.Bpm);
        var clips = Enumerable.Range(0, repeats).Select(i => pattern.ToClip(Engine.SampleRate, Engine.Bpm, start + (i * length))).ToList();
        Edit("Place drum pattern", () => clips.ForEach(track.AddMidiClip), () => clips.ForEach(c => track.MidiClips.Remove(c)));
    }

    public void RemoveTrackUndoable(Track track)
    {
        var index = Tracks.IndexOf(track);
        Edit("Remove track", () => { Tracks.Remove(track); ApplyMixerState(); }, () => { Tracks.Insert(index, track); ApplyMixerState(); }, EditKind.Tracks);
    }

    public void PrepareSpeed(double speed)
    {
        if (speed == 1.0)
        {
            return;
        }

        foreach (var view in Tracks.Where(t => !t.IsMidi).SelectMany(t => t.EffectiveClips()))
        {
            TimeStretcher.Get(view.Samples, view.Right, view.Offset, view.Length, speed);
        }
    }

    public void ApplySpeed(double speed)
    {
        var wasPlaying = Engine.IsPlaying;
        Engine.Pause();
        PracticeSpeed = speed;
        Engine.SetSpeed(speed);
        PublishClips();
        if (wasPlaying)
        {
            Engine.Play();
        }
    }

    private IEnumerable<MidiClip> AtPracticeSpeed(IEnumerable<MidiClip> clips) => PracticeSpeed == 1.0 ? clips : clips.Select(c => c.Scaled(1 / PracticeSpeed));

    private IReadOnlyList<PlaybackTrack> AtPracticeSpeed(IReadOnlyList<PlaybackTrack> views)
    {
        if (PracticeSpeed == 1.0)
        {
            return views;
        }

        return views.Select(view =>
        {
            var (left, right) = TimeStretcher.Get(view.Samples, view.Right, view.Offset, view.Length, PracticeSpeed);
            return new PlaybackTrack(left, (long)Math.Round(view.StartSample / PracticeSpeed))
            {
                Right = right,
                Gain = view.Gain,
                Pan = view.Pan,
                Mute = view.Mute,
                Solo = view.Solo,
            };
        }).ToList();
    }

    public void PublishClips()
    {
        var rate = Engine.SampleRate;
        var strips = new List<ChannelStrip>();
        foreach (var track in Tracks.Where(t => !t.IsMidi))
        {
            if (rate > 0)
            {
                track.PlaybackFx.Configure(rate);
            }

            strips.Add(new ChannelStrip(AtPracticeSpeed(track.EffectiveClips()), track.Effects, track.PlaybackFx));
        }

        SyncPlugins();
        Engine.SetChannels(strips);
        for (var input = 0; input < AsioEngine.MaxInputs; input++)
        {
            var bound = Tracks.Where(t => t.Input == input).OrderByDescending(t => t.Effects.AnyEnabled).ThenByDescending(t => t.Armed).FirstOrDefault();
            if (bound is not null && rate > 0)
            {
                bound.LiveFx.Configure(rate);
            }

            Engine.SetLiveEffects(input, bound?.Effects, bound?.LiveFx);
        }
        Engine.SetMidiClips(AtPracticeSpeed(Tracks.Where(t => t.IsKeys).SelectMany(t => t.MidiClips)));
        foreach (var role in Enum.GetValues<MachineRole>())
        {
            Engine.SetMachineClips(role, AtPracticeSpeed(Tracks.Where(t => t.Machine == role).SelectMany(t => t.MidiClips)));
        }

        Engine.SetDrumClips(AtPracticeSpeed(Tracks.Where(t => t.IsDrums).SelectMany(t => t.MidiClips)));
    }

    public event Action<byte, byte>? NoteActivity;

    public void HandleNote(byte note, byte velocity)
    {
        NoteActivity?.Invoke(note, velocity);
        if (velocity > 0)
        {
            if (_midiBuffer is not null)
            {
                Engine.ReleaseWait();
            }

            Engine.Synth.NoteOn(note, velocity);
            _midiActivity = Math.Max(_midiActivity, velocity);
        }
        else
        {
            Engine.Synth.NoteOff(note);
        }

        lock (_midiLock)
        {
            if (_midiBuffer is null || Engine.IsCountingIn)
            {
                return;
            }

            _midiBuffer.Add(new MidiEvent(MidiTime(), note, velocity));
        }
    }

    public void HandleControl(MidiKind kind, int value)
    {
        if (kind == MidiKind.Sustain)
        {
            Engine.Synth.SustainPedal(value >= 64);
        }
        else if (kind == MidiKind.PitchBend)
        {
            Engine.Synth.PitchBend(value);
        }

        lock (_midiLock)
        {
            if (_midiBuffer is null || Engine.IsCountingIn)
            {
                return;
            }

            _midiBuffer.Add(new MidiEvent(MidiTime(), 0, 0, kind, value));
        }
    }

    private long MidiTime() => Math.Max(0, Engine.Position - Engine.OutputLatencySamples - Engine.ManualOffsetSamples);

    private void FinishMidiRecording()
    {
        Track? track;
        List<MidiEvent>? events;
        lock (_midiLock)
        {
            track = _midiTrack;
            events = _midiBuffer;
            _midiTrack = null;
            _midiBuffer = null;
        }

        if (track is null || events is not { Count: > 0 })
        {
            return;
        }

        var held = new HashSet<byte>();
        var sustain = 0;
        var bend = 0;
        foreach (var e in events)
        {
            switch (e.Kind)
            {
                case MidiKind.Sustain:
                    sustain = e.Value;
                    break;
                case MidiKind.PitchBend:
                    bend = e.Value;
                    break;
                case MidiKind.Note when e.Velocity > 0:
                    held.Add(e.Note);
                    break;
                default:
                    held.Remove(e.Note);
                    break;
            }
        }

        var end = Math.Max(Engine.Position, events[^1].At);
        events.AddRange(held.Select(note => new MidiEvent(end, note, 0)));
        if (sustain >= 64)
        {
            events.Add(new MidiEvent(end, 0, 0, MidiKind.Sustain, 0));
        }

        if (bend != 0)
        {
            events.Add(new MidiEvent(end, 0, 0, MidiKind.PitchBend, 0));
        }

        track.AddMidiClip(new MidiClip(events));
    }

    public string ProjectPath => System.IO.Path.Combine(Directory, ProjectFile.FileName);

    public void Save()
    {
        System.IO.Directory.CreateDirectory(Directory);
        var tracks = Tracks.Select(t => new TrackData(
            t.Name,
            t.Mute,
            t.Solo,
            t.Gain,
            t.Clips.Select(c => new ClipData(System.IO.Path.GetRelativePath(Directory, c.Path), c.StartSample, c.Playback.Offset, c.Length, c.Lane)).ToList(),
            t.MidiClips.Select(m => new MidiClipData(m.Events.Select(e => new MidiEventData(e.At + m.Shift, e.Note, e.Velocity, (int)e.Kind, e.Value)).ToList(), m.PatternId)).ToList(),
            t.Pan,
            t.Input,
            t.IsMidi,
            t.ColorIndex,
            t.Preset,
            t.IsBacking ? true : null,
            t.Effects.Effects.ToDictionary(e => e.Name, e => new EffectData(e.Enabled, (double[])e.Values.Clone())),
            t.Comp.Select(c => new CompData(c.Start, c.End, c.Lane)).ToList(),
            PluginDataFor(t.Effects.Plugin),
            PluginDataFor(t.Instrument),
            t.IsDrums ? true : null,
            t.IsDrums ? t.Patterns.Select(p => new PatternData(p.Id, p.Name, p.Bars, p.Encode().ToList())).ToList() : null,
            t.Machine?.ToString(),
            t.Machine is null ? null : t.ChordPatterns.Select(p => new ChordPatternData(p.Id, p.Name, p.Bars, (int)p.Style, p.Encode())).ToList())).ToList();
        ProjectFile.Write(ProjectPath, new ProjectData(1, Engine.SampleRate, Engine.Bpm, tracks, Engine.BeatsPerBar, Engine.BeatUnit));
        RecentProjects.Add(ProjectPath);
    }

    private static PluginData? PluginDataFor(PluginSlot slot)
    {
        if (slot.Info is not { } info)
        {
            return null;
        }

        slot.Capture();
        return new PluginData(
            info.Path,
            info.ClassId.ToString(),
            info.Name,
            info.Vendor,
            info.Category,
            info.SubCategories,
            slot.Enabled,
            slot.State is null ? null : Convert.ToBase64String(slot.State.Component),
            slot.State is null ? null : Convert.ToBase64String(slot.State.Controller));
    }

    private static void RestorePlugin(PluginSlot slot, PluginData data)
    {
        var info = new Vst3PluginInfo(data.Path, data.Name, data.Vendor, data.Category, data.SubCategories, Guid.Parse(data.ClassId));
        var state = data.Component is null ? null : new Vst3State(Convert.FromBase64String(data.Component), Convert.FromBase64String(data.Controller ?? ""));
        slot.Restore(info, state, data.Enabled);
    }

    public IEnumerable<string> PluginErrors => Tracks.SelectMany(t => new[] { t.Effects.Plugin.Error, t.Instrument.Error }).OfType<string>();

    public void AssignPlugin(Track track, Vst3PluginInfo? info)
    {
        track.PluginSlot.Choose(info);
        SyncPlugins();
        track.Effects.Touch();
    }

    public void SyncPlugins()
    {
        var rate = Engine.SampleRate;
        foreach (var track in Tracks)
        {
            track.Instrument.Sync(rate);
            track.Effects.Plugin.Sync(rate);
            track.PlaybackFx.Plugin.CopyFrom(track.Effects.Plugin);
            track.PlaybackFx.Plugin.Sync(rate);
            track.LiveFx.Plugin.CopyFrom(track.Effects.Plugin);
            track.LiveFx.Plugin.Sync(rate);
        }

        ApplyMixerState();
    }

    public double Load(string projectPath)
    {
        var data = ProjectFile.Read(projectPath);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(projectPath))!;
        var loaded = new List<Track>();
        foreach (var d in data.Tracks)
        {
            var isMidi = d.IsBacking != true && (d.IsMidi ?? (d.Input is null && d.Name == "Keys"));
            int? input = isMidi ? null : d.Input ?? (d.Name == "Bass" ? 1 : 0);
            var backing = d.IsBacking == true;
            var track = new Track(d.Name, backing ? null : input) { ColorIndex = d.Color ?? loaded.Count, Preset = d.Preset ?? "Lead", IsBacking = backing, IsDrums = d.IsDrums == true, Machine = Enum.TryParse<MachineRole>(d.Machine, out var machineRole) ? machineRole : null };
            foreach (var p in d.ChordPatterns ?? [])
            {
                track.ChordPatterns.Add(ChordPattern.Decode(p.Id, p.Name, track.Machine ?? MachineRole.Guitar, p.Bars, (ChordStyle)p.Style, p.Chords));
            }

            foreach (var p in d.Patterns ?? [])
            {
                track.Patterns.Add(DrumPattern.Decode(p.Id, p.Name, p.Bars, p.Lanes));
            }

            track.Mute = d.Mute;
            track.Solo = d.Solo;
            track.Gain = d.Gain;
            track.Pan = d.Pan;
            if (d.Effects is not null)
            {
                foreach (var effect in track.Effects.Effects.Where(e => d.Effects.ContainsKey(e.Name)))
                {
                    var saved = d.Effects[effect.Name];
                    effect.Enabled = saved.Enabled;
                    Array.Copy(saved.Values, effect.Values, Math.Min(saved.Values.Length, effect.Values.Length));
                }

                track.Effects.Touch();
            }

            if (d.Plugin is not null)
            {
                RestorePlugin(track.Effects.Plugin, d.Plugin);
            }

            if (d.Instrument is not null)
            {
                RestorePlugin(track.Instrument, d.Instrument);
            }

            foreach (var c in d.Clips)
            {
                var path = System.IO.Path.Combine(directory, c.File);
                var playback = PlaybackTrack.FromWav(path, c.StartSample, Engine.SampleRate, keepStereo: true);
                if (c.Offset is { } offset && c.Length is { } length)
                {
                    playback.Offset = Math.Clamp(offset, 0, playback.Samples.Length);
                    playback.Length = Math.Clamp(length, 0, playback.Samples.Length - playback.Offset);
                }

                track.AddClip(new Clip(path, playback) { Lane = c.Lane ?? 0 });
            }

            foreach (var segment in d.Comp ?? [])
            {
                track.Comp.Add(new CompSegment(segment.Start, segment.End, segment.Lane));
            }

            foreach (var midi in d.MidiClips ?? [])
            {
                var events = midi.Events.Select(e => new MidiEvent(e.At, (byte)e.Note, (byte)e.Velocity, (MidiKind)e.Kind, e.Value)).ToList();
                var origin = midi.Pattern is null || events.Count == 0 ? 0 : events.Min(e => e.At);
                track.AddMidiClip(new MidiClip(events.Select(e => e with { At = e.At - origin }), origin) { PatternId = midi.Pattern });
            }

            loaded.Add(track);
        }

        Tracks.Clear();
        Tracks.AddRange(loaded);
        _created = loaded.Count == 0 ? 0 : loaded.Max(t => t.ColorIndex) + 1;
        Directory = directory;
        PracticeSpeed = 1.0;
        Engine.SetSpeed(1.0);
        PublishClips();
        ApplyMixerState();
        Engine.BeatsPerBar = data.BeatsPerBar;
        Engine.BeatUnit = data.BeatUnit;
        History.Clear();
        RecentProjects.Add(projectPath);
        return data.Bpm;
    }

    public IReadOnlyList<string> ExportStems(string folder)
    {
        System.IO.Directory.CreateDirectory(folder);
        var tail = Tracks.Any(t => t.MidiClips.Count > 0) ? Engine.SampleRate : 0;
        var length = Math.Max(LengthSamples, 0) + tail;
        var written = new List<string>();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var track in Tracks.Where(t => t.Clips.Count > 0 || t.MidiClips.Count > 0))
        {
            var name = SafeName(track.Name);
            var unique = name;
            for (var n = 2; !used.Add(unique); n++)
            {
                unique = $"{name}-{n}";
            }

            var audio = track.EffectiveClips().Select(c => new PlaybackTrack(c.Samples, c.StartSample)
            {
                Right = c.Right,
                Offset = c.Offset,
                Length = c.Length,
                Gain = track.Gain,
                Pan = track.Pan,
            }).ToList();
            var midi = track.MidiClips.Select(m => m.Copy(0)).ToList();
            midi.ForEach(m =>
            {
                m.Mute = false;
                m.Solo = false;
            });
            var path = System.IO.Path.Combine(folder, unique + ".wav");
            var strip = new ChannelStrip(audio, track.Effects, track.Effects.CloneForProcessing(Engine.SampleRate));
            var instrument = track.IsKeys && track.Instrument.Active ? track.Instrument.CreateCopy(Engine.SampleRate) : null;
            try
            {
                if (track.Machine is { } role)
                {
                    Mixer.Export([], [], Engine.SampleRate, path, 1f, 0f, length, null, null, null, [new MachineMix(midi, track.Gain, track.Pan, track.Preset)]);
                }
                else if (track.IsDrums)
                {
                    Mixer.Export([], [], Engine.SampleRate, path, 1f, 0f, length, null, null, new DrumMix(midi, track.Gain, track.Pan));
                }
                else
                {
                    Mixer.Export([strip], midi, Engine.SampleRate, path, track.Gain, track.Pan, length, track.Preset, instrument?.Instance);
                }
            }
            finally
            {
                strip.Processor?.Dispose();
                instrument?.Dispose();
            }

            written.Add(path);
        }

        if (written.Count == 0)
        {
            throw new InvalidOperationException("Nothing to export yet. Record something first.");
        }

        return written;
    }

    public void ExportMixdown(string path)
    {
        var wavPath = AudioEncoder.IsEncoded(path) ? System.IO.Path.GetTempFileName() : path;
        try
        {
            var keys = Tracks.FirstOrDefault(t => t.IsKeys);
            var machineMixes = Tracks.Where(t => t.Machine is not null).Select(t => new MachineMix(t.MidiClips.Select(m => m.Copy(0)).ToList(), t.Gain, t.Pan, t.Preset)).ToList();
            var drumTrack = DrumTrack;
            var drumMix = drumTrack is null ? null : new DrumMix(drumTrack.MidiClips.Select(m => m.Copy(0)).ToList(), drumTrack.Gain, drumTrack.Pan);
            var instrument = keys is { Instrument.Active: true } ? keys.Instrument.CreateCopy(Engine.SampleRate) : null;
            var strips = Tracks.Where(t => !t.IsMidi).Select(t => new ChannelStrip(t.EffectiveClips(), t.Effects, t.Effects.CloneForProcessing(Engine.SampleRate))).ToList();
            try
            {
                Mixer.Export(
                    strips,
                    Tracks.Where(t => t.IsKeys).SelectMany(t => t.MidiClips).ToList(),
                    Engine.SampleRate,
                    wavPath,
                    Engine.SynthGain,
                    Engine.SynthPan,
                    0,
                    Engine.Synth.PresetName,
                    instrument?.Instance,
                    drumMix,
                    machineMixes);
            }
            finally
            {
                instrument?.Dispose();
                strips.ForEach(s => s.Processor?.Dispose());
            }

            if (wavPath != path)
            {
                AudioEncoder.Convert(wavPath, path);
            }
        }
        finally
        {
            if (wavPath != path)
            {
                File.Delete(wavPath);
            }
        }
    }

    public void Dispose()
    {
        Midi.Dispose();
        Engine.Dispose();
    }
}
