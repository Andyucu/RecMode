using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using RecMode.Core.Settings;

namespace RecMode.App.Themes;

/// <summary>
/// Applies theme (Light/Dark/System) and the 5 accent presets at runtime by swapping the palette
/// ResourceDictionary and overriding the accent brushes (plan §2). System theme follows the Windows
/// app-theme registry value.
/// </summary>
public sealed class ThemeManager
{
    private static readonly Uri LightPalette = new("Themes/Palette.Light.xaml", UriKind.Relative);
    private static readonly Uri DarkPalette = new("Themes/Palette.Dark.xaml", UriKind.Relative);

    private ResourceDictionary? _currentPalette;

    public bool IsDark { get; private set; }

    /// <summary>Raised after the theme (light/dark) changes, so the shell can re-apply the DWM title-bar mode.</summary>
    public event Action? Changed;

    public void Apply(AppTheme theme, AccentColor accent)
    {
        ApplyTheme(theme);
        ApplyAccent(accent);
    }

    public void ApplyTheme(AppTheme theme)
    {
        bool dark = theme switch
        {
            AppTheme.Dark => true,
            AppTheme.Light => false,
            _ => IsSystemDark(),
        };
        IsDark = dark;

        var palette = new ResourceDictionary { Source = dark ? DarkPalette : LightPalette };
        var merged = Application.Current.Resources.MergedDictionaries;

        if (_currentPalette is not null)
        {
            merged.Remove(_currentPalette);
        }

        // Palette goes first so Controls.xaml (added in App.xaml) can override where needed.
        merged.Insert(0, palette);
        _currentPalette = palette;
        Changed?.Invoke();
    }

    public void ApplyAccent(AccentColor accent)
    {
        Color baseColor = accent switch
        {
            AccentColor.Blue => Hex("#0078D4"),
            AccentColor.Red => Hex("#D13438"),
            AccentColor.Purple => Hex("#8B6CCB"),
            AccentColor.Teal => Hex("#0F7B7B"),
            AccentColor.Orange => Hex("#CA5010"),
            _ => Hex("#0078D4"),
        };

        ResourceDictionary app = Application.Current.Resources;
        app["AccentColor"] = baseColor;
        app["AccentBrush"] = new SolidColorBrush(baseColor);
        app["AccentHoverBrush"] = new SolidColorBrush(Mix(baseColor, Colors.White, 0.12));
        app["AccentPressedBrush"] = new SolidColorBrush(Mix(baseColor, Colors.Black, 0.10));
    }

    private static bool IsSystemDark()
    {
        try
        {
            object? value = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1);
            return value is int i && i == 0;
        }
        catch (Exception)
        {
            return true; // default to dark
        }
    }

    private static Color Hex(string hex)
    {
        var c = (Color)ColorConverter.ConvertFromString(hex)!;
        return c;
    }

    private static Color Mix(Color a, Color b, double t) => Color.FromRgb(
        (byte)(a.R + (b.R - a.R) * t),
        (byte)(a.G + (b.G - a.G) * t),
        (byte)(a.B + (b.B - a.B) * t));
}
