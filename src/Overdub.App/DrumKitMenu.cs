using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Overdub.Audio;

namespace Overdub.App;

public static class DrumKitMenu
{
    public static void Show(Button anchor, MainViewModel main, int current)
    {
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = PlacementMode.Bottom };
        for (var i = 0; i < DrumKit.StyleNames.Length; i++)
        {
            var style = i;
            var item = new MenuItem { Header = DrumKit.StyleNames[i], IsCheckable = true, IsChecked = i == current };
            item.Click += (_, _) => main.SetDrumKit(style);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }
}
