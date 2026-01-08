using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Reactive;
using Avalonia.Styling;
using Avalonia.Threading;

namespace Consolonia.Themes;

/// <summary>
/// Base class for auto-switching Styles that react to a global resource named
/// "ConsoloniaThemeFamily" (e.g., "Modern", "TurboVision").
/// Derivatives implement composition via <see cref="ComposeForFamily"/>.
/// </summary>
public abstract class AutoThemeStylesBase : Styles
{
    private IDisposable? _subscription;
    private string? _currentFamily;

    protected AutoThemeStylesBase()
    {
        ((IResourceProvider)this).OwnerChanged += OnOwnerChanged;
    }

    private void OnOwnerChanged(object? sender, EventArgs e)
    {
        // Owner change may happen while resources are enumerated; defer subscription.
        Dispatcher.UIThread.Post(_ =>
        {
            _subscription?.Dispose();
            var owner = ((IResourceProvider)this).Owner;
            if (owner is null)
                return;

            _subscription = owner
                .GetResourceObservable("ConsoloniaThemeFamily")
                .Subscribe(new AnonymousObserver<object>(OnFamilyChanged));
        }, null, DispatcherPriority.MaxValue);
    }

    private void OnFamilyChanged(object value)
    {
        if (value is not string family)
        {
            // No theme family visible – clear our resources/styles.
            Resources = new ResourceDictionary();
            Clear();
            _currentFamily = null;
            return;
        }

        Apply(family);
    }

    private void Apply(string family)
    {
        if (string.Equals(_currentFamily, family, StringComparison.Ordinal))
            return;

        _currentFamily = family;

        // Reset resources and child styles before composing.
        Resources = new ResourceDictionary();
        Clear();

        ComposeForFamily(family);
    }

    /// <summary>
    /// Compose this Styles instance for the specified theme family.
    /// Implementations should call <see cref="MergeResource"/> and/or <see cref="IncludeStyle"/>.
    /// </summary>
    protected abstract void ComposeForFamily(string family);

    /// <summary>
    /// Helper to merge a ResourceDictionary-root XAML file.
    /// </summary>
    protected void MergeResource(Uri uri)
    {
        if (Resources is not ResourceDictionary rd)
        {
            rd = new ResourceDictionary();
            Resources = rd;
        }

        rd.MergedDictionaries.Add(new ResourceInclude(baseUri: null) { Source = uri });
    }

    /// <summary>
    /// Helper to include a Styles-root XAML file.
    /// </summary>
    protected void IncludeStyle(Uri uri)
    {
        Add(new StyleInclude(baseUri: null) { Source = uri });
    }
}
