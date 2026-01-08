using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.MarkupExtensions;

namespace Consolonia.Themes;

/// <summary>
/// Automatically includes Managed Windows resources for the current Consolonia theme.
/// - Modern → Base.axaml
/// - TurboVision* → TurboVision.axaml
/// </summary>
public class AutoManagedWindowTheme : Styles
{
    public static readonly StyledProperty<string?> ThemeNameProperty =
        AvaloniaProperty.Register<AutoManagedWindowTheme, string?>(nameof(ThemeName), defaultValue: "Modern");

    public string? ThemeName
    {
        get => GetValue(ThemeNameProperty);
        set => SetValue(ThemeNameProperty, value);
    }
    private static readonly Uri ModernBaseUri = new(
        "avares://Consolonia.ManagedWindows/Themes/Base.axaml");

    private static readonly Uri TurboVisionUri = new(
        "avares://Consolonia.ManagedWindows/Themes/TurboVision/TurboVision.axaml");

    public AutoManagedWindowTheme()
        : this(null)
    { }

    public AutoManagedWindowTheme(IServiceProvider? sp)
    {
        // Bind ThemeName to a DynamicResource so we only react to its changes
        var provided = new DynamicResourceExtension("ThemeName").ProvideValue(sp);
        if (provided is IBinding binding)
        {
            this.Bind(ThemeNameProperty, binding);
        }
        else
        {
            if (TryGetResource("ThemeName", theme: null, out var v) && v is string s)
                ThemeName = s;
        }

        // Update styles on ThemeName changes without Reactive extensions
        this.PropertyChanged += (sender, args) =>
        {
            if (args.Property == ThemeNameProperty)
            {
                Apply(ThemeName);
            }
        };

        // Initial compose
        Apply(ThemeName);
    }

    private void Apply(string? themeName)
    {
        Clear();
        var isTurbo = themeName?.StartsWith("TurboVision", StringComparison.OrdinalIgnoreCase) == true;
        Add(new StyleInclude(baseUri: null) { Source = isTurbo ? TurboVisionUri : ModernBaseUri });
    }
}
