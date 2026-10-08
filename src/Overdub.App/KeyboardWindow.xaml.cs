using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Overdub.App;

public partial class KeyboardWindow : Window
{
    private const int Octaves = 3;
    private const double WhiteWidth = 34;
    private const double WhiteHeight = 150;
    private const double BlackWidth = 22;
    private const double BlackHeight = 92;

    private static readonly string[] NoteNames = ["C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B"];
    private static readonly bool[] IsBlack = [false, true, false, true, false, false, true, false, true, false, true, false];
    private static readonly char[] ComputerKeys = ['A', 'W', 'S', 'E', 'D', 'F', 'T', 'G', 'Y', 'H', 'U', 'J', 'K', 'O', 'L', 'P'];

    private readonly MainViewModel _viewModel;
    private readonly Dictionary<int, Button> _keys = [];
    private readonly HashSet<Key> _heldKeys = [];
    private int _baseNote = 48;
    private int? _mouseNote;
    private bool _mouseHeld;

    public KeyboardWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        Build();
        _viewModel.ScreenKeyboardOpen = true;
        _viewModel.NoteActivity += OnNoteActivity;
        Closed += (_, _) =>
        {
            _viewModel.NoteActivity -= OnNoteActivity;
            _viewModel.ScreenKeyboardOpen = false;
            if (_mouseNote is { } held)
            {
                _viewModel.PlayNote((byte)held, 0);
            }

            _viewModel.Sustain(false);
        };
        PreviewKeyDown += OnKeyDown;
        PreviewKeyUp += OnKeyUp;
        MouseLeftButtonUp += (_, _) => ReleaseMouse();
    }

    private static string NameOf(int note) => $"{NoteNames[note % 12]}{(note / 12) - 1}";

    private void Build()
    {
        Keys.Children.Clear();
        _keys.Clear();
        var whiteIndex = 0;
        var notes = Enumerable.Range(_baseNote, (Octaves * 12) + 1).ToList();
        foreach (var note in notes.Where(n => !IsBlack[n % 12]))
        {
            AddKey(note, whiteIndex * WhiteWidth, WhiteWidth, WhiteHeight, Brushes.WhiteSmoke, 0);
            whiteIndex++;
        }

        whiteIndex = 0;
        foreach (var note in notes)
        {
            if (!IsBlack[note % 12])
            {
                whiteIndex++;
                continue;
            }

            AddKey(note, (whiteIndex * WhiteWidth) - (BlackWidth / 2), BlackWidth, BlackHeight, new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x27)), 1);
        }

        RangeText.Text = $"{NameOf(_baseNote)} to {NameOf(_baseNote + (Octaves * 12))}";
    }

    private void AddKey(int note, double left, double width, double height, Brush brush, int z)
    {
        var key = new Button
        {
            Width = width,
            Height = height,
            Style = (Style)FindResource("PianoKey"),
            Background = brush,
            Tag = (note, brush),
            ToolTip = NameOf(note),
        };
        System.Windows.Automation.AutomationProperties.SetName(key, $"Key {NameOf(note)}");
        Canvas.SetLeft(key, left);
        Panel.SetZIndex(key, z);
        key.PreviewMouseLeftButtonDown += (_, e) => PressKey(note, key, e);
        key.MouseEnter += (_, _) =>
        {
            if (_mouseHeld && Mouse.LeftButton == MouseButtonState.Pressed)
            {
                SwitchTo(note, key);
            }
        };
        key.Click += (_, _) => ClickKey(note);
        _keys[note] = key;
        Keys.Children.Add(key);
    }

    private bool _mouseDown;

    private void PressKey(int note, Button key, MouseButtonEventArgs e)
    {
        _mouseDown = true;
        _mouseHeld = true;
        var y = e.GetPosition(key).Y / Math.Max(1, key.ActualHeight);
        _velocity = (byte)Math.Clamp(45 + (int)(y * 82), 30, 127);
        SwitchTo(note, key);
        e.Handled = true;
    }

    private byte _velocity = 100;

    private void SwitchTo(int note, Button key)
    {
        if (_mouseNote == note)
        {
            return;
        }

        if (_mouseNote is { } previous)
        {
            _viewModel.PlayNote((byte)previous, 0);
        }

        _mouseNote = note;
        _viewModel.PlayNote((byte)note, _velocity);
    }

    private void ReleaseMouse()
    {
        _mouseHeld = false;
        if (_mouseNote is { } note)
        {
            _viewModel.PlayNote((byte)note, 0);
            _mouseNote = null;
        }
    }

    private async void ClickKey(int note)
    {
        if (_mouseDown)
        {
            _mouseDown = false;
            return;
        }

        _viewModel.PlayNote((byte)note, 100);
        await Task.Delay(900);
        _viewModel.PlayNote((byte)note, 0);
    }

    private void OnNoteActivity(byte note, byte velocity) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (_keys.TryGetValue(note, out var key) && key.Tag is ValueTuple<int, Brush> tag)
            {
                key.Background = velocity > 0 ? (Brush)FindResource("Good") : tag.Item2;
            }
        });

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.IsRepeat || Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        if (e.Key == Key.Z || e.Key == Key.X)
        {
            Shift(e.Key == Key.Z ? -12 : 12);
            e.Handled = true;
            return;
        }

        var index = IndexOf(e.Key);
        if (index < 0 || _heldKeys.Contains(e.Key))
        {
            return;
        }

        _heldKeys.Add(e.Key);
        _viewModel.PlayNote((byte)(_baseNote + 12 + index), 100);
        e.Handled = true;
    }

    private void OnKeyUp(object sender, KeyEventArgs e)
    {
        var index = IndexOf(e.Key);
        if (index >= 0 && _heldKeys.Remove(e.Key))
        {
            _viewModel.PlayNote((byte)(_baseNote + 12 + index), 0);
            e.Handled = true;
        }
    }

    private static int IndexOf(Key key)
    {
        var text = key == Key.OemSemicolon ? ';' : key.ToString().Length == 1 ? key.ToString()[0] : '\0';
        return Array.IndexOf(ComputerKeys, text);
    }

    private void Shift(int semitones)
    {
        _baseNote = Math.Clamp(_baseNote + semitones, 24, 84);
        Build();
    }

    private void OnOctaveDown(object sender, RoutedEventArgs e) => Shift(-12);

    private void OnOctaveUp(object sender, RoutedEventArgs e) => Shift(12);

    private void OnSustain(object sender, RoutedEventArgs e) => _viewModel.Sustain(SustainToggle.IsChecked == true);
}
