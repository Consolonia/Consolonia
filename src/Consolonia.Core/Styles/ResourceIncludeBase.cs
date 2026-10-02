using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

// ReSharper disable MemberCanBePrivate.Global

namespace Consolonia.Core.Styles
{
    // Copy-paste from FluentTheme from Avalonia
    [RequiresUnreferencedCode(
        "Arbitrary URI-based XAML loading requires reflection. Use a compiled Styles root in trimmed applications.")]
    public abstract class ResourceIncludeBase(Uri baseUri) : IResourceProvider, IStyle
    {
        private bool _isLoading;
        private IStyle[] _loaded;

        protected ResourceIncludeBase(IServiceProvider serviceProvider) : this(
            ((IUriContext)serviceProvider.GetService(typeof(IUriContext)))!.BaseUri)
        {
        }

        protected abstract Uri Uri { get; }

        /// <summary>
        ///     Gets the loaded style.
        /// </summary>
        public IStyle Loaded
        {
            get
            {
                // ReSharper disable once InvertIf
                if (_loaded == null)
                {
                    _isLoading = true;
                    var loaded = (IStyle)AvaloniaXamlLoader.Load(Uri, baseUri);
                    _loaded = [loaded];
                    _isLoading = false;
                }

                return _loaded[0];
            }
        }

        public bool TryGetResource(object key, ThemeVariant theme, out object value)
        {
            if (!_isLoading && Loaded is IResourceProvider p)
                return p.TryGetResource(key, theme, out value);

            value = null;
            return false;
        }

        public bool HasResources => (Loaded as IResourceProvider)?.HasResources ?? false;

        public void AddOwner(IResourceHost owner)
        {
            (Loaded as IResourceProvider)?.AddOwner(owner);
        }

        public void RemoveOwner(IResourceHost owner)
        {
            (Loaded as IResourceProvider)?.RemoveOwner(owner);
        }

        public IResourceHost Owner => (Loaded as IResourceProvider)?.Owner;

        public event EventHandler OwnerChanged
        {
            add
            {
                if (Loaded is IResourceProvider rp) rp.OwnerChanged += value;
            }
            remove
            {
                if (Loaded is IResourceProvider rp) rp.OwnerChanged -= value;
            }
        }

        public IReadOnlyList<IStyle> Children => _loaded ?? Array.Empty<IStyle>();
    }
}