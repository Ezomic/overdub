using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Overdub.App;

public sealed class ShortcutsWindow : Window
{
    private static readonly (string Keys, string Action)[] Shortcuts =
    [
        ("Space", "Play and pause"),
        ("R", "Record"),
        ("Esc", "Stop"),
        ("Home", "Go to the start"),
        ("Left, Right", "Move the playhead one beat (hold Ctrl for one bar)"),
        ("L", "Loop on or off"),
        ("C", "Metronome click on or off"),
        ("S", "Split the selected clip at the playhead"),
        ("Delete", "Delete the selected clip"),
        ("Ctrl+D", "Duplicate the selected clip"),
        ("Ctrl+Z", "Undo"),
        ("Ctrl+Y or Ctrl+Shift+Z", "Redo"),
        ("Ctrl+S", "Save the project"),
        ("Ctrl+1 to Ctrl+4", "Switch section: Record and mix, Build a song, Learn, Sounds"),
        ("F1", "Show this list"),
    ];

    public ShortcutsWindow(Window owner)
    {
        Owner = owner;
        Icon = owner.Icon;
        Title = "Keyboard shortcuts";
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var grid = new Grid { Margin = new Thickness(22) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var i = 0; i < Shortcuts.Length; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var key = new TextBlock { Text = Shortcuts[i].Keys, FontFamily = new FontFamily("Consolas"), Foreground = (Brush)FindResource("Text"), Margin = new Thickness(0, 4, 12, 4) };
            var action = new TextBlock { Text = Shortcuts[i].Action, Foreground = (Brush)FindResource("TextDim"), Margin = new Thickness(0, 4, 0, 4) };
            Grid.SetRow(key, i);
            Grid.SetRow(action, i);
            Grid.SetColumn(action, 1);
            grid.Children.Add(key);
            grid.Children.Add(action);
        }

        Content = grid;
        KeyDown += (_, e) =>
        {
            if (e.Key is System.Windows.Input.Key.Escape or System.Windows.Input.Key.F1)
            {
                Close();
            }
        };
    }
}
