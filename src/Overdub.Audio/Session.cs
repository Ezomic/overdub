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
    private INoteTarget? _liveTarget;
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
    public List<SongSection> Sections { get; } = [];

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

    private int _previewVersion;

    public Task PreviewMachineSound(Track track, string preset, float volume = 1f, EffectChain? effects = null)
    {
        var machines = MachineTracks;
        var own = machines.IndexOf(track);
        var spare = machines.Count < Engine.Machines.Length ? machines.Count : own;
        if (spare < 0 || Engine.SampleRate == 0)
        {
            return Task.CompletedTask;
        }

        var lane = Engine.Machines[spare];
        var version = Interlocked.Increment(ref _previewVersion);
        int[] phrase = track.Machine switch
        {
            MachineRole.Bass => [28, 31, 35, 40],
            MachineRole.Lead => [64, 67, 71, 76],
            _ => [52, 55, 59, 64],
        };
        var previousSource = lane.Source;
        var previousProcessor = lane.Processor;
        var rate = Engine.SampleRate;
        return Task.Run(() =>
        {
            lane.Gain = track.Gain * volume;
            lane.Pan = track.Pan;
            lane.SetPreset(preset, waitForSamples: true);
            if (effects is not null)
            {
                var processor = new EffectChain();
                processor.Configure(rate);
                lane.Processor = processor;
                lane.Source = effects;
            }

            foreach (var note in phrase)
            {
                if (version != _previewVersion)
                {
                    break;
                }

                lane.Voice.NoteOn((byte)note, 100);
                Thread.Sleep(380);
                lane.Voice.NoteOff((byte)note);
            }

            Thread.Sleep(600);
            lane.AllNotesOff();
            if (version == _previewVersion)
            {
                if (effects is not null)
                {
                    lane.Source = previousSource;
                    lane.Processor = previousProcessor;
                }

                if (spare != own)
                {
                    lane.SetPreset(null);
                }
            }
        });
    }

    public Task PreviewChords(Track? track, IReadOnlyList<int[]> chords, int holdMs, float volume = 1f)
    {
        if (Engine.SampleRate == 0 || chords.Count == 0)
        {
            return Task.CompletedTask;
        }

        var machines = MachineTracks;
        var own = track is null ? -1 : machines.IndexOf(track);
        var spare = own < 0 ? -1 : machines.Count < Engine.Machines.Length ? machines.Count : own;
        var lane = spare >= 0 ? Engine.Machines[spare] : Engine.Keys;
        var version = Interlocked.Increment(ref _previewVersion);
        return Task.Run(() =>
        {
            if (spare >= 0)
            {
                lane.Gain = track!.Gain * volume;
                lane.Pan = track.Pan;
                lane.SetPreset(track.Preset, waitForSamples: true);
            }

            foreach (var chord in chords)
            {
                if (version != _previewVersion)
                {
                    break;
                }

                foreach (var note in chord)
                {
                    lane.Voice.NoteOn((byte)note, 92);
                }

                Thread.Sleep(holdMs);
                foreach (var note in chord)
                {
                    lane.Voice.NoteOff((byte)note);
                }
            }

            Thread.Sleep(500);
            lane.AllNotesOff();
            if (version == _previewVersion && spare >= 0 && spare != own)
            {
                lane.SetPreset(null);
            }
        });
    }

    public void ApplyMixerState()
    {
        var drums = Tracks.FirstOrDefault(t => t.IsDrums);
        Engine.DrumGain = drums?.Gain ?? 1f;
        Engine.DrumPan = drums?.Pan ?? 0f;
        Engine.Drums.Mix = drums?.DrumLanes;
        Engine.Drums.Style = drums?.DrumKitStyle ?? 0;
        Engine.Drums.SetSample(drums?.DrumKitSample);
        Engine.DrumSends = drums?.Sends;
        var machines = MachineTracks;
        for (var i = 0; i < Engine.Machines.Length; i++)
        {
            var machine = i < machines.Count ? machines[i] : null;
            var lane = Engine.Machines[i];
            lane.Gain = machine?.Gain ?? 1f;
            lane.Pan = machine?.Pan ?? 0f;
            lane.SetPreset(machine?.Preset);
            lane.SetInstrument(machine is { Instrument.Active: true } ? machine.Instrument.Instance : null);
            lane.Sends = machine?.Sends;
            lane.Source = machine?.Effects;
            lane.Processor = machine?.PlaybackFx;
            if (machine is not null && Engine.SampleRate > 0)
            {
                machine.PlaybackFx.Configure(Engine.SampleRate);
            }
        }

        var keys = Tracks.FirstOrDefault(t => t.IsKeys);
        Engine.SynthGain = keys?.Gain ?? 1f;
        Engine.SynthSends = keys?.Sends;
        Engine.SynthPan = keys?.Pan ?? 0f;
        Engine.Keys.SetPreset(keys?.Preset);
        Engine.Keys.SetInstrument(keys is { Instrument.Active: true } ? keys.Instrument.Instance : null);
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

    public void AddSection(string name, long start, long length)
    {
        var section = new SongSection(Guid.NewGuid().ToString("N")[..8], name, start, length);
        Edit("Mark section", () => Sections.Add(section), () => Sections.Remove(section));
    }

    public void RenameSection(SongSection section, string name)
    {
        var index = Sections.IndexOf(section);
        if (index < 0)
        {
            return;
        }

        var renamed = section with { Name = name };
        Edit("Rename section", () => Sections[index] = renamed, () => Sections[index] = section);
    }

    public void RemoveSection(SongSection section)
    {
        var index = Sections.IndexOf(section);
        if (index >= 0)
        {
            Edit("Delete section", () => Sections.RemoveAt(index), () => Sections.Insert(index, section));
        }
    }

    public int CopySection(SongSection section, long destination, bool move)
    {
        var delta = destination - section.Start;
        if (delta == 0 || destination < 0)
        {
            return 0;
        }

        var end = section.Start + section.Length;
        var midi = Tracks.SelectMany(t => t.MidiClips.Where(c => c.StartSample >= section.Start && c.StartSample < end).Select(c => (Track: t, Original: c, Copy: c.Copy(delta)))).ToList();
        var audio = Tracks.SelectMany(t => t.Clips.Where(c => c.StartSample >= section.Start && c.StartSample < end).Select(c => (Track: t, Original: c, Copy: c.Copy(c.StartSample + delta, c.Playback.Offset, c.Length)))).ToList();
        var index = Sections.IndexOf(section);
        var target = move ? section with { Start = destination } : new SongSection(Guid.NewGuid().ToString("N")[..8], section.Name + " 2", destination, section.Length);
        Edit(
            move ? "Move section" : "Copy section",
            () =>
            {
                midi.ForEach(m => m.Track.AddMidiClip(m.Copy));
                audio.ForEach(a => a.Track.AddClip(a.Copy));
                if (move)
                {
                    midi.ForEach(m => m.Track.MidiClips.Remove(m.Original));
                    audio.ForEach(a => a.Track.Clips.Remove(a.Original));
                    if (index >= 0)
                    {
                        Sections[index] = target;
                    }
                }
                else
                {
                    Sections.Add(target);
                }
            },
            () =>
            {
                midi.ForEach(m => m.Track.MidiClips.Remove(m.Copy));
                audio.ForEach(a => a.Track.Clips.Remove(a.Copy));
                if (move)
                {
                    midi.ForEach(m => m.Track.AddMidiClip(m.Original));
                    audio.ForEach(a => a.Track.AddClip(a.Original));
                    if (index >= 0)
                    {
                        Sections[index] = section;
                    }
                }
                else
                {
                    Sections.Remove(target);
                }
            });
        return midi.Count + audio.Count;
    }

    public (int Root, MelodyScale Scale)? DetectedKey() => KeyFinder.FromSession(this) is { } key ? (key.Root, key.Minor ? MelodyScale.Minor : MelodyScale.Major) : null;

    public void TransposeAll(int semitones)
    {
        if (semitones == 0)
        {
            return;
        }

        var tracks = Tracks.Where(t => !t.IsDrums).ToList();
        var clipsBefore = tracks.Select(t => (Track: t, Clips: t.MidiClips.ToList())).ToList();
        var patternsBefore = tracks.SelectMany(t => t.ChordPatterns).Select(p => (Pattern: p, Copy: p.Clone())).ToList();
        Edit(
            semitones > 0 ? "Transpose up" : "Transpose down",
            () =>
            {
                foreach (var (pattern, _) in patternsBefore)
                {
                    for (var i = 0; i < ChordPattern.MaxBars; i++)
                    {
                        pattern[i] = pattern[i] with { Root = (((pattern[i].Root + semitones) % 12) + 12) % 12 };
                    }
                }

                foreach (var (track, clips) in clipsBefore)
                {
                    track.MidiClips.Clear();
                    foreach (var clip in clips)
                    {
                        track.MidiClips.Add(clip.PatternId is not null ? clip : clip.WithNotes(clip.NoteData().Select(n => n with { Pitch = (byte)Math.Clamp(n.Pitch + semitones, 0, 127) })));
                    }
                }

                RegeneratePatternClips();
            },
            () =>
            {
                foreach (var (pattern, copy) in patternsBefore)
                {
                    pattern.CopyFrom(copy);
                }

                foreach (var (track, clips) in clipsBefore)
                {
                    track.MidiClips.Clear();
                    track.MidiClips.AddRange(clips);
                }
            });
    }

    public void PlaceClips(Track track, IReadOnlyList<MidiClip> clips, string name) =>
        Edit(name, () => clips.ToList().ForEach(track.AddMidiClip), () => clips.ToList().ForEach(c => track.MidiClips.Remove(c)));

    public ChordPattern EffectivePattern(ChordPattern pattern)
    {
        if (pattern.FollowId is null)
        {
            return pattern;
        }

        var source = Tracks.Where(t => t.Machine == MachineRole.Guitar).SelectMany(t => t.ChordPatterns).FirstOrDefault(p => p.Id == pattern.FollowId);
        return source is null ? pattern : pattern.WithChordsFrom(EffectivePattern(source));
    }

    public void PlaceMelody(Track track, MidiEvent[] events, long start, string name = "Generate melody")
    {
        var clip = new MidiClip(events, start);
        Edit(name, () => track.AddMidiClip(clip), () => track.MidiClips.Remove(clip));
    }

    private INoteTarget LiveTarget()
    {
        var armed = Tracks.FirstOrDefault(t => t is { Armed: true, IsMidi: true });
        var lane = armed is null ? -1 : MachineTracks.IndexOf(armed);
        return lane >= 0 ? Engine.Machines[lane].Voice : Engine.Keys.Voice;
    }

    public List<Track> MachineTracks => Tracks.Where(t => t.Machine is not null).Take(AsioEngine.MaxMachines).ToList();

    public bool MachineLimitReached => Tracks.Count(t => t.Machine is not null) >= AsioEngine.MaxMachines;

    public Track AddMachineTrackUndoable(MachineRole role, string name)
    {
        var track = new Track(name, null)
        {
            Machine = role,
            ColorIndex = _created++,
            Preset = PluckSynth.DefaultName(role),
            Gain = 0.6f,
        };
        if (role != MachineRole.Lead)
        {
            track.ChordPatterns.Add(ChordPattern.Starter("A", role));
        }

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
        pattern = EffectivePattern(pattern);
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

                var fresh = EffectivePattern(pattern).ToClip(Engine.SampleRate, Engine.Bpm, Engine.BeatsPerBar, clip.Shift);
                fresh.Mute = clip.Mute;
                fresh.Solo = clip.Solo;
                track.MidiClips[i] = fresh;
            }
        }
    }

    public void EditPattern(Track track, DrumPattern pattern, Action<DrumPattern> change, string name, string? mergeKey = null)
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
            },
            EditKind.Clips,
            mergeKey);
    }

    public void SetDrumKit(Track track, int style, string? sample = null)
    {
        var before = track.DrumKitStyle;
        var beforeSample = track.DrumKitSample;
        Edit(
            "Change drum kit",
            () => { track.DrumKitStyle = style; track.DrumKitSample = sample; ApplyMixerState(); },
            () => { track.DrumKitStyle = before; track.DrumKitSample = beforeSample; ApplyMixerState(); },
            EditKind.Mix);
    }

    public void EditDrumLane(Track track, int lane, float gain, float pan, bool mute)
    {
        var mix = track.DrumLanes[lane];
        var before = (mix.Gain, mix.Pan, mix.Mute);
        Edit(
            "Drum lane mix",
            () => (mix.Gain, mix.Pan, mix.Mute) = (gain, pan, mute),
            () => (mix.Gain, mix.Pan, mix.Mute) = before,
            EditKind.Mix,
            $"drumlane:{track.Id}:{lane}");
    }

    public DrumPattern AddPattern(Track track)
    {
        var pattern = new DrumPattern(((char)('A' + track.Patterns.Count)).ToString());
        Edit("Add drum pattern", () => track.Patterns.Add(pattern), () => track.Patterns.Remove(pattern));
        return pattern;
    }

    public void ImportPattern(Track track, DrumPattern pattern) =>
        Edit("Import drum pattern", () => track.Patterns.Add(pattern), () => track.Patterns.Remove(pattern));

    public void ImportPattern(Track track, ChordPattern pattern) =>
        Edit("Import chord pattern", () => track.ChordPatterns.Add(pattern), () => track.ChordPatterns.Remove(pattern));

    public (int Fills, int Crashes) AddSectionFills()
    {
        var track = DrumTrack;
        if (track is null || Engine.SampleRate == 0)
        {
            return (0, 0);
        }

        var rate = Engine.SampleRate;
        var bpm = Engine.Bpm;
        var tolerance = rate / 20;
        var newPatterns = new List<DrumPattern>();
        var fillFor = new Dictionary<string, DrumPattern>();
        var swaps = new List<(MidiClip Old, MidiClip New)>();
        var additions = new List<MidiClip>();
        DrumPattern? crash = track.Patterns.FirstOrDefault(p => p.Name == "Crash");
        var fillIndex = 0;
        foreach (var section in Sections.Where(x => x.Start > 0).OrderBy(x => x.Start))
        {
            var ending = track.MidiClips.FirstOrDefault(c => c.PatternId is not null && track.Patterns.FirstOrDefault(p => p.Id == c.PatternId) is { } p && !p.Name.EndsWith('+') && Math.Abs(c.StartSample + p.LengthSamples(rate, bpm) - section.Start) <= tolerance && !swaps.Any(x => x.Old == c));
            if (ending is not null && track.Patterns.Concat(newPatterns).Count() < 8)
            {
                var source = track.Patterns.First(p => p.Id == ending.PatternId);
                if (!fillFor.TryGetValue(source.Id, out var fill))
                {
                    fill = source.WithFill(fillIndex++ % DrumPattern.FillNames.Length, source.Name + "+");
                    fillFor[source.Id] = fill;
                    newPatterns.Add(fill);
                }

                var replacement = fill.ToClip(rate, bpm, ending.Shift);
                replacement.Mute = ending.Mute;
                replacement.Solo = ending.Solo;
                swaps.Add((ending, replacement));
            }

            if (!track.MidiClips.Any(c => c.PatternId is not null && Math.Abs(c.StartSample - section.Start) <= tolerance && track.Patterns.FirstOrDefault(p => p.Id == c.PatternId)?.Name == "Crash") && (crash is not null || track.Patterns.Concat(newPatterns).Count() < 8))
            {
                if (crash is null)
                {
                    crash = new DrumPattern("Crash");
                    crash.Set(7, 0, 2);
                    newPatterns.Add(crash);
                }

                additions.Add(crash.ToClip(rate, bpm, section.Start));
            }
        }

        if (swaps.Count == 0 && additions.Count == 0)
        {
            return (0, 0);
        }

        Edit(
            "Add drum fills at the sections",
            () =>
            {
                newPatterns.ForEach(p => { if (!track.Patterns.Contains(p)) { track.Patterns.Add(p); } });
                foreach (var (old, replacement) in swaps)
                {
                    var index = track.MidiClips.IndexOf(old);
                    if (index >= 0)
                    {
                        track.MidiClips[index] = replacement;
                    }
                }

                additions.ForEach(track.AddMidiClip);
            },
            () =>
            {
                foreach (var (old, replacement) in swaps)
                {
                    var index = track.MidiClips.IndexOf(replacement);
                    if (index >= 0)
                    {
                        track.MidiClips[index] = old;
                    }
                }

                additions.ForEach(c => track.MidiClips.Remove(c));
                newPatterns.ForEach(p => track.Patterns.Remove(p));
            });
        return (swaps.Count, additions.Count);
    }

    public DrumPattern AddFillPattern(Track track, DrumPattern source, int type)
    {
        var baseName = source.Name.Split('+')[0] + "+";
        var name = baseName;
        for (var n = 2; track.Patterns.Any(p => p.Name == name); n++)
        {
            name = baseName + n;
        }

        var fill = source.WithFill(type, name);
        Edit("Add drum fill", () => track.Patterns.Add(fill), () => track.Patterns.Remove(fill));
        return fill;
    }

    public void PlacePattern(Track track, DrumPattern pattern, long start, int repeats = 1, DrumPattern? everyFourth = null)
    {
        var length = pattern.LengthSamples(Engine.SampleRate, Engine.Bpm);
        var clips = Enumerable.Range(0, repeats).Select(i => (everyFourth is not null && i % 4 == 3 ? everyFourth : pattern).ToClip(Engine.SampleRate, Engine.Bpm, start + (i * length))).ToList();
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

            strips.Add(new ChannelStrip(AtPracticeSpeed(track.EffectiveClips()), track.Effects, track.PlaybackFx) { Sends = track.Sends });
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
        var laneTracks = MachineTracks;
        for (var i = 0; i < Engine.Machines.Length; i++)
        {
            Engine.SetMachineClips(i, i < laneTracks.Count ? AtPracticeSpeed(laneTracks[i].MidiClips) : []);
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

            _liveTarget = LiveTarget();
            _liveTarget.NoteOn(note, velocity);
            _midiActivity = Math.Max(_midiActivity, velocity);
        }
        else
        {
            (_liveTarget ?? Engine.Keys.Voice).NoteOff(note);
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
            Engine.Keys.Voice.SustainPedal(value >= 64);
        }
        else if (kind == MidiKind.PitchBend)
        {
            Engine.Keys.Voice.PitchBend(value);
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
            t.IsDrums ? t.Patterns.Select(p => new PatternData(p.Id, p.Name, p.Bars, p.Encode().ToList(), p.Feel, p.Swing, p.Details().ToList())).ToList() : null,
            t.Machine?.ToString(),
            t.Machine is null ? null : t.ChordPatterns.Select(p => new ChordPatternData(p.Id, p.Name, p.Bars, (int)p.Style, p.Encode(), p.FollowId, p.Feel, p.Articulation)).ToList(),
            t.IsDrums ? t.DrumLanes.Select(l => new DrumLaneData(l.Gain, l.Pan, l.Mute)).ToList() : null,
            t.IsDrums ? t.DrumKitStyle : null,
            t.Sends.Reverb > 0 ? t.Sends.Reverb : null,
            t.Sends.Delay > 0 ? t.Sends.Delay : null,
            t.IsDrums ? t.DrumKitSample : null)).ToList();
        ProjectFile.Write(ProjectPath, new ProjectData(1, Engine.SampleRate, Engine.Bpm, tracks, Engine.BeatsPerBar, Engine.BeatUnit, Sections.Select(x => new SectionData(x.Id, x.Name, x.Start, x.Length)).ToList(), MasterIndex));
        RecentProjects.Add(ProjectPath);
        try
        {
            ProjectBackups.Snapshot(ProjectPath);
        }
        catch (IOException)
        {
        }
    }

    public IReadOnlyList<BackupEntry> Backups => ProjectBackups.List(ProjectPath);

    public void RestoreBackup(BackupEntry backup) => ProjectBackups.Restore(ProjectPath, backup);

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
            if (track.Machine is { } migrateRole && PluckSynth.Find(track.Preset) is null && track.Preset is not ("Synth pluck" or "Synth bass" or "Synth lead") && SfzInstrument.FindPath(track.Preset) is null)
            {
                track.Preset = PluckSynth.DefaultName(migrateRole);
            }

            track.DrumKitStyle = Math.Clamp(d.DrumKitStyle ?? 0, 0, DrumKit.StyleNames.Length - 1);
            track.DrumKitSample = d.DrumKitSample;
            track.Sends.Reverb = Math.Clamp(d.ReverbSend ?? 0, 0, 1);
            track.Sends.Delay = Math.Clamp(d.DelaySend ?? 0, 0, 1);
            for (var i = 0; i < Math.Min(d.DrumLanes?.Count ?? 0, track.DrumLanes.Length); i++)
            {
                track.DrumLanes[i].Gain = d.DrumLanes![i].Gain;
                track.DrumLanes[i].Pan = d.DrumLanes[i].Pan;
                track.DrumLanes[i].Mute = d.DrumLanes[i].Mute;
            }

            foreach (var p in d.ChordPatterns ?? [])
            {
                track.ChordPatterns.Add(ChordPattern.Decode(p.Id, p.Name, track.Machine ?? MachineRole.Guitar, p.Bars, (ChordStyle)p.Style, p.Chords));
                track.ChordPatterns[^1].FollowId = p.Follow;
                track.ChordPatterns[^1].Feel = p.Feel;
                track.ChordPatterns[^1].Articulation = Math.Clamp(p.Articulation, 0, ChordPattern.ArticulationNames.Length - 1);
            }

            foreach (var p in d.Patterns ?? [])
            {
                track.Patterns.Add(DrumPattern.Decode(p.Id, p.Name, p.Bars, p.Lanes));
                track.Patterns[^1].Feel = p.Feel;
                track.Patterns[^1].ApplyDetails(p.Details);
                track.Patterns[^1].Swing = Math.Clamp(p.Swing, 0, DrumPattern.SwingNames.Length - 1);
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
        MasterIndex = Math.Clamp(data.Mastering ?? 0, 0, Mastering.Presets.Count - 1);
        Sections.Clear();
        Sections.AddRange((data.Sections ?? []).Select(x => new SongSection(x.Id, x.Name, x.Start, x.Length)));
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
            var strip = new ChannelStrip(audio, track.Effects, track.Effects.CloneForProcessing(Engine.SampleRate)) { Sends = track.Sends };
            var instrument = track.IsKeys && track.Instrument.Active ? track.Instrument.CreateCopy(Engine.SampleRate) : null;
            try
            {
                if (track.Machine is { } role)
                {
                    using var copy = track.Instrument.Active ? track.Instrument.CreateCopy(Engine.SampleRate) : null;
                    Mixer.Export([], [], Engine.SampleRate, path, 1f, 0f, length, null, null, null, [new MachineMix(role, midi, track.Gain, track.Pan, track.Preset, copy?.Instance, track.Effects.CloneForProcessing(Engine.SampleRate), track.Sends)], bpm: Engine.Bpm);
                }
                else if (track.IsDrums)
                {
                    Mixer.Export([], [], Engine.SampleRate, path, 1f, 0f, length, null, null, new DrumMix(midi, track.Gain, track.Pan, track.DrumLanes, track.DrumKitStyle, track.Sends, track.DrumKitSample), bpm: Engine.Bpm);
                }
                else
                {
                    Mixer.Export([strip], midi, Engine.SampleRate, path, track.Gain, track.Pan, length, track.Preset, instrument?.Instance, bpm: Engine.Bpm);
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

    public int MasterIndex { get; set; }

    private static MelodyDensity? LeadDensity(string section)
    {
        var name = section.ToLowerInvariant();
        if (name.StartsWith("intro") || name.StartsWith("outro") || name.StartsWith("loop"))
        {
            return null;
        }

        if (name.StartsWith("solo"))
        {
            return MelodyDensity.Busy;
        }

        return name.StartsWith("chorus") || name.StartsWith("last chorus") || name.StartsWith("hook") || name.StartsWith("head") || name.StartsWith("drop") || name.StartsWith("turnaround")
            ? MelodyDensity.Medium
            : MelodyDensity.Sparse;
    }

    public string? ApplyTemplate(SongTemplate template)
    {
        var rate = Engine.SampleRate;
        if (rate == 0)
        {
            return "Connect your Komplete Audio first.";
        }

        var bpm = Engine.Bpm;
        var beatsPerBar = Engine.BeatsPerBar;
        var bar = Engine.SamplesPerBeat * beatsPerBar;
        var start = (long)(Math.Ceiling(LengthSamples / bar) * bar);
        var addedTracks = new List<Track>();
        var drums = DrumTrack;
        var guitar = Tracks.FirstOrDefault(t => t.Machine == MachineRole.Guitar);
        var bass = Tracks.FirstOrDefault(t => t.Machine == MachineRole.Bass);
        if (drums is null)
        {
            drums = new Track("Drums", null) { IsDrums = true, ColorIndex = _created++ };
            addedTracks.Add(drums);
        }

        if (guitar is null)
        {
            guitar = new Track("Guitar machine", null) { Machine = MachineRole.Guitar, ColorIndex = _created++, Preset = PluckSynth.DefaultName(MachineRole.Guitar), Gain = 0.6f };
            addedTracks.Add(guitar);
        }

        if (bass is null)
        {
            bass = new Track("Bass machine", null) { Machine = MachineRole.Bass, ColorIndex = _created++, Preset = PluckSynth.DefaultName(MachineRole.Bass), Gain = 0.6f };
            addedTracks.Add(bass);
        }

        var lead = Tracks.FirstOrDefault(t => t.Machine == MachineRole.Lead);
        if (lead is null)
        {
            lead = new Track("Lead machine", null) { Machine = MachineRole.Lead, ColorIndex = _created++, Preset = PluckSynth.DefaultName(MachineRole.Lead), Gain = 0.5f };
            addedTracks.Add(lead);
        }

        var keyQuality = template.Progressions.SelectMany(p => p).FirstOrDefault(c => c.Offset == 0).Quality;
        var leadScale = keyQuality switch
        {
            ChordQuality.Minor => MelodyScale.MinorPentatonic,
            ChordQuality.Seventh => MelodyScale.Blues,
            _ => MelodyScale.MajorPentatonic,
        };
        var leadClips = new List<MidiClip>();

        var drumPatterns = new Dictionary<string, DrumPattern>();
        var fillPatterns = new Dictionary<string, DrumPattern>();
        var guitarPatterns = new Dictionary<(int, ChordStyle), ChordPattern>();
        var bassPatterns = new Dictionary<(int, ChordStyle), ChordPattern>();
        var guitarSource = new Dictionary<int, ChordPattern>();
        var newDrumPatterns = new List<DrumPattern>();
        var newGuitarPatterns = new List<ChordPattern>();
        var newBassPatterns = new List<ChordPattern>();
        var drumClips = new List<MidiClip>();
        var guitarClips = new List<MidiClip>();
        var bassClips = new List<MidiClip>();
        var sections = new List<SongSection>();
        var cursor = start;
        for (var index = 0; index < template.Sections.Count; index++)
        {
            var plan = template.Sections[index];
            var progression = template.Progressions[plan.Progression];
            var chordBars = progression.Count;
            var key = (plan.Progression, plan.Guitar);
            if (!guitarPatterns.TryGetValue(key, out var guitarPattern))
            {
                guitarPattern = new ChordPattern(((char)('A' + guitar.ChordPatterns.Count + newGuitarPatterns.Count)).ToString(), MachineRole.Guitar, chordBars) { Style = plan.Guitar };
                for (var i = 0; i < chordBars; i++)
                {
                    guitarPattern[i] = new Chord((template.KeyRoot + progression[i].Offset) % 12, progression[i].Quality);
                }

                guitarPatterns[key] = guitarPattern;
                newGuitarPatterns.Add(guitarPattern);
                guitarSource.TryAdd(plan.Progression, guitarPattern);
            }

            var bassKey = (plan.Progression, plan.Bass);
            if (!bassPatterns.TryGetValue(bassKey, out var bassPattern))
            {
                bassPattern = new ChordPattern(((char)('A' + bass.ChordPatterns.Count + newBassPatterns.Count)).ToString(), MachineRole.Bass, chordBars) { Style = plan.Bass, FollowId = guitarSource[plan.Progression].Id };
                bassPatterns[bassKey] = bassPattern;
                newBassPatterns.Add(bassPattern);
            }

            if (!drumPatterns.TryGetValue(plan.DrumPreset, out var drumPattern))
            {
                drumPattern = new DrumPattern(plan.DrumPreset);
                drumPattern.ApplyPreset(plan.DrumPreset);
                drumPatterns[plan.DrumPreset] = drumPattern;
                newDrumPatterns.Add(drumPattern);
            }

            DrumPattern? fill = null;
            if (plan.Fill && index < template.Sections.Count - 1)
            {
                if (!fillPatterns.TryGetValue(plan.DrumPreset, out fill))
                {
                    fill = drumPattern.WithFill(fillPatterns.Count % DrumPattern.FillNames.Length, drumPattern.Name + "+");
                    fillPatterns[plan.DrumPreset] = fill;
                    newDrumPatterns.Add(fill);
                }
            }

            var chordLength = (long)Math.Round(chordBars * bar);
            var repeats = Math.Max(1, plan.Bars / chordBars);
            for (var r = 0; r < repeats; r++)
            {
                var at = cursor + (r * chordLength);
                guitarClips.Add(guitarPattern.ToClip(rate, bpm, beatsPerBar, at));
                bassClips.Add(bassPattern.WithChordsFrom(guitarSource[plan.Progression]).ToClip(rate, bpm, beatsPerBar, at));
                if (LeadDensity(plan.Name) is { } density)
                {
                    var chords = Enumerable.Range(0, chordBars).Select(i => guitarPattern[i]).ToList();
                    var seed = HashCode.Combine(template.Name, plan.Progression, density);
                    leadClips.Add(new MidiClip(MelodyGenerator.Generate(rate, bpm, beatsPerBar, template.KeyRoot, leadScale, chordBars, density, seed, chords), at));
                }
            }

            var drumBars = repeats * chordBars;
            for (var b = 0; b < drumBars; b++)
            {
                var useFill = fill is not null && b == drumBars - 1;
                drumClips.Add((useFill ? fill! : drumPattern).ToClip(rate, bpm, cursor + (long)Math.Round(b * bar)));
            }

            sections.Add(new SongSection(Guid.NewGuid().ToString("N")[..8], plan.Name, cursor, (long)Math.Round(plan.Bars * bar)));
            cursor += (long)Math.Round(plan.Bars * bar);
        }

        if (leadClips.Count == 0)
        {
            addedTracks.Remove(lead);
        }

        if (drums.Patterns.Count + newDrumPatterns.Count > 8 || guitar.ChordPatterns.Count + newGuitarPatterns.Count > 8 || bass.ChordPatterns.Count + newBassPatterns.Count > 8)
        {
            return "This project already has too many patterns for the template (the limit is 8 per track). Remove some, or start a new project.";
        }

        Edit(
            "Apply song template",
            () =>
            {
                addedTracks.ForEach(Tracks.Add);
                drums.Patterns.AddRange(newDrumPatterns);
                guitar.ChordPatterns.AddRange(newGuitarPatterns);
                bass.ChordPatterns.AddRange(newBassPatterns);
                drumClips.ForEach(drums.AddMidiClip);
                guitarClips.ForEach(guitar.AddMidiClip);
                bassClips.ForEach(bass.AddMidiClip);
                leadClips.ForEach(lead.AddMidiClip);
                Sections.AddRange(sections);
                ApplyMixerState();
            },
            () =>
            {
                drumClips.ForEach(c => drums.MidiClips.Remove(c));
                guitarClips.ForEach(c => guitar.MidiClips.Remove(c));
                bassClips.ForEach(c => bass.MidiClips.Remove(c));
                leadClips.ForEach(c => lead.MidiClips.Remove(c));
                newDrumPatterns.ForEach(p => drums.Patterns.Remove(p));
                newGuitarPatterns.ForEach(p => guitar.ChordPatterns.Remove(p));
                newBassPatterns.ForEach(p => bass.ChordPatterns.Remove(p));
                sections.ForEach(x => Sections.Remove(x));
                addedTracks.ForEach(t => Tracks.Remove(t));
                ApplyMixerState();
            },
            EditKind.Tracks);
        return null;
    }


    public LoudnessReport? LastLoudness { get; private set; }

    public void ExportMixdown(string path)
    {
        var finalWav = AudioEncoder.IsEncoded(path) ? System.IO.Path.GetTempFileName() : path;
        var wavPath = MasterIndex > 0 ? System.IO.Path.GetTempFileName() : finalWav;
        try
        {
            var keys = Tracks.FirstOrDefault(t => t.IsKeys);
            var machineCopies = Tracks.Where(t => t.Machine is not null).Select(t => (Track: t, Copy: t.Instrument.Active ? t.Instrument.CreateCopy(Engine.SampleRate) : null)).ToList();
            var machineMixes = machineCopies.Select(c => new MachineMix(c.Track.Machine!.Value, c.Track.MidiClips.Select(m => m.Copy(0)).ToList(), c.Track.Gain, c.Track.Pan, c.Track.Preset, c.Copy?.Instance, c.Track.Effects.CloneForProcessing(Engine.SampleRate), c.Track.Sends)).ToList();
            var drumTrack = DrumTrack;
            var drumMix = drumTrack is null ? null : new DrumMix(drumTrack.MidiClips.Select(m => m.Copy(0)).ToList(), drumTrack.Gain, drumTrack.Pan, drumTrack.DrumLanes, drumTrack.DrumKitStyle, drumTrack.Sends, drumTrack.DrumKitSample);
            var instrument = keys is { Instrument.Active: true } ? keys.Instrument.CreateCopy(Engine.SampleRate) : null;
            var strips = Tracks.Where(t => !t.IsMidi).Select(t => new ChannelStrip(t.EffectiveClips(), t.Effects, t.Effects.CloneForProcessing(Engine.SampleRate)) { Sends = t.Sends }).ToList();
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
                    keys?.Preset,
                    instrument?.Instance,
                    drumMix,
                    machineMixes,
                    keys?.Sends,
                    Engine.Bpm);
            }
            finally
            {
                instrument?.Dispose();
                machineCopies.ForEach(c => c.Copy?.Dispose());
                strips.ForEach(s => s.Processor?.Dispose());
            }

            LastLoudness = MasterIndex > 0 && MasterIndex < Mastering.Presets.Count
                ? Mastering.Master(wavPath, finalWav, Mastering.Presets[MasterIndex])
                : Mastering.Measure(wavPath);
            if (finalWav != path)
            {
                AudioEncoder.Convert(finalWav, path);
            }
        }
        finally
        {
            foreach (var temp in new[] { wavPath, finalWav }.Distinct().Where(f => f != path))
            {
                File.Delete(temp);
            }
        }
    }

    public void Dispose()
    {
        Midi.Dispose();
        Engine.Dispose();
    }
}
