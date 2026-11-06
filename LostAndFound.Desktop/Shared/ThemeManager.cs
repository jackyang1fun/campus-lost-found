using System.Windows;
using System.Windows.Media;

namespace LostAndFound.Desktop.Shared;

public static class ThemeManager
{
    public static void Apply()
    {
        if (Application.Current is null)
        {
            return;
        }

        var resources = Application.Current.Resources;
        var highContrast = SystemParameters.HighContrast;

        SetBrush(resources, "AppBackgroundBrush", highContrast
            ? SystemColors.WindowBrush
            : Brush("#F4F7F5"));
        SetBrush(resources, "SurfaceBrush", highContrast
            ? SystemColors.WindowBrush
            : Brush("#FFFFFF"));
        SetBrush(resources, "SurfaceMutedBrush", highContrast
            ? SystemColors.ControlBrush
            : Brush("#EDF5F1"));
        SetBrush(resources, "SidebarBrush", highContrast
            ? SystemColors.ControlBrush
            : Brush("#0F5548"));
        SetBrush(resources, "SidebarSelectedBrush", highContrast
            ? SystemColors.HighlightBrush
            : Brush("#1D7560"));
        SetBrush(resources, "SidebarTextBrush", highContrast
            ? SystemColors.ControlTextBrush
            : Brush("#F7FCFA"));
        SetBrush(resources, "AccentBrush", highContrast
            ? SystemColors.HighlightBrush
            : Brush("#147A5E"));
        SetBrush(resources, "AccentHoverBrush", highContrast
            ? SystemColors.HighlightBrush
            : Brush("#0F664F"));
        SetBrush(resources, "AccentTextBrush", highContrast
            ? SystemColors.HighlightTextBrush
            : Brush("#FFFFFF"));
        SetBrush(resources, "AccentSoftBrush", highContrast
            ? SystemColors.ControlBrush
            : Brush("#E4F2EB"));
        SetBrush(resources, "TextBrush", highContrast
            ? SystemColors.WindowTextBrush
            : Brush("#21322B"));
        SetBrush(resources, "MutedTextBrush", highContrast
            ? SystemColors.GrayTextBrush
            : Brush("#6A7B73"));
        SetBrush(resources, "BorderBrush", highContrast
            ? SystemColors.ControlDarkBrush
            : Brush("#DCE7E1"));
        SetBrush(resources, "DangerBrush", highContrast
            ? SystemColors.WindowTextBrush
            : Brush("#A43D3D"));
        SetBrush(resources, "WarningBrush", highContrast
            ? SystemColors.WindowTextBrush
            : Brush("#9A6414"));
        SetBrush(resources, "SuccessBrush", highContrast
            ? SystemColors.WindowTextBrush
            : Brush("#177A58"));
        SetBrush(resources, "FocusBrush", highContrast
            ? SystemColors.HighlightBrush
            : Brush("#2E8C73"));

        resources["HeroBackgroundBrush"] = highContrast
            ? SystemColors.ControlBrush
            : new LinearGradientBrush(
                Color.FromRgb(221, 241, 231),
                Color.FromRgb(245, 251, 247),
                25);
        resources["AuthBackgroundBrush"] = highContrast
            ? SystemColors.WindowBrush
            : new LinearGradientBrush(
                Color.FromRgb(218, 240, 229),
                Color.FromRgb(250, 252, 250),
                135);
    }

    private static SolidColorBrush Brush(string value)
    {
        var color = (Color)ColorConverter.ConvertFromString(value);
        return new SolidColorBrush(color);
    }

    private static void SetBrush(ResourceDictionary resources, string key, Brush value)
    {
        if (value.CanFreeze)
        {
            value.Freeze();
        }

        resources[key] = value;
    }
}
