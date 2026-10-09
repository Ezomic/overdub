using System.Windows;
using System.Windows.Controls;
using Overdub.Audio;

namespace Overdub.App;

public partial class PianoRollWindow : Window
{
    private static readonly int[] Divisions = [4, 8, 16, 32];

    private readonly MainViewModel _viewModel;
    private readonly TrackViewModel _track;
    private readonly PianoRollModel _model;
    private readonly int _index;
    private readonly long _origin;
    private bool _committing;

    public PianoRollWindow(MainViewModel viewModel, TrackViewModel track, MidiClip clip)
    {
        _viewModel = viewModel;
        _track = track;
        _index = track.Model.MidiClips.IndexOf(clip);
        InitializeComponent();
        Title = $"Piano roll: {track.Name}";
        var engine = viewModel.Engine;
        _model = new PianoRollModel(clip.NoteData(), engine.SamplesPerBeat, engine.BeatsPerBar, engine.BeatUnit);
        var barSamples = engine.SamplesPerBeat * engine.BeatsPerBar;
        _origin = (long)(Math.Floor(clip.StartSample / barSamples) * barSamples);

        Roll.Model = _model;
        Roll.OriginSample = _origin;
        Roll.NoteBrush = track.Color;
        Roll.NotesCommitted += OnCommitted;
        Roll.Audition += (pitch, on) => _viewModel.PlayNote((byte)pitch, on ? (byte)90 : (byte)0);
        Velocity.Model = _model;
        Velocity.Roll = Roll;
        Velocity.NoteBrush = track.Color;
        Velocity.NotesCommitted += OnCommitted;

        Roll.Height = (PianoRollModel.HighestPitch - PianoRollModel.LowestPitch + 1) * PianoRollControl.RowHeight;
        UpdateGridButton();
        UpdateWidth();
        Loaded += (_, _) => ScrollToNotes();
        _viewModel.EditHistoryChanged += OnHistoryChanged;
        Closed += (_, _) => _viewModel.EditHistoryChanged -= OnHistoryChanged;
    }

    private void UpdateWidth()
    {
        var end = _model.Notes.Count == 0 ? _origin : _model.Notes.Max(n => n.End);
        var beats = ((end - _origin) / _model.SamplesPerBeat) + (4 * _model.BeatsPerBar);
        Roll.Width = Math.Max(900, beats * Roll.PixelsPerBeat);
        Roll.Refresh();
        Velocity.InvalidateVisual();
    }

    private void ScrollToNotes()
    {
        var pitch = _model.Notes.Count == 0 ? 60 : (int)_model.Notes.Average(n => n.Pitch);
        Scroller.ScrollToVerticalOffset(Math.Max(0, PianoRollControl.YOf(pitch) - (Scroller.ViewportHeight / 2)));
    }

    private void UpdateGridButton() => GridButton.Content = $"Grid 1/{_model.Division}";

    private void OnCommitted()
    {
        var track = _track.Model;
        if (_index >= track.MidiClips.Count)
        {
            Close();
            return;
        }

        _committing = true;
        _viewModel.EditMidiNotes(track, _index, _model.Notes);
        _committing = false;
        UpdateWidth();
    }

    private void OnHistoryChanged()
    {
        if (_committing)
        {
            return;
        }

        var track = _track.Model;
        if (_index >= track.MidiClips.Count)
        {
            Close();
            return;
        }

        _model.Replace(track.MidiClips[_index].NoteData());
        _model.Selected.Clear();
        UpdateWidth();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        Keys.VerticalOffset = e.VerticalOffset;
        Keys.InvalidateVisual();
        Velocity.HorizontalOffset = e.HorizontalOffset;
        Velocity.InvalidateVisual();
    }

    private void OnGridClick(object sender, RoutedEventArgs e)
    {
        _model.Division = Divisions[(Array.IndexOf(Divisions, _model.Division) + 1) % Divisions.Length];
        UpdateGridButton();
        Roll.Refresh();
    }

    private void OnKeyClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = KeyButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var off = new MenuItem { Header = "Off" };
        off.Click += (_, _) => SetKey(null);
        menu.Items.Add(off);
        for (var root = 0; root < 12; root++)
        {
            var key = root;
            var item = new MenuItem { Header = Chord.Roots[key], IsChecked = _model.Key == key };
            item.Click += (_, _) => SetKey(key);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void SetKey(int? key)
    {
        _model.Key = key;
        KeyButton.Content = key is null ? "Key: off" : $"Key: {Chord.Roots[key.Value]}";
        Roll.Refresh();
    }

    private void OnScaleClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ScaleButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var scale in Enum.GetValues<MelodyScale>())
        {
            var choice = scale;
            var item = new MenuItem { Header = MelodyGenerator.ScaleName(choice), IsChecked = _model.Scale == choice };
            item.Click += (_, _) =>
            {
                _model.Scale = choice;
                ScaleButton.Content = MelodyGenerator.ScaleName(choice);
                Roll.Refresh();
            };
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void OnSnapScale(object sender, RoutedEventArgs e)
    {
        _model.SnapToScale = SnapButton.IsChecked == true;
        if (_model.SnapToScale && _model.Key is null)
        {
            _viewModel.ReportProblem("Pick a key first, then notes can snap to its scale.");
        }
    }

    private void OnZoomIn(object sender, RoutedEventArgs e) => Zoom(1.5);

    private void OnZoomOut(object sender, RoutedEventArgs e) => Zoom(1 / 1.5);

    private void Zoom(double factor)
    {
        Roll.PixelsPerBeat = Math.Clamp(Roll.PixelsPerBeat * factor, 12, 400);
        UpdateWidth();
    }

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        _model.SelectAll();
        Roll.Refresh();
        Velocity.InvalidateVisual();
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_model.Selected.Count == 0)
        {
            return;
        }

        _model.DeleteSelected();
        OnCommitted();
        Velocity.InvalidateVisual();
    }
}
