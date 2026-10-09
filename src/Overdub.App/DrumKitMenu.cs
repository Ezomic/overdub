using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Overdub.Audio;

namespace Overdub.App;

public static class DrumKitMenu
{
    public static void Show(Button anchor, MainViewModel main, int current, string? currentSample)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        for (var i = 0; i < DrumKit.StyleNames.Length; i++)
        {
            var style = i;
            var item = new MenuItem { Header = DrumKit.StyleNames[i], IsCheckable = true, IsChecked = currentSample is null && i == current };
            item.Click += (_, _) => main.SetDrumKit(style);
            menu.Items.Add(item);
        }

        var kits = SoundPrograms.Discover().Where(p => p.IsDrumKit).ToList();
        if (kits.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var kit in kits)
            {
                var chosen = kit;
                var item = new MenuItem { Header = $"{chosen.Pack}: {chosen.Title}", IsCheckable = true, IsChecked = string.Equals(chosen.Name, currentSample, StringComparison.OrdinalIgnoreCase) };
                item.Click += (_, _) => main.SetDrumKit(current, chosen.Name);
                menu.Items.Add(item);
            }
        }

        menu.Items.Add(new Separator());
        var more = new MenuItem { Header = "Get sampled kits in the sound library..." };
        more.Click += (_, _) => new SoundLibraryWindow(main, System.Windows.Window.GetWindow(anchor)!).Show();
        menu.Items.Add(more);
        menu.IsOpen = true;
    }
}
