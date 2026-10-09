using System.Windows;
using System.Windows.Media;

namespace Overdub.App;

public sealed class SoundLibraryWindow : Window
{
    public SoundLibraryWindow(MainViewModel main, Window owner)
    {
        Owner = owner;
        Icon = owner.Icon;
        Title = "Sounds";
        Width = 760;
        Height = 700;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = (Brush)FindResource("Bg");
        Foreground = (Brush)FindResource("Text");
        FontFamily = new FontFamily("Segoe UI");
        FontSize = 13;
        var view = new SoundLibraryView();
        view.Attach(main);
        Content = view;
        Closed += (_, _) => view.CancelDownloads();
    }
}
