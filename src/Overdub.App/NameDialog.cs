using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Overdub.App;

public sealed class NameDialog : Window
{
    private readonly TextBox _box = new() { Margin = new Thickness(0, 8, 0, 12), Padding = new Thickness(6, 4, 6, 4), MinWidth = 280 };

    public NameDialog(Window owner, string title, string prompt, string initial)
    {
        Owner = owner;
        Title = title;
        Icon = owner.Icon;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        _box.Text = initial;
        var ok = new Button { Content = "Save", IsDefault = true, Padding = new Thickness(16, 4, 16, 4), Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => DialogResult = !string.IsNullOrWhiteSpace(_box.Text);
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 4, 16, 4) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var panel = new StackPanel { Margin = new Thickness(18) };
        panel.Children.Add(new TextBlock { Text = prompt });
        panel.Children.Add(_box);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) =>
        {
            _box.Focus();
            _box.SelectAll();
        };
    }

    public string Value => _box.Text.Trim();

    public static string? Ask(Window owner, string title, string prompt, string initial)
    {
        var dialog = new NameDialog(owner, title, prompt, initial);
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }
}
