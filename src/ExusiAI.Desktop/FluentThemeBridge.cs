using System.Windows;
using System.Windows.Media;
using ExusiAI.Theme;
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;

namespace ExusiAI.Desktop;

/// <summary>Keep the existing host palettes and WPF UI controls on the same theme.</summary>
internal static class FluentThemeBridge
{
    internal static void Apply(ThemePalette palette)
    {
        var resources = Application.Current.Resources;
        var background = Color(palette.Background);
        var mode = (0.2126 * background.R + 0.7152 * background.G + 0.0722 * background.B) < 128
            ? ApplicationTheme.Dark : ApplicationTheme.Light;

        // Swap only control resources. WindowBackdropService continues to own DWM.
        var dictionary = resources.MergedDictionaries.OfType<ThemesDictionary>().First();
        dictionary.Theme = mode;
        ApplicationAccentColorManager.Apply(Color(palette.Accent), mode);

        Set(palette.TextPrimary, "TextFillColorPrimaryBrush", "ButtonForeground", "ButtonForegroundPointerOver",
            "ComboBoxForeground", "TextControlForeground", "ListBoxItemForeground", "ListBoxItemSelectedForegroundThemeBrush");
        Set(palette.TextSecondary, "TextFillColorSecondaryBrush", "ButtonForegroundPressed", "TextControlPlaceholderForeground");
        Set(palette.Surface, "ButtonBackground", "ComboBoxBackground", "ComboBoxBackgroundFocused",
            "ComboBoxDropDownBackground", "TextControlBackground");
        Set(palette.SurfaceAlternative, "ButtonBackgroundPointerOver", "ButtonBackgroundPressed",
            "ComboBoxBackgroundPointerOver", "TextControlBackgroundPointerOver", "MenuBarItemBackgroundSelected");
        Set(palette.AccentSoft, "ListBoxItemSelectedBackgroundThemeBrush");
        Set(palette.Border, "ComboBoxDropDownBorderBrush", "ControlStrokeColorDefaultBrush", "CardStrokeColorDefaultBrush");
        var foreground = App.GetContrastingForeground(palette.Accent);
        Set(foreground, "AccentButtonForeground", "AccentButtonForegroundPointerOver", "AccentButtonForegroundPressed",
            "TextOnAccentFillColorPrimaryBrush");

        void Set(string color, params string[] keys)
        {
            var brush = new SolidColorBrush(Color(color));
            brush.Freeze();
            foreach (var key in keys) resources[key] = brush;
        }
    }

    private static Color Color(string value) => (Color)ColorConverter.ConvertFromString(value);
}
