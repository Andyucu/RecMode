using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using RecMode.App.ViewModels;

namespace RecMode.App.Services;

/// <summary>
/// Tray icon + menu and minimize-to-tray (plan Phase 5). Runs headless from the tray; the schedule/hotkeys
/// keep working with the main window hidden.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr handle);

    private readonly RecordViewModel _record;
    private TaskbarIcon? _tray;
    private Window? _window;
    private Icon? _icon;

    public TrayIconService(RecordViewModel record) => _record = record;

    public void Attach(Window window)
    {
        _window = window;
        _icon = BuildIcon();

        var menu = new ContextMenu();
        menu.Items.Add(MenuItem("Show RecMode", ShowWindow));
        menu.Items.Add(MenuItem("Start / stop recording", () => { if (_record.RecordCommand.CanExecute(null)) _record.RecordCommand.Execute(null); }));
        menu.Items.Add(MenuItem("Screenshot", _record.TakeScreenshot));
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem("Quit", () => Application.Current.Shutdown()));

        _tray = new TaskbarIcon
        {
            Icon = _icon,
            ToolTipText = "RecMode",
            ContextMenu = menu,
        };
        _tray.TrayMouseDoubleClick += (_, _) => ShowWindow();
        _tray.ForceCreate();

        window.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_window is { WindowState: WindowState.Minimized })
        {
            _window.Hide(); // minimize-to-tray
        }
    }

    private void ShowWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static Icon BuildIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var bg = new SolidBrush(ColorTranslator.FromHtml("#0078D4"));
            g.FillEllipse(bg, 3, 3, 26, 26);
            using var dot = new SolidBrush(Color.White);
            g.FillEllipse(dot, 12, 12, 8, 8);
        }

        IntPtr handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    public void Dispose()
    {
        if (_window is not null)
        {
            _window.StateChanged -= OnStateChanged;
        }

        _tray?.Dispose();
        if (_icon is not null)
        {
            DestroyIcon(_icon.Handle);
            _icon.Dispose();
        }
    }
}
