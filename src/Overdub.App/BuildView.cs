using System.Collections.Specialized;
using System.Windows.Controls.Primitives;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Overdub.Audio;

namespace Overdub.App;

public sealed class BuildView : UserControl
{
    private readonly StackPanel _page = new() { Margin = new Thickness(20, 16, 20, 20) };
    private MainViewModel? _main;

    public BuildView()
    {
        Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _page };
        IsVisibleChanged += (_, e) =>
        {
            if (e.NewValue is true)
            {
                Refresh();
            }
        };
    }

    public void Attach(MainViewModel main)
    {
        _main = main;
        main.Tracks.CollectionChanged += OnTracksChanged;
        main.EditHistoryChanged += () =>
        {
            if (IsVisible)
            {
                Refresh();
            }
        };
        Refresh();
    }

    private void OnTracksChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (IsVisible)
        {
            Refresh();
        }
    }

    private Window? Owner => Window.GetWindow(this);

    private void Refresh()
    {
        if (_main is null)
        {
            return;
        }

        _page.Children.Clear();
        _page.Children.Add(Ui.Title("Build a song"));
        _page.Children.Add(Ui.Sub("Pick a starting point, then shape each player in your band."));
        _page.Children.Add(Ui.Label("Start from a template"));
        _page.Children.Add(Templates());
        _page.Children.Add(new Border { Height = 14 });
        _page.Children.Add(Ui.Label("Your song"));
        _page.Children.Add(SongBox());
        _page.Children.Add(Ui.Label("Your band"));
        var players = _main.Tracks
            .Where(t => t.IsDrums || t.IsMachine || t.IsKeys)
            .OrderBy(t => t.IsDrums ? 0 : t.Model.Machine switch { MachineRole.Guitar => 1, MachineRole.Bass => 2, MachineRole.Lead => 3, _ => 4 })
            .ToList();
        foreach (var track in players)
        {
            _page.Children.Add(Row(track));
        }

        if (players.Count == 0)
        {
            _page.Children.Add(Ui.Sub("Nobody is in your band yet. Pick a template above, or add a player below.", 8));
        }

        var add = Ui.Secondary("+ Add a player", "Add a drum machine, a guitar, bass or lead machine, or a keyboard", 130);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Margin = new Thickness(0, 4, 0, 0);
        add.Click += (_, _) => ShowAddMenu(add);
        _page.Children.Add(add);
    }

    private UIElement Templates()
    {
        var grid = new UniformGrid { Columns = 4, Margin = new Thickness(0) };
        var all = SongTemplates.All;
        foreach (var template in all.Take(3))
        {
            var chosen = template;
            var text = new StackPanel();
            text.Children.Add(new TextBlock { Text = chosen.Name, FontSize = 14, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock { Text = $"{chosen.TotalBars} bars, {chosen.Bpm:0} BPM", FontSize = 12.5, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 3, 0, 0) });
            var card = Ui.ClickCard(text, () => _main!.ApplyTemplate(chosen), chosen.Description);
            card.Margin = new Thickness(0, 0, 8, 0);
            card.Padding = new Thickness(11, 9, 11, 9);
            grid.Children.Add(card);
        }

        var more = new StackPanel();
        more.Children.Add(new TextBlock { Text = $"See all {all.Count}...", FontSize = 14, FontWeight = FontWeights.SemiBold });
        more.Children.Add(new TextBlock { Text = "funk, jazz, metal, house", FontSize = 12.5, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 3, 0, 0) });
        Border? seeAll = null;
        seeAll = Ui.ClickCard(more, () => ShowAllTemplates(seeAll!), "Every song template, with a short description of each");
        seeAll.Padding = new Thickness(11, 9, 11, 9);
        grid.Children.Add(seeAll);
        return grid;
    }

    private void ShowAllTemplates(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var template in SongTemplates.All)
        {
            var chosen = template;
            var item = new MenuItem { Header = $"{chosen.Name}   ({chosen.TotalBars} bars, {chosen.Bpm:0} BPM)", ToolTip = chosen.Description };
            item.Click += (_, _) => _main!.ApplyTemplate(chosen);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private UIElement Row(TrackViewModel track)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var dot = new Border { Width = 10, Height = 34, CornerRadius = new CornerRadius(3), Background = track.Color, HorizontalAlignment = HorizontalAlignment.Left };
        var name = new TextBlock { Text = track.Name, FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        var sound = new TextBlock { Text = track.IsDrums ? track.KitTitle : track.PresetTitle, FontSize = 12.5, Foreground = Ui.Res("TextDim"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(8, 0, 8, 0), ToolTip = track.IsDrums ? track.KitTitle : track.PresetTitle };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        if (track.IsDrums)
        {
            var kit = Ui.Secondary("Change kit", "Pick a built-in kit or an installed sampled kit");
            kit.Click += (_, _) => DrumKitMenu.Show(kit, _main!, track.Model.DrumKitStyle, track.Model.DrumKitSample);
            buttons.Children.Add(kit);
            var machine = Ui.Secondary("Drum machine", "Edit drum patterns and place them on the timeline");
            machine.Click += (_, _) => new DrumWindow(_main!, Owner!).Show();
            buttons.Children.Add(machine);
        }
        else
        {
            var change = Ui.Secondary("Change sound", "Choose the instrument sound, with a preview");
            change.Click += (_, _) => new SoundPickerWindow(_main!, Owner!, track).ShowDialog();
            buttons.Children.Add(change);
            if (track.Model.Machine is MachineRole.Guitar or MachineRole.Bass)
            {
                var chords = Ui.Secondary("Chords", "Edit chord patterns and place them on the timeline");
                chords.Click += (_, _) => new ChordWindow(_main!, Owner!, track.Model).Show();
                buttons.Children.Add(chords);
            }

            if (track.Model.Machine == MachineRole.Bass)
            {
                var coach = Ui.Secondary("Coach", "Bass lines over your chords, as tab, to learn and play");
                coach.Click += (_, _) => new BassCoachWindow(_main!, Owner!, track.Model).Show();
                buttons.Children.Add(coach);
            }

            if (track.IsKeys)
            {
                var play = Ui.Secondary("Play the chords", "Fill the keys with your song's chords as a pad, arpeggio or stabs", 120);
                play.Click += (_, _) => ShowKeysStyles(play, track);
                buttons.Children.Add(play);
                var edit = Ui.Secondary("Edit notes", "Open the piano roll for the keys", 90);
                edit.Click += (_, _) => EditKeys(track);
                buttons.Children.Add(edit);
            }

            if (track.Model.Machine == MachineRole.Lead)
            {
                var melody = Ui.Secondary("Melody", "Generate a single-note melody in your key");
                melody.Click += (_, _) => new MelodyWindow(_main!, Owner!, track.Model).Show();
                buttons.Children.Add(melody);
            }
        }

        var remove = Ui.Secondary("Remove", "Remove this player and its blocks from the song (Undo brings it back)", 90);
        remove.Margin = new Thickness(0);
        remove.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(remove, 4);
        remove.Click += (_, _) =>
        {
            if (remove.Tag is true)
            {
                _main!.RemovePlayer(track);
                return;
            }

            remove.Tag = true;
            remove.Content = "Remove?";
        };
        remove.MouseLeave += (_, _) =>
        {
            remove.Tag = null;
            remove.Content = "Remove";
        };
        Grid.SetColumn(name, 1);
        Grid.SetColumn(sound, 2);
        Grid.SetColumn(buttons, 3);
        grid.Children.Add(dot);
        grid.Children.Add(name);
        grid.Children.Add(sound);
        grid.Children.Add(buttons);
        grid.Children.Add(remove);
        return Ui.Card(grid);
    }

    private UIElement SongBox()
    {
        var key = _main!.DetectSongKeyQuick();
        var stack = new StackPanel();
        var top = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
        var keyBox = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        keyBox.Children.Add(new TextBlock { Text = "Key", FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 3) });
        var keyButton = Ui.Secondary(key is null ? "Not known yet" : $"{key.Name} ▾", "Move every player to another key at once", 150);
        keyButton.Margin = new Thickness(0);
        keyButton.IsEnabled = key is not null;
        keyButton.Click += (_, _) => ShowKeyMenu(keyButton);
        keyBox.Children.Add(keyButton);
        top.Children.Add(keyBox);

        var tempoBox = new StackPanel { Margin = new Thickness(0, 0, 18, 0) };
        tempoBox.Children.Add(new TextBlock { Text = "Tempo (BPM)", FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 3) });
        var tempo = new TextBox { Text = _main.Bpm.ToString("0"), Width = 70, Height = 26, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center, ToolTip = "Beats per minute", Background = Ui.Res("Well"), Foreground = Ui.Res("Text"), BorderBrush = Ui.Res("Border"), CaretBrush = Ui.Res("Text") };
        void Commit()
        {
            if (double.TryParse(tempo.Text, out var bpm))
            {
                _main.Bpm = bpm;
            }

            tempo.Text = _main.Bpm.ToString("0");
        }

        tempo.LostFocus += (_, _) => Commit();
        tempo.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
            {
                Commit();
            }
        };
        tempoBox.Children.Add(tempo);
        top.Children.Add(tempoBox);
        top.Children.Add(new TextBlock { Text = "Changing the key moves every player together: chords, bass lines, melody and keys.", FontSize = 12.5, Foreground = Ui.Res("TextDim"), TextWrapping = TextWrapping.Wrap, MaxWidth = 340, VerticalAlignment = VerticalAlignment.Bottom });
        stack.Children.Add(top);

        var chords = _main.SongProgression();
        if (key is not null && chords.Count > 0)
        {
            stack.Children.Add(new TextBlock { Text = "Chords (every player follows these)", FontSize = 12, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 5) });
            var row = new WrapPanel();
            var flats = NoteSpelling.Mode switch { 1 => false, 2 => true, _ => NoteSpelling.KeyUsesFlats(key.Root, key.Minor) };
            foreach (var chord in chords)
            {
                var tile = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                tile.Children.Add(new TextBlock { Text = Theory.Numeral(chord, key.Root, key.Minor), FontFamily = new FontFamily("Georgia"), FontSize = 19, Foreground = Ui.AccentBrush, HorizontalAlignment = HorizontalAlignment.Center });
                tile.Children.Add(new TextBlock { Text = NoteSpelling.NameFor(chord.Root, flats) + chord.QualitySuffix, FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center });
                row.Children.Add(new Border { Background = Ui.Res("Well"), BorderBrush = Ui.Res("Border"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 6, 16, 6), Margin = new Thickness(0, 0, 8, 6), Child = tile });
            }

            var edit = Ui.Secondary("Edit chords", "Open the chord editor for the guitar machine", 100);
            edit.Margin = new Thickness(0, 0, 0, 6);
            edit.Click += (_, _) => EditChords();
            row.Children.Add(edit);
            stack.Children.Add(row);
        }
        else
        {
            var edit = Ui.Secondary("Edit chords", "Open the chord editor for the guitar machine", 100);
            edit.HorizontalAlignment = HorizontalAlignment.Left;
            edit.Click += (_, _) => EditChords();
            stack.Children.Add(new TextBlock { Text = "No chords yet. Pick a template above, or add a guitar player and edit its chords.", FontSize = 12.5, Foreground = Ui.Res("TextDim"), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
            stack.Children.Add(edit);
        }

        var card = Ui.Card(stack, 14);
        card.CornerRadius = new CornerRadius(10);
        card.Padding = new Thickness(16, 12, 16, 8);
        return card;
    }

    private void ShowKeyMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        var minor = _main!.DetectSongKeyQuick()?.Minor == true;
        for (var root = 0; root < 12; root++)
        {
            var target = root;
            var item = new MenuItem { Header = $"{NoteSpelling.Name(target)} {(minor ? "minor" : "major")}" };
            item.Click += (_, _) => _main.MoveToKey(target);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void EditChords()
    {
        var track = _main!.Tracks.FirstOrDefault(t => t.Model.Machine == MachineRole.Guitar) ?? _main.Tracks.FirstOrDefault(t => t.Model.Machine == MachineRole.Bass);
        if (track is null)
        {
            _main.ShowMessage("Add a guitar player first (Add a player), then edit its chords.");
            return;
        }

        new ChordWindow(_main, Owner!, track.Model).Show();
    }

    private void ShowKeysStyles(FrameworkElement anchor, TrackViewModel track)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        foreach (var (style, name) in Session.KeysStyles)
        {
            var chosen = style;
            var item = new MenuItem { Header = name, ToolTip = ChordPattern.StyleDescription(chosen) };
            item.Click += (_, _) => _main!.PlayChordsOnKeys(track, chosen);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void EditKeys(TrackViewModel track)
    {
        if (track.Model.MidiClips.FirstOrDefault() is not { } clip)
        {
            _main!.ShowMessage("The keys have no notes yet. Use Play the chords, or record on your keyboard first.");
            return;
        }

        new PianoRollWindow(_main!, track, clip) { Owner = Owner }.Show();
    }

    private void ShowAddMenu(FrameworkElement anchor)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var drums = new MenuItem { Header = "Drums", IsEnabled = _main!.DrumTrack is null, ToolTip = "A step-sequencer drum machine" };
        drums.Click += (_, _) => _main!.AddDrumTrack();
        menu.Items.Add(drums);
        foreach (var role in Enum.GetValues<MachineRole>())
        {
            var chosen = role;
            var item = new MenuItem { Header = role switch { MachineRole.Lead => "Lead guitar (melody)", MachineRole.Guitar => "Guitar machine (plays chords)", _ => "Bass machine (plays bass lines)" }, IsEnabled = !_main.MachineLimitReached };
            item.Click += (_, _) => _main!.AddMachineTrack(chosen);
            menu.Items.Add(item);
        }

        var keys = new MenuItem { Header = "Keys (play it yourself on a MIDI keyboard)" };
        keys.Click += (_, _) => _main!.AddMidiTrack();
        menu.Items.Add(keys);
        menu.IsOpen = true;
    }
}
