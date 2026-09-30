using System;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Reactive;
using Avalonia.Styling;

namespace Consolonia.Themes.Infrastructure
{
    /// <summary>
    ///     Base class for auto-switching Styles that react to a global resource named
    ///     "ConsoloniaThemeFamily" (e.g., "Modern", "TurboVision").
    /// </summary>
    public abstract class AutoThemeStylesBase : Styles
    {
        public const string ModernThemeKey = "Modern";
        public const string TurboVisionThemeKey = "TurboVision";
        public const string ConsoloniaThemeFamilyKey = "ConsoloniaThemeFamily";

        // ReSharper disable once NotAccessedField.Local //todo: low: where to dispose?
        private IDisposable _consoloniaThemeFamilySybscription;

        private string _currentFamily;

        protected AutoThemeStylesBase()
        {
            _consoloniaThemeFamilySybscription = this.GetResourceObservable(ConsoloniaThemeFamilyKey)
                .Subscribe(new AnonymousObserver<object>(ApplyFromTheme));
        }

        private void ApplyFromTheme(object value)
        {
            if (value is null || ReferenceEquals(value, AvaloniaProperty.UnsetValue))
                Apply(null);
            else if (value is string family)
                Apply(family);
            else
                throw new InvalidOperationException(
                    $"Resource '{ConsoloniaThemeFamilyKey}' must be a theme family string.");
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
        ///     Compose this Styles instance for the specified theme family.
        ///     Implementations should call  <see cref="IncludeStyle" />.
        /// </summary>
        protected abstract void ComposeForFamily(string family);

        /// <summary>
        ///     Adds a compiled Styles root for the selected family.
        /// </summary>
        protected void IncludeStyle(Styles style)
        {
            Add(style);
            
            ((IResourceProvider)style).RemoveOwner(style.Owner!);
        }

        /// <summary>
        ///     Supports legacy dynamic URI includes in JIT applications. Use compiled styles for NativeAOT.
        /// </summary>
        [RequiresUnreferencedCode("Programmatic URI style loading is incompatible with trimming. Use IncludeStyle(IStyle) with a compiled Styles root.")]
        protected void IncludeStyle(Uri uri)
        {
            var styleInclude = new StyleInclude(baseUri: null) { Source = uri };
            Add(styleInclude);
            ((IResourceProvider)styleInclude).RemoveOwner(styleInclude.Owner!);
        }
    }
}