using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class SoundPickerWindow : Window
{
    private sealed record Entry(string Pack, string Kind, string Title, string Name, string Description);

    private const string ItemStyleXaml = """
        <Style xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" TargetType="ListBoxItem">
            <Setter Property="Padding" Value="10,5" />
            <Setter Property="Foreground" Value="#E8E8EA" />
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ListBoxItem">
                        <Border x:Name="Bd" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" Background="Transparent" Padding="{TemplateBinding Padding}" CornerRadius="3">
                            <ContentPresenter />
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="Bd" Property="Background" Value="#2B2B30" />
                            </Trigger>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="Bd" Property="Background" Value="#3A3A44" />
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Foreground" Value="#9A9AA2" />
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
        """;

    private readonly MainViewModel _main;
    private readonly TrackViewModel _track;
    private readonly List<Entry> _entries;
    private readonly TextBox _search = new() { Padding = new Thickness(6, 4, 6, 4), VerticalContentAlignment = VerticalAlignment.Center };
    private readonly ListBox _packs = new();
    private readonly ListBox _sounds = new();
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _use;
    private readonly Button _preview;
    private bool _filling;

    public SoundPickerWindow(MainViewModel main, Window owner, TrackViewModel track)
    {
        _main = main;
        _track = track;
        Owner = owner;
        Icon = owner.Icon;
        Title = $"Sound: {track.Name}";
        Width = 680;
        Height = 580;
        MinWidth = 560;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        _entries = BuildEntries(track.Model.Machine!.Value);

        var itemStyle = (Style)XamlReader.Parse(ItemStyleXaml);
        foreach (var list in new[] { _packs, _sounds })
        {
            list.Background = (Brush)FindResource("Panel");
            list.BorderBrush = (Brush)FindResource("Border");
            list.BorderThickness = new Thickness(1);
            list.Foreground = (Brush)FindResource("Text");
            list.ItemContainerStyle = itemStyle;
            list.Padding = new Thickness(4);
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        }

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var library = TransportButton("Sound library...", 120);
        library.Click += (_, _) =>
        {
            new SoundLibraryWindow(_main, this).ShowDialog();
            SoundPrograms.Invalidate();
            Reload();
        };
        DockPanel.SetDock(library, Dock.Right);
        top.Children.Add(library);
        var sample = TransportButton("From a recording...", 130);
        sample.Click += (_, _) =>
        {
            var dialog = new SampleInstrumentWindow(_main, this, _track);
            dialog.ShowDialog();
            Reload();
            if (dialog.CreatedPreset is not null)
            {
                Close();
            }
        };
        DockPanel.SetDock(sample, Dock.Right);
        top.Children.Add(sample);
        var searchLabel = new TextBlock { Text = "Search", Foreground = (Brush)FindResource("TextDim"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        DockPanel.SetDock(searchLabel, Dock.Left);
        top.Children.Add(searchLabel);
        _search.Margin = new Thickness(0, 0, 10, 0);
        _search.TextChanged += (_, _) => FillSounds();
        top.Children.Add(_search);

        var lists = new Grid();
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        lists.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(_sounds, 2);
        lists.Children.Add(_packs);
        lists.Children.Add(_sounds);
        _packs.SelectionChanged += (_, _) =>
        {
            if (!_filling && _search.Text.Length == 0)
            {
                FillSounds();
            }
        };
        _sounds.SelectionChanged += (_, _) => UpdateDescription();
        _sounds.MouseDoubleClick += (_, _) => Use();

        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _preview = TransportButton("Preview", 90);
        _preview.Click += async (_, _) => await Preview();
        _use = TransportButton("Use this sound", 120);
        _use.IsDefault = true;
        _use.Click += (_, _) => Use();
        var cancel = TransportButton("Cancel", 80);
        cancel.IsCancel = true;
        buttons.Children.Add(_preview);
        buttons.Children.Add(_use);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        _description.Foreground = (Brush)FindResource("TextDim");
        _description.Margin = new Thickness(0, 0, 12, 0);
        bottom.Children.Add(_description);

        var root = new DockPanel { Margin = new Thickness(18) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(bottom);
        root.Children.Add(lists);
        Content = root;
        Reload();
        Loaded += (_, _) => _search.Focus();
    }

    private static List<Entry> BuildEntries(MachineRole role)
    {
        var entries = PluckSynth.BuiltInNames(role)
            .Select(name => new Entry("Built-in", "Built-in", name, name, "Overdub's own plucked-string and synth voices. No samples needed, so they load instantly."))
            .ToList();
        entries.AddRange(SoundPrograms.Discover()
            .Where(p => (role == MachineRole.Bass) == p.IsBass)
            .Select(p => new Entry(p.Pack, p.Kind, p.Title, p.Name, p.Description)));
        return entries;
    }

    private Button TransportButton(string text, double width)
    {
        return new Button
        {
            Content = text,
            Style = (Style)FindResource("TransportButton"),
            Width = width,
            Height = 28,
            FontSize = 12,
            Padding = new Thickness(12, 0, 12, 0),
            Focusable = false,
            Margin = new Thickness(8, 0, 0, 0),
        };
    }

    private void Reload()
    {
        _entries.Clear();
        _entries.AddRange(BuildEntries(_track.Model.Machine!.Value));
        _filling = true;
        _packs.Items.Clear();
        var currentPack = _entries.FirstOrDefault(e => string.Equals(e.Name, _track.Preset, StringComparison.OrdinalIgnoreCase))?.Pack ?? "Built-in";
        ListBoxItem? select = null;
        foreach (var kind in _entries.Select(e => e.Kind).Distinct())
        {
            _packs.Items.Add(new ListBoxItem { Content = kind, IsEnabled = false, FontSize = 11, Padding = new Thickness(10, 8, 10, 2) });
            foreach (var pack in _entries.Where(e => e.Kind == kind).Select(e => e.Pack).Distinct())
            {
                var count = _entries.Count(e => e.Pack == pack);
                var item = new ListBoxItem { Content = $"{pack}  ({count})", Tag = pack };
                _packs.Items.Add(item);
                if (pack == currentPack)
                {
                    select = item;
                }
            }
        }

        if (_entries.All(e => e.Kind == "Built-in"))
        {
            _packs.Items.Add(new ListBoxItem { Content = "No sampled sounds installed yet. Use Sound library... to add some.", IsEnabled = false, Padding = new Thickness(10, 10, 10, 2) });
        }

        _packs.SelectedItem = select;
        _filling = false;
        FillSounds();
    }

    private void FillSounds()
    {
        _filling = true;
        _sounds.Items.Clear();
        var query = _search.Text.Trim();
        IEnumerable<Entry> shown;
        if (query.Length > 0)
        {
            shown = _entries.Where(e => e.Title.Contains(query, StringComparison.OrdinalIgnoreCase) || e.Pack.Contains(query, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            var pack = (_packs.SelectedItem as ListBoxItem)?.Tag as string;
            shown = _entries.Where(e => e.Pack == pack);
        }

        ListBoxItem? select = null;
        foreach (var entry in shown)
        {
            var item = new ListBoxItem { Content = query.Length > 0 ? $"{entry.Pack}: {entry.Title}" : entry.Title, Tag = entry };
            _sounds.Items.Add(item);
            if (string.Equals(entry.Name, _track.Preset, StringComparison.OrdinalIgnoreCase))
            {
                select = item;
            }
        }

        if (_sounds.Items.Count == 0)
        {
            _sounds.Items.Add(new ListBoxItem { Content = query.Length > 0 ? "No sounds match." : "Pick a pack on the left.", IsEnabled = false });
        }

        _sounds.SelectedItem = select ?? _sounds.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.IsEnabled);
        _filling = false;
        UpdateDescription();
    }

    private Entry? Selected => (_sounds.SelectedItem as ListBoxItem)?.Tag as Entry;

    private void UpdateDescription()
    {
        var entry = Selected;
        _description.Text = entry is null ? "" : $"{entry.Pack}: {entry.Title}. {entry.Description}";
        _use.IsEnabled = entry is not null;
        _preview.IsEnabled = entry is not null;
    }

    private async Task Preview()
    {
        if (Selected is not { } entry)
        {
            return;
        }

        _preview.Content = "Playing...";
        _preview.IsEnabled = false;
        try
        {
            await _main.PreviewSound(_track, entry.Name);
        }
        finally
        {
            _preview.Content = "Preview";
            _preview.IsEnabled = Selected is not null;
        }
    }

    private void Use()
    {
        if (Selected is not { } entry)
        {
            return;
        }

        _track.SetPreset(entry.Name);
        Close();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);
        if (e.Key == Key.Down && _search.IsKeyboardFocused && _sounds.Items.Count > 0)
        {
            _sounds.Focus();
            (_sounds.ItemContainerGenerator.ContainerFromIndex(_sounds.SelectedIndex < 0 ? 0 : _sounds.SelectedIndex) as ListBoxItem)?.Focus();
            e.Handled = true;
        }
    }
}
