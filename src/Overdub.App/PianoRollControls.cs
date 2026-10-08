using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class PianoRollControl : FrameworkElement
{
    public const double RowHeight = 14;
    private const double EdgeGrab = 6;

    private static readonly bool[] BlackKey = [false, true, false, true, false, false, true, false, true, false, true, false];

    private enum Mode
    {
        None,
        Move,
        Resize,
    }

    private Mode _mode;
    private double _startX;
    private int _startPitch;
    private int _lastAudition = -1;

    public PianoRollControl()
    {
        Focusable = true;
        ClipToBounds = true;
    }

    public PianoRollModel? Model { get; set; }
    public long OriginSample { get; set; }
    public double PixelsPerBeat { get; set; } = 48;
    public Brush NoteBrush { get; set; } = Brushes.SteelBlue;

    public event Action? NotesCommitted;

    public event Action<int, bool>? Audition;

    public double XOf(long sample) => (sample - OriginSample) / Model!.SamplesPerBeat * PixelsPerBeat;

    public long SampleAt(double x) => OriginSample + (long)(x / PixelsPerBeat * Model!.SamplesPerBeat);

    public static double YOf(int pitch) => (PianoRollModel.HighestPitch - pitch) * RowHeight;

    public static int PitchAt(double y) => PianoRollModel.HighestPitch - (int)Math.Floor(y / RowHeight);

    public void Refresh() => InvalidateVisual();

    protected override void OnRender(DrawingContext context)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        var row = TryFindResource("Lane") as Brush ?? Brushes.Black;
        var shade = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255));
        var line = new Pen(TryFindResource("Border") as Brush ?? Brushes.Gray, 1);
        context.DrawRectangle(row, null, new Rect(0, 0, width, height));
        for (var pitch = PianoRollModel.LowestPitch; pitch <= PianoRollModel.HighestPitch; pitch++)
        {
            var y = YOf(pitch);
            if (BlackKey[pitch % 12])
            {
                context.DrawRectangle(shade, null, new Rect(0, y, width, RowHeight));
            }

            if (pitch % 12 == 0)
            {
                context.DrawLine(line, new Point(0, y + RowHeight), new Point(width, y + RowHeight));
            }
        }

        if (Model is null)
        {
            return;
        }

        var beatLine = new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x2E, 0x34)), 1);
        var barLine = new Pen(new SolidColorBrush(Color.FromRgb(0x4A, 0x4A, 0x52)), 1);
        var gridLine = new Pen(new SolidColorBrush(Color.FromArgb(60, 0x2E, 0x2E, 0x34)), 1);
        var gridSamples = Model.GridSamples;
        for (var sample = OriginSample; XOf(sample) <= width; sample += gridSamples)
        {
            var x = Math.Round(XOf(sample)) + 0.5;
            var beat = (sample - OriginSample) / Model.SamplesPerBeat;
            var onBeat = Math.Abs(beat - Math.Round(beat)) < 0.0001;
            var onBar = onBeat && (long)Math.Round(beat) % Model.BeatsPerBar == 0;
            if (!onBeat && PixelsPerBeat * gridSamples / Model.SamplesPerBeat < 6)
            {
                continue;
            }

            context.DrawLine(onBar ? barLine : onBeat ? beatLine : gridLine, new Point(x, 0), new Point(x, height));
        }

        for (var i = 0; i < Model.Notes.Count; i++)
        {
            var note = Model.Notes[i];
            var x = XOf(note.Start);
            var w = Math.Max(4, XOf(note.End) - x);
            var rect = new Rect(x, YOf(note.Pitch) + 1, w, RowHeight - 2);
            var opacity = 0.45 + (0.55 * note.Velocity / 127.0);
            context.PushOpacity(opacity);
            context.DrawRoundedRectangle(NoteBrush, null, rect, 2, 2);
            context.Pop();
            if (Model.Selected.Contains(i))
            {
                context.DrawRoundedRectangle(null, new Pen(Brushes.White, 1.5), rect, 2, 2);
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Model is null)
        {
            return;
        }

        Focus();
        var point = e.GetPosition(this);
        var pitch = PitchAt(point.Y);
        var hit = Model.NoteAt(SampleAt(point.X), pitch);
        var control = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (hit >= 0 && e.ClickCount == 2)
        {
            Model.Delete(hit);
            Commit();
            return;
        }

        if (hit >= 0)
        {
            var note = Model.Notes[hit];
            if (!Model.Selected.Contains(hit) || control)
            {
                Model.Select(hit, control);
            }

            _mode = XOf(note.End) - point.X <= EdgeGrab && XOf(note.End) - XOf(note.Start) > 14 ? Mode.Resize : Mode.Move;
        }
        else
        {
            Model.Add(SampleAt(point.X), pitch);
            _mode = Mode.Resize;
            pitch = Math.Clamp(pitch, PianoRollModel.LowestPitch, PianoRollModel.HighestPitch);
        }

        Model.BeginGesture();
        _startX = point.X;
        _startPitch = pitch;
        PlayAudition(pitch);
        CaptureMouse();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (Model is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!IsMouseCaptured)
        {
            var pitch = PitchAt(point.Y);
            var hit = Model.NoteAt(SampleAt(point.X), pitch);
            Cursor = hit >= 0 && XOf(Model.Notes[hit].End) - point.X <= EdgeGrab ? Cursors.SizeWE : hit >= 0 ? Cursors.SizeAll : Cursors.Arrow;
            return;
        }

        var deltaSamples = (long)((point.X - _startX) / PixelsPerBeat * Model.SamplesPerBeat);
        if (_mode == Mode.Move)
        {
            var currentPitch = PitchAt(point.Y);
            Model.MoveSelected(deltaSamples, currentPitch - _startPitch);
            PlayAudition(currentPitch);
        }
        else if (_mode == Mode.Resize)
        {
            Model.ResizeSelected(deltaSamples);
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured || Model is null)
        {
            return;
        }

        ReleaseMouseCapture();
        StopAudition();
        _mode = Mode.None;
        if (Model.EndGesture())
        {
            Commit();
        }

        InvalidateVisual();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (Model is null)
        {
            return;
        }

        var point = e.GetPosition(this);
        var hit = Model.NoteAt(SampleAt(point.X), PitchAt(point.Y));
        if (hit >= 0)
        {
            Model.Delete(hit);
            Commit();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Model is null)
        {
            return;
        }

        if (e.Key == Key.Delete && Model.Selected.Count > 0)
        {
            Model.DeleteSelected();
            Commit();
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Model.SelectAll();
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void Commit()
    {
        InvalidateVisual();
        NotesCommitted?.Invoke();
    }

    private void PlayAudition(int pitch)
    {
        if (pitch == _lastAudition)
        {
            return;
        }

        StopAudition();
        _lastAudition = pitch;
        Audition?.Invoke(pitch, true);
    }

    private void StopAudition()
    {
        if (_lastAudition >= 0)
        {
            Audition?.Invoke(_lastAudition, false);
            _lastAudition = -1;
        }
    }
}

public sealed class PianoKeysControl : FrameworkElement
{
    private static readonly bool[] BlackKey = [false, true, false, true, false, false, true, false, true, false, true, false];
    private static readonly string[] Names = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];

    public double VerticalOffset { get; set; }

    protected override void OnRender(DrawingContext context)
    {
        var dim = TryFindResource("TextDim") as Brush ?? Brushes.Gray;
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        context.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        for (var pitch = PianoRollModel.LowestPitch; pitch <= PianoRollModel.HighestPitch; pitch++)
        {
            var y = PianoRollControl.YOf(pitch) - VerticalOffset;
            if (y + PianoRollControl.RowHeight < 0 || y > ActualHeight)
            {
                continue;
            }

            var black = BlackKey[pitch % 12];
            var fill = black ? new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24)) : new SolidColorBrush(Color.FromRgb(0xE8, 0xE8, 0xEA));
            context.DrawRectangle(fill, new Pen(new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x3F)), 0.5), new Rect(0, y, black ? ActualWidth * 0.62 : ActualWidth, PianoRollControl.RowHeight));
            if (pitch % 12 == 0)
            {
                var label = new FormattedText($"{Names[0]}{(pitch / 12) - 1}", System.Globalization.CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, new Typeface("Segoe UI"), 10, Brushes.Black, dpi);
                context.DrawText(label, new Point(ActualWidth - 26, y + 1));
            }
        }

        context.Pop();
    }
}

