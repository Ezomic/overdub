using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class SoundDesignerView : UserControl
{
    private readonly MainViewModel _main;
    private readonly Action _back;
    private readonly WaveformEditorControl _wave = new() { Height = 130 };
    private readonly BarSlider _morph = new();
    private readonly EnvelopeEditorControl _envelope = new() { Height = 70 };
    private readonly Dictionary<string, BarSlider> _sliders = [];
    private DesignedSound _sound = DesignedSound.Starters[0].Copy();
    private string _startName = DesignedSound.Starters[0].Name;
    private string? _savedName;
    private StackPanel _starters = new();

    public SoundDesignerView(MainViewModel main, Action back)
    {
        _main = main;
        _back = back;
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = Build() };
        Load(DesignedSound.Starters[0]);
    }

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private UIElement Build()
    {
        var page = new StackPanel { Margin = new Thickness(22, 14, 22, 20) };
        var back = new TextBlock { Text = "‹ Sounds", FontSize = 13, Foreground = Ui.AccentBrush, Cursor = Cursors.Hand, Margin = new Thickness(0, 0, 0, 8), HorizontalAlignment = HorizontalAlignment.Left };
        back.MouseLeftButtonUp += (_, _) => _back();
        page.Children.Add(back);
        page.Children.Add(Ui.Title("Make your own bass sound"));
        page.Children.Add(Ui.Sub("Shape the waveform, then the filter and punch. Press Hear it to listen while you tweak.", 12));

        var startRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        startRow.Children.Add(new TextBlock { Text = "Start from", FontSize = 12, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 6) });
        _starters = new StackPanel { Orientation = Orientation.Horizontal };
        startRow.Children.Add(_starters);
        page.Children.Add(startRow);

        page.Children.Add(WaveformCard());

        var columns = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 0) };
        columns.Children.Add(Group("2. Weight", 10, ("Sub (one octave down)", "sub"), ("Thickness (detune)", "thickness")));
        columns.Children.Add(Group("3. Tone", 0, ("Brightness (filter)", "brightness"), ("Growl (drive)", "growl")));
        page.Children.Add(columns);

        page.Children.Add(EnvelopeCard());
        page.Children.Add(Actions());
        return page;
    }

    private static Border Card(UIElement child, double bottom = 10)
    {
        var card = Ui.Card(child, bottom);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(18, 12, 18, 14);
        return card;
    }

    private UIElement WaveformCard()
    {
        var stack = new StackPanel();
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
        var hint = new TextBlock { Text = "Draw on it, or slide to morph", FontSize = 12, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(hint, Dock.Right);
        head.Children.Add(hint);
        head.Children.Add(new TextBlock { Text = "1. Waveform", FontSize = 16, FontWeight = FontWeights.SemiBold });
        stack.Children.Add(head);
        _wave.Drawn += () => _sound.Morph = DesignedSound.CustomMorph;
        stack.Children.Add(_wave);

        var morphRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        morphRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(60) });
        morphRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        morphRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        morphRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        morphRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = Small("Morph", 0xD6, 0xD6, 0xDA);
        label.FontSize = 12.5;
        var sine = Small("Sine", 0x9A, 0x9A, 0xA5);
        sine.Margin = new Thickness(0, 0, 10, 0);
        var saw = Small("Saw", 0x9A, 0x9A, 0xA5);
        saw.Margin = new Thickness(10, 0, 14, 0);
        var square = Small("Square", 0x9A, 0x9A, 0xA5);
        Grid.SetColumn(sine, 1);
        Grid.SetColumn(_morph, 2);
        Grid.SetColumn(saw, 3);
        Grid.SetColumn(square, 4);
        _morph.VerticalAlignment = VerticalAlignment.Center;
        _morph.ValueChanged += value =>
        {
            _sound.Morph = value;
            ShowWave(DesignedSound.Shape(value));
        };
        foreach (var element in new UIElement[] { label, sine, _morph, saw, square })
        {
            morphRow.Children.Add(element);
        }

        stack.Children.Add(morphRow);

        var shapes = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        shapes.Children.Add(ShapeButton("Sine", () => SetShape(0, DesignedSound.Shape(0))));
        shapes.Children.Add(ShapeButton("Triangle", () => SetShape(DesignedSound.CustomMorph, DesignedSound.Triangle())));
        shapes.Children.Add(ShapeButton("Saw", () => SetShape(0.5, DesignedSound.Shape(0.5))));
        shapes.Children.Add(ShapeButton("Square", () => SetShape(1, DesignedSound.Shape(1))));
        shapes.Children.Add(ShapeButton("Smooth it", () =>
        {
            _sound.Morph = DesignedSound.CustomMorph;
            ShowWave(DesignedSound.Smooth(_wave.Wave));
        }));
        stack.Children.Add(shapes);
        stack.Children.Add(new TextBlock
        {
            Text = "A sine is pure and deep. A saw is buzzy and bright. A square is hollow. Your drawn shape is a mix of all of them.",
            FontSize = 12.5,
            LineHeight = 18,
            Foreground = Frozen(0xCF, 0xCF, 0xD4),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
        });
        return Card(stack);
    }

    private static TextBlock Small(string text, byte r, byte g, byte b) => new() { Text = text, FontSize = 12, Foreground = Frozen(r, g, b), VerticalAlignment = VerticalAlignment.Center };

    private Button ShapeButton(string text, Action click)
    {
        var button = Ui.Secondary(text, null, double.NaN);
        button.Margin = new Thickness(0, 0, 8, 4);
        button.Height = 26;
        button.Click += (_, _) => click();
        return button;
    }

    private void SetShape(double morph, float[] wave)
    {
        _sound.Morph = morph;
        _morph.SetValue(morph < 0 ? _morph.Value : morph);
        ShowWave(wave);
    }

    private void ShowWave(float[] wave)
    {
        _sound.Wave = wave;
        _wave.Wave = wave;
        _wave.InvalidateVisual();
    }

    private UIElement Group(string title, double right, (string Label, string Key) first, (string Label, string Key) second)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
        foreach (var (label, key) in new[] { first, second })
        {
            var text = new TextBlock { Text = label, FontSize = 12.5, Foreground = Frozen(0xD6, 0xD6, 0xDA), Margin = new Thickness(0, 0, 0, 2) };
            var slider = new BarSlider { Margin = new Thickness(0, 0, 0, 8) };
            slider.ValueChanged += value => Set(key, value);
            _sliders[key] = slider;
            stack.Children.Add(text);
            stack.Children.Add(slider);
        }

        var card = Card(stack);
        card.Margin = new Thickness(0, 0, right, 10);
        return card;
    }

    private void Set(string key, double value)
    {
        switch (key)
        {
            case "sub":
                _sound.Sub = (float)value;
                break;
            case "thickness":
                _sound.Thickness = (float)value;
                break;
            case "brightness":
                _sound.Brightness = (float)value;
                break;
            default:
                _sound.Growl = (float)value;
                break;
        }
    }

    private UIElement EnvelopeCard()
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock { Text = "4. Shape of each note", FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) });
        _envelope.Changed += () =>
        {
            _sound.AttackN = _envelope.AttackN;
            _sound.DecayN = _envelope.DecayN;
            _sound.Sustain = _envelope.Sustain;
            _sound.ReleaseN = _envelope.ReleaseN;
        };
        stack.Children.Add(_envelope);
        stack.Children.Add(new TextBlock { Text = "Short and snappy gives a pluck. Long and flat gives a held note. Drag the dots.", FontSize = 12.5, Foreground = Frozen(0xCF, 0xCF, 0xD4), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
        return Card(stack, 12);
    }

    private UIElement Actions()
    {
        var row = new WrapPanel();
        var hear = Ui.Accent("Hear it", "Play a short bass riff with this sound");
        hear.Margin = new Thickness(0, 0, 8, 0);
        hear.OnClick(async () => await _main.HearDesigned(_sound));
        row.Children.Add(hear);
        var save = Ui.Secondary("Save as a sound", "Keep this sound so you can pick it for any machine", 130);
        save.Click += (_, _) => Save();
        row.Children.Add(save);
        var use = Ui.Secondary("Use on my bass machine", "Save the sound and switch your bass machine to it", 180);
        use.Click += (_, _) => UseOnBass();
        row.Children.Add(use);
        return row;
    }

    private void Load(DesignedSound source)
    {
        _sound = source.Copy();
        _startName = source.Name;
        _savedName = null;
        ShowWave(_sound.Wave);
        _morph.SetValue(_sound.Morph < 0 ? 0 : _sound.Morph);
        _sliders["sub"].SetValue(_sound.Sub);
        _sliders["thickness"].SetValue(_sound.Thickness);
        _sliders["brightness"].SetValue(_sound.Brightness);
        _sliders["growl"].SetValue(_sound.Growl);
        _envelope.AttackN = _sound.AttackN;
        _envelope.DecayN = _sound.DecayN;
        _envelope.Sustain = _sound.Sustain;
        _envelope.ReleaseN = _sound.ReleaseN;
        _envelope.InvalidateVisual();
        FillStarters();
    }

    private void FillStarters()
    {
        _starters.Children.Clear();
        foreach (var starter in DesignedSound.Starters)
        {
            var chosen = starter;
            _starters.Children.Add(Chip(chosen.Name, chosen.Name == _startName, () => Load(chosen)));
        }

        foreach (var mine in DesignedSounds.List())
        {
            var chosen = mine;
            _starters.Children.Add(Chip(chosen.Name, false, () =>
            {
                Load(chosen);
                _savedName = chosen.Name;
            }));
        }
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

    private bool Save()
    {
        var owner = Window.GetWindow(this);
        if (owner is null)
        {
            return false;
        }

        var name = NameDialog.Ask(owner, "Save sound", "Name your sound", _savedName ?? (_startName + " (mine)"));
        if (name is null)
        {
            return false;
        }

        _sound.Name = name;
        DesignedSounds.Save(_sound);
        _savedName = name;
        FillStarters();
        _main.ShowNotice($"Saved \"{name}\". Pick it from Change sound on any machine.");
        return true;
    }

    private void UseOnBass()
    {
        var bass = _main.Tracks.FirstOrDefault(t => t.Model.Machine == MachineRole.Bass);
        if (bass is null)
        {
            _main.ShowMessage("Add a bass machine first: go to Build a song and use Add a player.");
            return;
        }

        if (_savedName is null || !string.Equals(_savedName, _sound.Name, StringComparison.Ordinal) || DesignedSounds.Find(DesignedSounds.PresetName(_savedName)) is null)
        {
            if (!Save())
            {
                return;
            }
        }
        else
        {
            DesignedSounds.Save(_sound);
        }

        bass.SetPreset(DesignedSounds.PresetName(_sound.Name));
        _main.ShowNotice($"Your bass machine now plays \"{_sound.Name}\".");
    }
}
