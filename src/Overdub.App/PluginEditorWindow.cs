using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Overdub.Audio.Vst3;

namespace Overdub.App;

public sealed class PluginEditorWindow : Window
{
    private static readonly Dictionary<PluginSlot, PluginEditorWindow> Open = [];

    private readonly DispatcherTimer _timer;
    private readonly PluginHost _host;

    private PluginEditorWindow(Window owner, string title, PluginSlot slot, Vst3Editor editor, Action changed)
    {
        Title = title;
        Icon = owner.Icon;
        SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.CanMinimize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _host = new PluginHost(editor);
        Content = _host;
        editor.SizeRequested += (w, h) => Dispatcher.Invoke(() => _host.SetSize(w, h));
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick += (_, _) =>
        {
            if (slot.Capture())
            {
                changed();
            }
        };
        _timer.Start();
        Closed += (_, _) =>
        {
            _timer.Stop();
            Open.Remove(slot);
            if (slot.Capture())
            {
                changed();
            }
        };
    }

    public static bool Show(Window owner, string title, PluginSlot slot, Action changed)
    {
        if (Open.TryGetValue(slot, out var existing))
        {
            existing.Activate();
            return true;
        }

        var editor = slot.Instance?.CreateEditor();
        if (editor is null)
        {
            return false;
        }

        var window = new PluginEditorWindow(owner, title, slot, editor, changed) { Owner = owner };
        Open[slot] = window;
        window.Show();
        return true;
    }

    private sealed class PluginHost(Vst3Editor editor) : HwndHost
    {
        private const int WsChild = 0x40000000;
        private const int WsVisible = 0x10000000;
        private const int WsClipChildren = 0x02000000;
        private const int WsClipSiblings = 0x04000000;
        private IntPtr _hwnd;

        public void SetSize(int width, int height)
        {
            Width = width;
            Height = height;
            if (_hwnd != IntPtr.Zero)
            {
                SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, width, height, 0x16);
            }
        }

        protected override HandleRef BuildWindowCore(HandleRef hwndParent)
        {
            var (width, height) = editor.Size;
            Width = width;
            Height = height;
            _hwnd = CreateWindowEx(0, "static", "", WsChild | WsVisible | WsClipChildren | WsClipSiblings, 0, 0, width, height, hwndParent.Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            editor.Attach(_hwnd);
            return new HandleRef(this, _hwnd);
        }

        protected override void DestroyWindowCore(HandleRef hwnd)
        {
            editor.Dispose();
            DestroyWindow(hwnd.Handle);
            _hwnd = IntPtr.Zero;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateWindowEx(int exStyle, string className, string windowName, int style, int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

        [DllImport("user32.dll")]
        private static extern bool DestroyWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