public sealed class VelocityControl : FrameworkElement
{
    private int _dragIndex = -1;

    public PianoRollModel? Model { get; set; }
    public PianoRollControl? Roll { get; set; }
    public double HorizontalOffset { get; set; }
    public Brush NoteBrush { get; set; } = Brushes.SteelBlue;

    public event Action? NotesCommitted;

    protected override void OnRender(DrawingContext context)
    {
        var back = TryFindResource("Panel") as Brush ?? Brushes.Black;
        context.DrawRectangle(back, null, new Rect(0, 0, ActualWidth, ActualHeight));
        if (Model is null || Roll is null)
        {
            return;
        }

        context.PushClip(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
        for (var i = 0; i < Model.Notes.Count; i++)
        {
            var note = Model.Notes[i];
            var x = Roll.XOf(note.Start) - HorizontalOffset;
            var h = Math.Max(2, note.Velocity / 127.0 * (ActualHeight - 8));
            var rect = new Rect(x, ActualHeight - 4 - h, 5, h);
            context.DrawRectangle(NoteBrush, Model.Selected.Contains(i) ? new Pen(Brushes.White, 1.2) : null, rect);
        }

        context.Pop();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (Model is null || Roll is null)
        {
            return;
        }

        var x = e.GetPosition(this).X + HorizontalOffset;
        var best = -1;
        var bestDistance = 8.0;
        for (var i = 0; i < Model.Notes.Count; i++)
        {
            var distance = Math.Abs(Roll.XOf(Model.Notes[i].Start) + 2.5 - x);
            if (distance < bestDistance)
            {
                best = i;
                bestDistance = distance;
            }
        }

        if (best < 0)
        {
            return;
        }

        _dragIndex = best;
        if (!Model.Selected.Contains(best))
        {
            Model.Select(best, false);
        }

        Model.BeginGesture();
        SetFrom(e.GetPosition(this).Y);
        CaptureMouse();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragIndex >= 0 && IsMouseCaptured)
        {
            SetFrom(e.GetPosition(this).Y);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_dragIndex < 0 || Model is null)
        {
            return;
        }

        ReleaseMouseCapture();
        _dragIndex = -1;
        if (Model.EndGesture())
        {
            NotesCommitted?.Invoke();
        }
    }

    private void SetFrom(double y)
    {
        if (Model is null)
        {
            return;
        }

        var velocity = (byte)Math.Clamp((int)Math.Round((ActualHeight - 4 - y) / (ActualHeight - 8) * 127), 1, 127);
        Model.SetVelocity(Model.Selected, velocity);
        Roll?.Refresh();
        InvalidateVisual();
    }
}
