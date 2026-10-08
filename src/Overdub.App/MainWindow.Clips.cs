using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Overdub.Audio;

namespace Overdub.App;

public partial class MainWindow
{
    private const double EdgeGrab = 7;
    private const double MinClipWidth = 4;

    private enum DragMode
    {
        Move,
        TrimLeft,
        TrimRight,
    }

    private FrameworkElement? _dragElement;
    private DragMode _dragMode;
    private double _dragStartX;
    private double _dragOriginLeft;
    private double _dragOriginWidth;
    private bool _dragMoved;

    private void OnClipDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        e.Handled = true;
        var (left, width, model) = element.DataContext switch
        {
            ClipViewModel c => (c.Left, c.Width, (object)c.Model),
            MidiClipViewModel m => (m.Left, m.Width, (object)m.Model),
            _ => (0d, 0d, (object?)null),
        };
        if (model is null)
        {
            return;
        }

        _viewModel.Select(model);
        if (e.ClickCount == 2 && element.DataContext is MidiClipViewModel)
        {
            OpenPianoRoll();
            return;
        }

        var local = e.GetPosition(element).X;
        _dragMode = element.DataContext is ClipViewModel && local <= EdgeGrab ? DragMode.TrimLeft
            : element.DataContext is ClipViewModel && local >= width - EdgeGrab ? DragMode.TrimRight
            : DragMode.Move;
        _dragElement = element;
        _dragStartX = e.GetPosition(LaneArea).X;
        _dragOriginLeft = left;
        _dragOriginWidth = width;
        _dragMoved = false;
        element.CaptureMouse();
    }

    private void OnClipMove(object sender, MouseEventArgs e)
    {
        if (sender is not FrameworkElement element)
        {
            return;
        }

        if (_dragElement != element)
        {
            var x = e.GetPosition(element).X;
            element.Cursor = element.DataContext is ClipViewModel && (x <= EdgeGrab || x >= element.ActualWidth - EdgeGrab)
                ? Cursors.SizeWE
                : Cursors.SizeAll;
            return;
        }

        var dx = e.GetPosition(LaneArea).X - _dragStartX;
        if (!_dragMoved && Math.Abs(dx) < 3)
        {
            return;
        }

        _dragMoved = true;
        if (element.DataContext is ClipViewModel audio)
        {
            var playback = audio.Model.Playback;
            var extendLeft = audio.ToPixels(playback.Offset);
            var extendRight = audio.ToPixels(playback.Samples.Length - playback.Offset - playback.Length);
            switch (_dragMode)
            {
                case DragMode.Move:
                    audio.Left = Math.Max(0, _dragOriginLeft + dx);
                    break;
                case DragMode.TrimLeft:
                    var left = Math.Clamp(_dragOriginLeft + dx, Math.Max(0, _dragOriginLeft - extendLeft), _dragOriginLeft + _dragOriginWidth - MinClipWidth);
                    audio.Left = left;
                    audio.Width = _dragOriginLeft + _dragOriginWidth - left;
                    break;
                default:
                    audio.Width = Math.Clamp(_dragOriginWidth + dx, MinClipWidth, _dragOriginWidth + extendRight);
                    break;
            }
        }
        else if (element.DataContext is MidiClipViewModel midi)
        {
            midi.Left = Math.Max(0, _dragOriginLeft + dx);
        }
    }

    private void OnClipUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragElement is null || sender != _dragElement)
        {
            return;
        }

        var element = _dragElement;
        _dragElement = null;
        element.ReleaseMouseCapture();
        if (!_dragMoved)
        {
            return;
        }

        var row = _viewModel.RowAt(e.GetPosition(LaneArea).Y);
        switch (element.DataContext)
        {
            case ClipViewModel audio when _dragMode == DragMode.Move:
                _viewModel.CommitClipMove(audio, audio.Left, row);
                break;
            case ClipViewModel audio:
                _viewModel.CommitClipTrim(audio, audio.Left, audio.Width);
                break;
            case MidiClipViewModel midi:
                _viewModel.CommitMidiMove(midi, midi.Left, row);
                break;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox)
        {
            return;
        }

        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        switch (e.Key)
        {
            case Key.Space:
                _viewModel.PlayCommand.Execute(null);
                break;
            case Key.R when !control:
                _viewModel.RecordCommand.Execute(null);
                break;
            case Key.S when !control:
                _viewModel.SplitAtPlayhead();
                break;
            case Key.Delete:
                _viewModel.DeleteSelection();
                break;
            case Key.Z when control && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
            case Key.Y when control:
                _viewModel.Redo();
                break;
            case Key.Z when control:
                _viewModel.Undo();
                break;
            case Key.D when control:
                _viewModel.DuplicateSelection();
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
