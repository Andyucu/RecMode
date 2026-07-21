using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using RecMode.App.ViewModels;
using RecMode.App.Views;
using RecMode.Capture;
using RecMode.Core.Infrastructure;

namespace RecMode.App.Services;

/// <summary>
/// Shows/hides the draw-on-screen annotation overlay based on <see cref="RecordViewModel.IsAnnotating"/>
/// (plan Phase 8). The overlay is sized to whatever is actually being recorded (<see
/// cref="RecordViewModel.ActiveCaptureTarget"/>) rather than always the primary monitor, and has the
/// <see cref="AnnotationHintWindow"/>'s and the floating <see cref="RecordingToolbar"/>'s bounds carved out of
/// its own hit-test region so ink can never cover — or steal clicks from — either bar. Exiting draw mode is
/// wired three ways for reliability regardless of which of the three floating windows has focus: the overlay's
/// own Esc handler, the hint bar's button, and a pair of global hotkeys (Esc, F12) that fire even if focus
/// landed on the toolbar or elsewhere. Torn down when annotation (or the recording) ends.
/// </summary>
public sealed class AnnotationService(RecordViewModel record, IOsCapabilities os, GlobalHotkeys hotkeys, RecordingToolbar toolbar) : IDisposable
{
    private AnnotationOverlay? _overlay;
    private AnnotationHintWindow? _hint;
    private int _escapeHotkeyId = -1;
    private int _exitHotkeyId = -1;
    private bool _hooked;

    public void Attach()
    {
        record.PropertyChanged += OnPropertyChanged;
        hotkeys.Pressed += OnHotkeyPressed;
        _hooked = true;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(RecordViewModel.IsAnnotating))
        {
            return;
        }

        if (record.IsAnnotating)
        {
            Show();
        }
        else
        {
            Hide();
        }
    }

    private void OnHotkeyPressed(int id)
    {
        if (id == _escapeHotkeyId || id == _exitHotkeyId)
        {
            record.StopAnnotating();
        }
    }

    private void Show()
    {
        if (_overlay is not null)
        {
            return;
        }

        _overlay = new AnnotationOverlay(record.StopAnnotating, record.ActiveCaptureTarget);
        _overlay.Show();

        // Shown after the overlay so it lands above the full-screen ink surface in the topmost z-order.
        _hint = new AnnotationHintWindow(record.StopAnnotating, os);
        _hint.Loaded += (_, _) => UpdateExclusions();
        _hint.Show();
        UpdateExclusions(); // the recording toolbar (if any) is already up and positioned

        // Global, so Esc/F12 exit draw mode no matter which of the three floating windows currently has
        // keyboard focus — the overlay's own Esc handler only fires when it itself is focused.
        _escapeHotkeyId = hotkeys.Register(0, VirtualKeys.Escape);
        _exitHotkeyId = hotkeys.Register(0, VirtualKeys.F12);
    }

    private void UpdateExclusions()
    {
        if (_overlay is null)
        {
            return;
        }

        var rects = new List<RegionRect>();
        if (_hint is not null && TryGetScreenRect(_hint, out RegionRect hintRect))
        {
            rects.Add(hintRect);
        }

        if (toolbar.Window is { } toolbarWindow && TryGetScreenRect(toolbarWindow, out RegionRect toolbarRect))
        {
            rects.Add(toolbarRect);
        }

        _overlay.ExcludeRects(rects);
    }

    private static bool TryGetScreenRect(Window window, out RegionRect rect)
    {
        IntPtr hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero && CaptureCapabilities.TryGetWindowScreenRect(hwnd, out rect))
        {
            return true;
        }

        rect = default;
        return false;
    }

    private void Hide()
    {
        _overlay?.Close();
        _overlay = null;
        _hint?.Close();
        _hint = null;

        if (_escapeHotkeyId != -1)
        {
            hotkeys.Unregister(_escapeHotkeyId);
            _escapeHotkeyId = -1;
        }

        if (_exitHotkeyId != -1)
        {
            hotkeys.Unregister(_exitHotkeyId);
            _exitHotkeyId = -1;
        }
    }

    public void Dispose()
    {
        record.PropertyChanged -= OnPropertyChanged;
        if (_hooked)
        {
            hotkeys.Pressed -= OnHotkeyPressed;
        }

        Hide();
    }
}
