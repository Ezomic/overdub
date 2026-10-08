using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Overdub.Audio.Vst3;

namespace Overdub.App;

public sealed class PluginPickerWindow : Window
{
    private readonly ListBox _list = new() { Margin = new Thickness(0, 0, 0, 12), DisplayMemberPath = nameof(Row.Label) };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 0, 0, 8), Text = "Scanning for plugins..." };

    public PluginPickerWindow(Window owner)
    {
        Owner = owner;
        Title = "Choose a VST3 effect";
        Width = 460;
        Height = 480;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        _status.Foreground = (Brush)FindResource("TextDim");
        var choose = new Button { Content = "Use this plugin", Padding = new Thickness(14, 4, 14, 4), HorizontalAlignment = HorizontalAlignment.Right, IsEnabled = false, IsDefault = true };
        choose.Click += (_, _) => Accept();
        _list.SelectionChanged += (_, _) => choose.IsEnabled = _list.SelectedItem is not null;
        _list.MouseDoubleClick += (_, _) => Accept();
        var panel = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(_status, Dock.Top);
        DockPanel.SetDock(choose, Dock.Bottom);
        panel.Children.Add(_status);
        panel.Children.Add(choose);
        panel.Children.Add(_list);
        Content = panel;
    }

    public Vst3PluginInfo? Selected { get; private set; }

    public bool PickFrom(Task<IReadOnlyList<Vst3PluginInfo>> scan)
    {
        _ = scan.ContinueWith(t => Dispatcher.Invoke(() => SetPlugins(t.IsCompletedSuccessfully ? t.Result : [])));
        return ShowDialog() == true;
    }

    private void SetPlugins(IReadOnlyList<Vst3PluginInfo> plugins)
    {
        _list.ItemsSource = plugins.Select(p => new Row(p, $"{p.Name}   ({p.Vendor})")).ToList();
        _status.Text = plugins.Count == 0 ? "No VST3 effects found in the standard folders." : $"{plugins.Count} effects found";
    }

    private void Accept()
    {
        if (_list.SelectedItem is Row row)
        {
            Selected = row.Info;
            DialogResult = true;
        }
    }

    private sealed record Row(Vst3PluginInfo Info, string Label);
}
