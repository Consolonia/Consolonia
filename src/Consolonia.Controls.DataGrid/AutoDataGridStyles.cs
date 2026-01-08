using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Reactive;
using Avalonia.Threading;
using Consolonia.Controls;

namespace Consolonia.Themes;

/// <summary>
/// Automatically includes DataGrid resources for the current Consolonia theme.
/// - Always includes the shared DataGrid templates.
/// - Includes Modern-specific DataGridRow only when ThemeName is not TurboVision*.
/// </summary>
public class AutoDataGridStyles : Styles
{
    private readonly IServiceProvider _sp;

    public static readonly StyledProperty<string?> ThemeNameProperty =
        AvaloniaProperty.Register<AutoDataGridStyles, string?>(nameof(ThemeName), defaultValue: "Modern");

    public string? ThemeName
    {
        get => GetValue(ThemeNameProperty);
        set => SetValue(ThemeNameProperty, value);
    }

    // Always included
    private static readonly Uri DataGridUri = new(
        "avares://Consolonia.Controls.DataGrid/Themes/Templates/Controls/DataGrid.axaml");

    // Only for Modern family
    private static readonly Uri ModernRowUri = new(
        "avares://Consolonia.Controls.DataGrid/Themes/Modern/Controls/DataGridRow.axaml");

    private IDisposable _subscribedThemeName;
    private string _currentThemeName;

    public AutoDataGridStyles(IServiceProvider sp)
    {
        _sp = sp;
        /*var themeNameResource = ( DynamicResourceExtension)new DynamicResourceExtension("ThemeName").ProvideValue(sp);
        Resources.TryGetValue("ThemeName", our var value)*/
        
        //if (TryGetResource("ThemeName", theme: null, out object v) && v is string s)
            
        
        ((IResourceProvider)this).OwnerChanged += OnOwnerChanged;
    }

    private void OnOwnerChanged(object? sender, EventArgs e)
    {
        // when owner is changed we can not modify resources - it's being enumerated or something
        Dispatcher.UIThread.Post(_ =>
        {
            _subscribedThemeName?.Dispose();
            _subscribedThemeName = Owner.GetResourceObservable("ThemeName")
                .Subscribe(new AnonymousObserver<object>(OnThemeNameChanged));
        }, null, DispatcherPriority.MaxValue);
    }

    private void OnThemeNameChanged(object themeNameObject)
    {
        if(themeNameObject == null)
        {
            Resources = new ResourceDictionary();
            return;
        }
        
        Apply((string)themeNameObject);
    }

    private void Apply(string themeName)
    {
        if (themeName == _currentThemeName) 
            return;
        
        _currentThemeName = themeName;
        
        var resourceDictionary = new ResourceDictionary();
        resourceDictionary.MergedDictionaries.Add(new ResourceInclude(baseUri: null) { Source = DataGridUri });

        if (themeName?.StartsWith("TurboVision", StringComparison.OrdinalIgnoreCase) != true)
        {
            resourceDictionary.MergedDictionaries.Add(new ResourceInclude(baseUri: null) { Source = ModernRowUri });
        }

        Resources = resourceDictionary;
    }
}
