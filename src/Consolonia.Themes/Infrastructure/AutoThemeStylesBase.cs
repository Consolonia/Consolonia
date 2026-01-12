using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace Consolonia.Themes.Infrastructure;

/// <summary>
/// Base class for auto-switching Styles that react to a global resource named
/// "ConsoloniaThemeFamily" (e.g., "Modern", "TurboVision").
/// </summary>
public abstract class AutoThemeStylesBase : Styles
{
    public const string ModernThemeKey = "Modern";
    public const string TurboVisionThemeKey = "TurboVision";
    
    private string _currentFamily;
    private IDisposable _consoloniaThemeFamilySybscription; //todo: low: where to dispose?

    protected AutoThemeStylesBase()
    {
        _consoloniaThemeFamilySybscription = this.GetResourceObservable("ConsoloniaThemeFamily").Subscribe(o =>
        {
            ApplyFromTheme((string)o);
        });
    }

    private void ApplyFromTheme(string value)
    {
        Apply(value);
    }
    
    

    private void Apply(string family)
    {
        if (string.Equals(_currentFamily, family, StringComparison.Ordinal))
            return;

        _currentFamily = family;
        Clear();

        if (family == null)
            return;

        ComposeForFamily(family);
    }

    /// <summary>
    /// Compose this Styles instance for the specified theme family.
    /// Implementations should call  <see cref="IncludeStyle"/>.
    /// </summary>
    protected abstract void ComposeForFamily(string family);
    
    /*protected void MergeResource(Uri uri)
    {
        if (Resources is not ResourceDictionary rd)
        {
            rd = new ResourceDictionary();
            Resources = rd;
        }

        rd.MergedDictionaries.Add(new ResourceInclude(baseUri: null) { Source = uri });
    }*/

    /// <summary>
    /// Helper to include a Styles-root XAML file.
    /// </summary>
    protected void IncludeStyle(Uri uri)
    {
        var styleInclude = new StyleInclude(baseUri: null) { Source = uri };
        Add(styleInclude);
        ((IResourceProvider)styleInclude).RemoveOwner(styleInclude.Owner!);
    }
}