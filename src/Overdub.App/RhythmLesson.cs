using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Overdub.Audio;

namespace Overdub.App;

public sealed class RhythmLesson : UserControl
{
    private const int Bars = 4;
    private readonly LearnContext _context;
    private readonly StackPanel _page = new() { Margin = new Thickness(22, 14, 22, 20) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(25) };
    private readonly List<Border> _blocks = [];
    private readonly List<double> _taps = [];
    private TimeSignatureInfo _info;
    private double _bpm;
    private int _subdivision;
    private bool _playing;
    private Stopwatch? _clock;
    private IReadOnlyList<ClickStep> _steps = [];
    private TextBlock _playLabel = new();
    private TextBlock _tapResult = new();
    private Border _playButton = new();

    public RhythmLesson(LearnContext context)
    {
        _context = context;
        var (beats, unit) = context.Main.Signature;
        _info = Rhythm.Find(beats, unit);
        _bpm = Math.Clamp(Math.Round(context.Main.Bpm), 40, 200);
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        _timer.Tick += (_, _) => Paint();
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is false)
            {
                Stop();
            }
        };
        Render();
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private static readonly Brush Blue = Frozen(0x6B, 0x8D, 0xE3);
    private static readonly Brush BlueText = Frozen(0x0E, 0x18, 0x30);
    private static readonly Brush Quiet = Frozen(0x2F, 0x2F, 0x37);

    private void Stop()
    {
        if (_playing)
        {
            _context.Main.StopClick();
        }

        _playing = false;
        _clock = null;
        _timer.Stop();
        Paint();
    }

    private void Choose(TimeSignatureInfo info)
    {
        Stop();
        _info = info;
        _taps.Clear();
        Render();
    }

    private void Render()
    {
        _page.Children.Clear();
        _blocks.Clear();
        var back = new TextBlock { Text = "‹ All lessons", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) =>
        {
            Stop();
            _context.Back();
        };
        _page.Children.Add(back);
        _page.Children.Add(Ui.Title("Rhythm and time signatures"));
        _page.Children.Add(Ui.Sub("See how beats are counted and grouped, hear the click, then tap along to feel it.", 12));
        _page.Children.Add(Ui.Label("Time signature"));
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        foreach (var info in Rhythm.All)
        {
            var chosen = info;
            chips.Children.Add(Chip(info.Name, info == _info, () => Choose(chosen)));
        }

        _page.Children.Add(chips);
        _page.Children.Add(BeatCard());
        var columns = new UniformGrid { Columns = 2 };
        columns.Children.Add(HearCard());
        columns.Children.Add(TapCard());
        _page.Children.Add(columns);
        _page.Children.Add(LengthsCard());
        _page.Children.Add(Buttons());
        Paint();
    }

    private static Border Chip(string text, bool selected, Action click)
    {
        var chip = new Border
        {
            Background = selected ? Ui.AccentTint : Ui.Res("Panel"),
            BorderBrush = selected ? Ui.AccentBrush : Ui.Res("Border"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(11, 5, 11, 5),
            Margin = new Thickness(0, 0, 6, 6),
            Cursor = Cursors.Hand,
            Child = new TextBlock { Text = text, FontSize = 13 },
        };
        chip.OnClick(click);
        return chip;
    }

    private static Border Card(UIElement child, double bottom = 12, double right = 0)
    {
        var card = Ui.Card(child, bottom);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(18, 14, 18, 14);
        card.Margin = new Thickness(0, 0, right, bottom);
        return card;
    }

    private UIElement BeatCard()
    {
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var side = new TextBlock { Text = "one bar", FontSize = 12, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(side, Dock.Right);
        head.Children.Add(side);
        head.Children.Add(new TextBlock { Text = _info.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
        stack.Children.Add(head);

        var grid = new UniformGrid { Columns = _info.Beats, Margin = new Thickness(0, 0, 0, 4) };
        var starts = _info.GroupStarts;
        for (var beat = 0; beat < _info.Beats; beat++)
        {
            var strong = beat == 0;
            var group = !strong && starts.Contains(beat);
            var block = new Border
            {
                Height = 46,
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 6, 0),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.Transparent,
                Background = strong ? Ui.AccentBrush : group ? Blue : Quiet,
                Child = new TextBlock { Text = (beat + 1).ToString(), FontSize = 18, FontWeight = strong || group ? FontWeights.SemiBold : FontWeights.Normal, Foreground = strong ? Ui.AccentText : group ? BlueText : Ui.Res("Text"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            _blocks.Add(block);
            var cell = new StackPanel();
            cell.Children.Add(block);
            cell.Children.Add(new TextBlock { Text = strong ? "strong" : group ? "group" : " ", FontSize = 12, Foreground = Ui.Res("TextDim"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 6, 0) });
            grid.Children.Add(cell);
        }

        stack.Children.Add(grid);
        stack.Children.Add(new TextBlock { Text = _info.Description, FontSize = 13.5, LineHeight = 20, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 8) }.Glossed(_context));
        var tip = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Foreground = Frozen(0xCF, 0xE6, 0xD4) };
        tip.Inlines.Add(new System.Windows.Documents.Run("Try it: ") { FontWeight = FontWeights.SemiBold });
        tip.Inlines.AddRange(Gloss.Inlines(_info.Tip, _context));
        stack.Children.Add(new Border { Background = Frozen(0x1B, 0x2A, 0x22), BorderBrush = Ui.GoodBrush, BorderThickness = new Thickness(3, 0, 0, 0), Padding = new Thickness(12, 8, 12, 8), Child = tip });
        return Card(stack);
    }

    private UIElement HearCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Hear it", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        stack.Children.Add(new TextBlock { Text = "Tempo", FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 4) });
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var tempoText = new TextBlock { Text = $"{_bpm:0} BPM", FontSize = 13, Width = 70, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(tempoText, Dock.Right);
        row.Children.Add(tempoText);
        var slider = new BarSlider { Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        slider.SetValue((_bpm - 40) / 160.0);
        slider.ValueChanged += value =>
        {
            _bpm = Math.Round(40 + (value * 160));
            tempoText.Text = $"{_bpm:0} BPM";
        };
        row.Children.Add(slider);
        stack.Children.Add(row);
        stack.Children.Add(new TextBlock { Text = "Subdivision", FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 4) });
        var subs = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
        for (var i = 0; i < Rhythm.SubdivisionNames.Length; i++)
        {
            var chosen = i;
            subs.Children.Add(Chip(Rhythm.SubdivisionNames[i], i == _subdivision, () =>
            {
                Stop();
                _subdivision = chosen;
                Render();
            }));
        }

        stack.Children.Add(subs);
        _playLabel = new TextBlock { Text = "Play the click", Foreground = Ui.AccentText, FontWeight = FontWeights.SemiBold, FontSize = 13 };
        _playButton = new Border { Background = Ui.AccentBrush, CornerRadius = new CornerRadius(5), Padding = new Thickness(16, 6, 16, 6), Cursor = Cursors.Hand, HorizontalAlignment = HorizontalAlignment.Left, Child = _playLabel, ToolTip = "Play four bars of clicks with a high tick on the strong beat" };
        _playButton.OnClick(TogglePlay);
        stack.Children.Add(_playButton);
        return Card(stack, 12, 10);
    }

    private UIElement TapCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Tap along", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        var pad = new Border
        {
            Height = 64,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = Ui.AccentBrush,
            Background = Ui.AccentTint,
            Cursor = Cursors.Hand,
            Margin = new Thickness(0, 0, 0, 8),
            Child = new TextBlock { Text = "Tap here on the strong beats", FontSize = 15, Foreground = Frozen(0xF0, 0xDC, 0xB8), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        pad.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            Tap();
        };
        stack.Children.Add(pad);
        _tapResult = new TextBlock { Text = "Press Play the click, then tap on the strong beats (the amber and blue blocks).", FontSize = 12.5, LineHeight = 18, Foreground = Frozen(0xCF, 0xCF, 0xD4), TextWrapping = TextWrapping.Wrap };
        stack.Children.Add(_tapResult);
        stack.Children.Add(new TextBlock { Text = "Timing includes a little audio delay, so a steady small offset is normal.", FontSize = 12, Foreground = Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
        return Card(stack);
    }

    private UIElement LengthsCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "Note lengths in this bar", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        var grid = new UniformGrid { Columns = 4 };
        foreach (var (name, meaning) in Rhythm.NoteLengths(_info))
        {
            var cell = new StackPanel();
            cell.Children.Add(new TextBlock { Text = name, FontSize = 12.5, FontWeight = FontWeights.SemiBold });
            cell.Children.Add(new TextBlock { Text = meaning, FontSize = 12.5, Foreground = Frozen(0xD6, 0xD6, 0xDA), TextWrapping = TextWrapping.Wrap });
            grid.Children.Add(new Border { Background = Ui.Res("Well"), BorderBrush = Ui.Res("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 8, 0), Child = cell });
        }

        stack.Children.Add(grid);
        return Card(stack);
    }

    private UIElement Buttons()
    {
        var row = new WrapPanel();
        var current = _context.Main.Signature;
        var already = current == (_info.Beats, _info.Unit);
        var use = Ui.Secondary(already ? $"Your song is in {_info.Name}" : $"Use {_info.Name} in my song", already ? "The song already uses this time signature" : "Change the song's time signature. Existing blocks keep their place, so bar lines may no longer match them.", 190);
        use.IsEnabled = !already;
        use.Click += (_, _) =>
        {
            _context.Main.SetSignature(_info.Beats, _info.Unit);
            Render();
        };
        row.Children.Add(use);
        var songs = Ui.Secondary($"Songs in {_info.Name} ▾", "Well-known music in this time signature", 150);
        songs.Click += (_, _) =>
        {
            var menu = new ContextMenu { PlacementTarget = songs, Placement = PlacementMode.Bottom };
            foreach (var song in _info.Songs)
            {
                menu.Items.Add(new MenuItem { Header = song });
            }

            menu.IsOpen = true;
        };
        row.Children.Add(songs);
        return row;
    }

    private void TogglePlay()
    {
        if (_playing)
        {
            Stop();
            return;
        }

        _steps = Rhythm.Schedule(_info, _bpm, _subdivision, Bars);
        _taps.Clear();
        _tapResult.Text = "Tap on the strong beats (the amber and blue blocks).";
        _playing = true;
        _clock = null;
        var notes = _steps.Select(s => (s.Pitch, s.AtMs, s.LenMs)).ToList();
        var task = _context.Main.HearClick(notes, () => _clock = Stopwatch.StartNew());
        _ = task.ContinueWith(_ => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (_playing)
            {
                Stop();
            }
        })));
        _timer.Start();
        Paint();
    }

    private double ElapsedMs => _clock?.Elapsed.TotalMilliseconds ?? -1;

    private void Paint()
    {
        if (_playLabel is null)
        {
            return;
        }

        _playLabel.Text = _playing ? "Stop" : "Play the click";
        var active = -1;
        var elapsed = ElapsedMs;
        if (_playing && elapsed >= 0)
        {
            var step = _steps.LastOrDefault(s => s.Main && s.AtMs <= elapsed);
            if (step is not null && elapsed < (_steps[^1].AtMs + 400))
            {
                active = step.Beat;
            }
        }

        for (var i = 0; i < _blocks.Count; i++)
        {
            _blocks[i].BorderBrush = i == active ? Brushes.White : Brushes.Transparent;
        }
    }

    private void Tap()
    {
        var elapsed = ElapsedMs;
        if (!_playing || elapsed < 0)
        {
            _tapResult.Text = "Press Play the click first, then tap on the strong beats.";
            return;
        }

        _taps.Add(elapsed);
        var strong = _steps.Where(s => s.Main && _info.GroupStarts.Contains(s.Beat)).Select(s => (double)s.AtMs).ToList();
        var last = _taps.TakeLast(8).ToList();
        var (onTime, early, late, off, average) = Rhythm.JudgeTaps(last, strong);
        if (last.Count == 8)
        {
            GuidedProgress.Report("tap", onTime);
        }

        var parts = new List<string> { $"{onTime} on time" };
        if (late > 0)
        {
            parts.Add($"{late} late");
        }

        if (early > 0)
        {
            parts.Add($"{early} early");
        }

        if (off > 0)
        {
            parts.Add($"{off} between beats");
        }

        var direction = Math.Abs(average) < 5 ? "right on the beat" : $"{Math.Abs(average):0} ms {(average > 0 ? "behind" : "ahead of")} the beat";
        _tapResult.Text = $"Last {last.Count} tap{(last.Count == 1 ? "" : "s")}: {string.Join(", ", parts)}. Your average is {direction}.";
    }
}
