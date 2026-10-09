using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Overdub.Audio;

namespace Overdub.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel = new();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        var args = Environment.GetCommandLineArgs();
        if (args.Length > 1 && File.Exists(args[1]))
        {
            _viewModel.Open(args[1]);
        }

        Closed += (_, _) => _viewModel.Dispose();
    }

    private void OnSliderReset(object sender, MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.Slider slider && double.TryParse((string)slider.Tag, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value))
        {
            slider.Value = value;
        }
    }

    private void OnAddTrackClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        for (var i = 0; i < _viewModel.InputCount; i++)
        {
            var input = i;
            var item = new MenuItem { Header = $"Audio, input {input + 1}" };
            item.Click += (_, _) => _viewModel.AddAudioTrack(input);
            menu.Items.Add(item);
        }

        var drums = new MenuItem { Header = "Drum machine", IsEnabled = _viewModel.DrumTrack is null };
        drums.Click += (_, _) => _viewModel.AddDrumTrack();
        menu.Items.Add(drums);
        foreach (var role in Enum.GetValues<MachineRole>())
        {
            var machineRole = role;
            var machine = new MenuItem { Header = role switch { MachineRole.Lead => "Lead guitar (melody)", _ => $"{role} machine (chords)" }, IsEnabled = !_viewModel.MachineLimitReached };
            machine.Click += (_, _) => _viewModel.AddMachineTrack(machineRole);
            menu.Items.Add(machine);
        }

        var midi = new MenuItem { Header = "MIDI keys (built-in synth)" };
        midi.Click += (_, _) => _viewModel.AddMidiTrack();
        menu.Items.Add(midi);
        menu.IsOpen = true;
    }

    private void OnNameMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement { DataContext: TrackViewModel vm, Parent: Grid grid })
        {
            return;
        }

        vm.IsEditing = true;
        Dispatcher.BeginInvoke(() =>
        {
            var box = grid.Children.OfType<TextBox>().First();
            box.Focus();
            box.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnNameLostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackViewModel vm })
        {
            vm.IsEditing = false;
        }
    }

    private void OnNameKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox { DataContext: TrackViewModel vm } box || e.Key is not (Key.Enter or Key.Escape))
        {
            return;
        }

        var binding = System.Windows.Data.BindingOperations.GetBindingExpression(box, TextBox.TextProperty);
        if (e.Key == Key.Enter)
        {
            binding?.UpdateSource();
        }
        else
        {
            binding?.UpdateTarget();
        }

        vm.IsEditing = false;
        e.Handled = true;
    }

    private void OnRemoveMouseLeave(object sender, MouseEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackViewModel vm })
        {
            vm.ResetRemove();
        }
    }

    private async void OnRampClick(object sender, RoutedEventArgs e) => await _viewModel.CycleRampAsync();

    private async void OnSpeedClick(object sender, RoutedEventArgs e) => await _viewModel.CycleSpeedAsync();

    private void OnSnapClick(object sender, RoutedEventArgs e) => _viewModel.CycleSnap();

    private void OnUndoClick(object sender, RoutedEventArgs e) => _viewModel.Undo();

    private void OnRedoClick(object sender, RoutedEventArgs e) => _viewModel.Redo();

    private void OnQuantizeClick(object sender, RoutedEventArgs e) => _viewModel.QuantizeSelection();

    private void OnQuantizeGridClick(object sender, RoutedEventArgs e) => _viewModel.CycleQuantizeGrid();

    private void OnSplitClick(object sender, RoutedEventArgs e) => _viewModel.SplitAtPlayhead();

    private void OnDuplicateClick(object sender, RoutedEventArgs e) => _viewModel.DuplicateSelection();

    private void OnDeleteClick(object sender, RoutedEventArgs e) => _viewModel.DeleteSelection();

    private void OnKeyboardClick(object sender, RoutedEventArgs e) => new KeyboardWindow(_viewModel) { Owner = this }.Show();

    private void OnAnalyzeClick(object sender, RoutedEventArgs e) => new AnalysisWindow(_viewModel) { Owner = this }.Show();

    private void OpenPianoRoll()
    {
        if (_viewModel.FindMidiClipForEditing() is not { } target)
        {
            _viewModel.ShowMessage("Record or add some MIDI notes first, then select the clip.");
            return;
        }

        new PianoRollWindow(_viewModel, target.Track, target.Clip) { Owner = this }.Show();
    }

    private void OnPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: TrackViewModel track } target)
        {
            return;
        }

        var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var option in track.PresetOptions)
        {
            var name = option;
            var item = new MenuItem { Header = name, IsCheckable = true, IsChecked = string.Equals(name, track.Preset, StringComparison.OrdinalIgnoreCase) };
            item.Click += (_, _) => track.SetPreset(name);
            menu.Items.Add(item);
        }

        if (track.Model.Machine is not null)
        {
            menu.Items.Add(new Separator());
            var more = new MenuItem { Header = "Get more sounds..." };
            more.Click += (_, _) => new SoundLibraryWindow(_viewModel, this).Show();
            menu.Items.Add(more);
        }

        menu.IsOpen = true;
    }

    private void OnCoachClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackViewModel { Model.Machine: MachineRole.Bass } track })
        {
            new BassCoachWindow(_viewModel, this, track.Model).Show();
        }
    }

    private void OnMelodyClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackViewModel { Model.Machine: MachineRole.Lead } track })
        {
            new MelodyWindow(_viewModel, this, track.Model).Show();
        }
    }

    private void OnChordsClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackViewModel { Model.Machine: not null } track })
        {
            new ChordWindow(_viewModel, this, track.Model).Show();
        }
    }

    private void OnDrumsClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.DrumTrack is not null)
        {
            new DrumWindow(_viewModel, this).Show();
        }
    }

    private void OnEffectsClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TrackViewModel track })
        {
            new EffectsWindow(_viewModel, track) { Owner = this }.Show();
        }
    }

    private void OnFileClick(object sender, RoutedEventArgs e)
    {
        var menu = MenuFor(sender);
        menu.Items.Add(Item("Open...", () => OnOpenClick(FileButton, new RoutedEventArgs())));
        menu.Items.Add(Item("Save", () => _viewModel.Save()));
        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Import audio...", () => OnImportClick(FileButton, new RoutedEventArgs())));
        menu.Items.Add(Item("Export mixdown...", () => OnExportClick(FileButton, new RoutedEventArgs())));
        menu.Items.Add(Item("Export stems...", () => OnStemsClick(FileButton, new RoutedEventArgs())));
        menu.Items.Add(Item("Export selected MIDI block...", () => _viewModel.ExportSelectedMidi()));
        menu.IsOpen = true;
    }

    private void OnSectionsClick(object sender, RoutedEventArgs e)
    {
        var menu = MenuFor(sender);
        var mark = Item("Mark the selected range as a section...", () =>
        {
            if (NameDialog.Ask(this, "Mark section", "Name for this part of the song:", $"Section {_viewModel.Sections.Count + 1}") is { } name)
            {
                _viewModel.MarkSection(name);
            }
        });
        mark.IsEnabled = _viewModel.HasRegion;
        menu.Items.Add(mark);
        var sections = _viewModel.Sections;
        if (sections.Count == 0)
        {
            menu.Items.Add(new MenuItem { Header = "Drag on the ruler to select a range first", IsEnabled = false });
        }

        foreach (var section in sections)
        {
            var current = section;
            var entry = new MenuItem { Header = current.Name };
            entry.Items.Add(Item("Go to start", () => _viewModel.GoToSection(current)));
            entry.Items.Add(Item("Copy to the playhead", () => _viewModel.CopySectionToPlayhead(current, false)));
            entry.Items.Add(Item("Move to the playhead", () => _viewModel.CopySectionToPlayhead(current, true)));
            entry.Items.Add(Item("Rename...", () =>
            {
                if (NameDialog.Ask(this, "Rename section", "New name:", current.Name) is { } name)
                {
                    _viewModel.RenameSection(current, name);
                }
            }));
            entry.Items.Add(Item("Delete label", () => _viewModel.RemoveSection(current)));
            menu.Items.Add(entry);
        }

        menu.IsOpen = true;
    }

    private void OnToolsClick(object sender, RoutedEventArgs e)
    {
        var menu = MenuFor(sender);
        menu.Items.Add(Item("Sound library...", () => new SoundLibraryWindow(_viewModel, this).Show()));
        menu.Items.Add(Item("Tuner", () => OnTunerClick(ToolsButton, new RoutedEventArgs())));
        menu.Items.Add(Item("On-screen keyboard", () => OnKeyboardClick(ToolsButton, new RoutedEventArgs())));
        menu.Items.Add(Item("Piano roll", OpenPianoRoll));
        menu.Items.Add(Item("Analyze clip", () => OnAnalyzeClick(ToolsButton, new RoutedEventArgs())));
        menu.Items.Add(Item("Measure latency", () => OnLatencyClick(ToolsButton, new RoutedEventArgs())));
        menu.IsOpen = true;
    }

    private static ContextMenu MenuFor(object sender) => new()
    {
        PlacementTarget = (UIElement)sender,
        Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
    };

    private static MenuItem Item(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void OnTunerClick(object sender, RoutedEventArgs e) => new TunerWindow(_viewModel) { Owner = this }.Show();

    private void OnLatencyClick(object sender, RoutedEventArgs e) => new LatencyWindow(_viewModel) { Owner = this }.ShowDialog();

    private double? _rulerDragStart;

    private void OnRulerDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (e.ClickCount == 2)
        {
            _viewModel.ClearRegion();
            return;
        }

        _rulerDragStart = e.GetPosition(Ruler).X;
        Ruler.CaptureMouse();
    }

    private void OnRulerMove(object sender, MouseEventArgs e)
    {
        if (_rulerDragStart is { } start && e.LeftButton == MouseButtonState.Pressed)
        {
            var x = e.GetPosition(Ruler).X;
            if (Math.Abs(x - start) >= 4)
            {
                _viewModel.SetRegion(start, x);
            }
        }
    }

    private void OnRulerUp(object sender, MouseButtonEventArgs e)
    {
        if (_rulerDragStart is not { } start)
        {
            return;
        }

        _rulerDragStart = null;
        Ruler.ReleaseMouseCapture();
        var x = e.GetPosition(Ruler).X;
        if (Math.Abs(x - start) < 4)
        {
            _viewModel.SeekToPixel(x);
        }
        else
        {
            _viewModel.SetRegion(start, x);
        }
    }

    private void OnTapClick(object sender, RoutedEventArgs e) => _viewModel.Tap();

    private void OnSignatureClick(object sender, RoutedEventArgs e) => _viewModel.CycleSignature();

    private void OnCountInClick(object sender, RoutedEventArgs e) => _viewModel.CycleCountIn();

    private void OnWaitClick(object sender, RoutedEventArgs e) => _viewModel.CycleWait();

    private void OnSaveClick(object sender, RoutedEventArgs e) => _viewModel.Save();

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        var recent = RecentProjects.Load();
        if (recent.Count == 0)
        {
            BrowseForProject();
            return;
        }

        var menu = new ContextMenu
        {
            PlacementTarget = (UIElement)sender,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom,
        };
        foreach (var path in recent)
        {
            var item = new MenuItem
            {
                Header = $"{Path.GetFileName(Path.GetDirectoryName(path))}  ({File.GetLastWriteTime(path):d MMM HH:mm})",
                ToolTip = path,
            };
            item.Click += (_, _) => _viewModel.Open(path);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());
        var browse = new MenuItem { Header = "Browse..." };
        browse.Click += (_, _) => BrowseForProject();
        menu.Items.Add(browse);
        menu.IsOpen = true;
    }

    private void BrowseForProject()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Overdub project (project.overdub.json)|*.overdub.json",
            InitialDirectory = Directory.Exists(_viewModel.ProjectFolder) ? _viewModel.ProjectFolder : null,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.Open(dialog.FileName);
        }
    }

    private void OnImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import a backing track",
            Filter = "Audio files|*.wav;*.mp3;*.flac;*.m4a;*.aac;*.wma;*.aif;*.aiff|All files|*.*",
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ImportAudio(dialog.FileName);
        }
    }

    private void OnStemsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Choose a folder for the stems",
            InitialDirectory = Directory.Exists(_viewModel.ProjectFolder) ? _viewModel.ProjectFolder : null,
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.ExportStems(dialog.FolderName);
        }
    }

    private void OnExportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "WAV audio (*.wav)|*.wav|MP3 audio (*.mp3)|*.mp3|FLAC audio (*.flac)|*.flac",
            FileName = Path.GetFileName(_viewModel.DefaultExportName),
            AddExtension = true,
            DefaultExt = ".wav",
            InitialDirectory = Path.GetDirectoryName(_viewModel.DefaultExportName),
        };
        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.Export(dialog.FileName);
        }
    }

    private (TrackViewModel Track, int Lane)? _compGesture;
    private double _compStart;

    private void OnLaneMouseDown(object sender, MouseButtonEventArgs e)
    {
        var point = e.GetPosition(LaneArea);
        if (_viewModel.StripAt(point.Y) is { } strip)
        {
            _compGesture = (strip.Track, strip.Lane);
            _compStart = point.X;
            LaneArea.CaptureMouse();
            e.Handled = true;
            return;
        }

        _viewModel.Select(null);
        _viewModel.SeekToPixel(point.X);
    }

    private void OnLaneMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_compGesture is not { } gesture)
        {
            return;
        }

        _compGesture = null;
        LaneArea.ReleaseMouseCapture();
        _viewModel.CompTake(gesture.Track, gesture.Lane, _compStart, e.GetPosition(LaneArea).X);
    }
}
