using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Resources;
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
    private ShellPresenter? _presenter;
    private Window? _window;
    private TaskbarIcon? _tray;
    private Icon? _icon;
    private MenuItem? _recentMenu;
    private ContextMenu? _menu;
    private Action? _onPresenterChanged;

    public TrayIconService(RecordViewModel record) => _record = record;

    /// <summary>Attaches to whichever window <see cref="ShellPresenter"/> currently shows for the app, and
    /// re-wires minimize-to-tray whenever it swaps (e.g. entering/leaving Compact layout).</summary>
    public void Attach(ShellPresenter presenter)
    {
        _presenter = presenter;
        RewireWindow(presenter.Current);
        // Kept in a field so Dispose() can detach it. As a bare lambda it was unremovable, and it captures
        // the presenter — so a disposed tray service stayed reachable through it. Harmless while both are
        // singletons, but it made Dispose() silently asymmetric.
        _onPresenterChanged = () => RewireWindow(presenter.Current);
        presenter.CurrentChanged += _onPresenterChanged;

        _icon = BuildIcon();

        var menu = new ContextMenu();
        menu.Items.Add(MenuItem(Resources.Strings.Tray_ShowRecMode, ShowWindow));
        menu.Items.Add(MenuItem(Resources.Strings.Tray_StartStopRecording, () => { if (_record.RecordCommand.CanExecute(null)) _record.RecordCommand.Execute(null); }));
        menu.Items.Add(MenuItem(Resources.Strings.Record_Screenshot, _record.TakeScreenshot));
        _recentMenu = new MenuItem { Header = Resources.Strings.Tray_Recent };
        menu.Items.Add(_recentMenu);
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItem(Resources.Strings.Tray_Quit, () => Application.Current.Shutdown()));
        menu.Opened += OnMenuOpened;
        _menu = menu;

        _tray = new TaskbarIcon
        {
            Icon = _icon,
            ToolTipText = Resources.Strings.App_Name,
            ContextMenu = menu,
        };
        _tray.TrayMouseDoubleClick += (_, _) => ShowWindow();
        _tray.ForceCreate();
    }

    private void RewireWindow(Window window)
    {
        if (_window is not null)
        {
            _window.StateChanged -= OnStateChanged;
        }

        _window = window;
        _window.StateChanged += OnStateChanged;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (_window is { WindowState: WindowState.Minimized })
        {
            _window.Hide(); // minimize-to-tray
        }
    }

    private void ShowWindow() => _presenter?.Show();

    private void OnMenuOpened(object sender, RoutedEventArgs e) => RefreshRecentMenu();

    /// <summary>Rebuilt every time the tray menu is about to open (not change-tracked) — cheap (at most 3
    /// items) and avoids wiring a live-update event for a list that only ever changes right when a recording
    /// starts, which is exactly when this menu isn't open anyway.</summary>
    private void RefreshRecentMenu()
    {
        if (_recentMenu is null)
        {
            return;
        }

        _recentMenu.Items.Clear();
        if (_record.RecentTargets.Count == 0)
        {
            _recentMenu.IsEnabled = false;
            return;
        }

        _recentMenu.IsEnabled = true;
        foreach (RecMode.Capture.CaptureTarget target in _record.RecentTargets)
        {
            _recentMenu.Items.Add(MenuItem(target.DisplayName, () => _record.QuickRecordFromTray(target)));
        }
    }

    private static MenuItem MenuItem(string header, Action action)
    {
        var item = new MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private static Icon BuildIcon()
    {
        // The real app icon (Assets/AppIcon.ico, embedded as a WPF resource) — loaded via GetResourceStream
        // rather than duplicating the asset as a loose file on disk.
        var uri = new Uri("pack://application:,,,/Assets/AppIcon.ico");
        StreamResourceInfo? info = Application.GetResourceStream(uri);
        if (info is not null)
        {
            using (info.Stream)
            {
                return new Icon(info.Stream, 32, 32);
            }
        }

        // Fallback placeholder, in case the packed resource can't be resolved for some reason.
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

        if (_presenter is not null && _onPresenterChanged is not null)
        {
            _presenter.CurrentChanged -= _onPresenterChanged;
            _onPresenterChanged = null;
        }

        if (_menu is not null)
        {
            _menu.Opened -= OnMenuOpened;
            _menu = null;
        }

        _tray?.Dispose();
        if (_icon is not null)
        {
            try
            {
                DestroyIcon(_icon.Handle);
                _icon.Dispose();
            }
            catch (ObjectDisposedException)
            {
                // H.NotifyIcon's TaskbarIcon.Dispose() (just above) already disposed the Icon assigned to
                // its Icon property — nothing left for us to clean up. Found via a real, previously-masked
                // shutdown-time exception (see CrashReporter's own defensive-catch fix, same review pass).
            }
        }
    }
}
