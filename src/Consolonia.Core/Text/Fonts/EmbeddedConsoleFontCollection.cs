using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Fonts;
using Avalonia.Platform;

namespace Consolonia.Core.Text.Fonts
{
    public class EmbeddedConsoleFontCollection : IFontCollection
    {
        private readonly List<FontFamily> _fontFamilies = new();
        private readonly Dictionary<string, Uri[]> _fontFamilyUris;
        private readonly Dictionary<string, Uri> _fontUris;
        private readonly Dictionary<string, IConsoleTypeface> _typefaceByName = new();
        private readonly Dictionary<Uri, IConsoleTypeface> _typefaceByUri = new();
        private bool _disposedValue;

        /// <summary>
        ///     Construct a font collection from a font URI
        /// </summary>
        /// <param name="fontUri">Example: fonts:Consolonia </param>
        /// <param name="resourceUri">The a embedded resources Uri. Example: avares://MyAssembly/Fonts</param>
        public EmbeddedConsoleFontCollection(Uri fontUri, Uri resourceUri)
        {
            Key = fontUri;
            _fontUris = AssetLoader.GetAssets(resourceUri, null)
                .Where(asset => asset.AbsolutePath.EndsWith(".tlf", StringComparison.OrdinalIgnoreCase) ||
                                asset.AbsolutePath.EndsWith(".flf", StringComparison.OrdinalIgnoreCase))
                .ToDictionary(uri => Path.GetFileNameWithoutExtension(uri.AbsolutePath));

            _fontFamilyUris = _fontUris // .Where(kv => Char.IsDigit(kv.Key[^1]))
                .GroupBy(kv => kv.Key.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9'))
                .ToDictionary(g => g.Key, g => g.Select(kv => kv.Value).ToArray());
            foreach (string familyName in _fontFamilyUris.Keys)
            {
                string key = familyName.TrimStart('#');
                _fontFamilies.Add(new FontFamily(Key, $"#{key}"));
            }
        }

        // Resharper disable once UnusedAutoPropertyAccessor.Global
        public IFontManagerImpl FontManager { get; private set; }

        public FontFamily this[int index] => _fontFamilies[index];

        public Uri Key { get; }

        public int Count => _fontUris.Count;

        public IEnumerator<FontFamily> GetEnumerator()
        {
            return _fontFamilies.GetEnumerator();
        }

        public void Initialize(IFontManagerImpl fontManager)
        {
            FontManager = fontManager;
        }

        public bool TryGetGlyphTypeface(string familyName, FontStyle style, FontWeight weight, FontStretch stretch,
            out GlyphTypeface glyphTypeface)
        {
            if (_typefaceByName.TryGetValue(familyName, out IConsoleTypeface consoleTypeface))
            {
                return TryCreateGlyphTypeface(consoleTypeface, out glyphTypeface);
            }

            if (_fontFamilyUris.TryGetValue(familyName, out Uri[] uris))
            {
                // load the family and all it's members
                consoleTypeface = LoadEmbeddedFontFamily(familyName, uris.ToArray());
                return TryCreateGlyphTypeface(consoleTypeface, out glyphTypeface);
            }

            if (_fontUris.TryGetValue(familyName, out Uri uri))
            {
                // load just the font
                consoleTypeface = LoadEmbeddedFont(uri);
                return TryCreateGlyphTypeface(consoleTypeface, out glyphTypeface);
            }

            glyphTypeface = null;
            return false;
        }

        // Resharper disable AssignNullToNotNullAttribute  - I just can't seem to get Resharper to understand the out parameter here
        public bool TryMatchCharacter(int codepoint, FontStyle fontStyle, FontWeight fontWeight,
            FontStretch fontStretch, string familyName, CultureInfo culture, out Typeface typeface)
        {
            if (_typefaceByName.TryGetValue(familyName, out IConsoleTypeface glyphTypeface) ||
                _fontFamilyUris.TryGetValue(familyName, out Uri[] uris) &&
                (glyphTypeface = LoadEmbeddedFontFamily(familyName, uris.ToArray())) != null ||
                _fontUris.TryGetValue(familyName, out Uri uri) &&
                (glyphTypeface = LoadEmbeddedFont(uri)) != null)
                if (glyphTypeface != null && glyphTypeface.TryGetGlyph((uint)codepoint, out _))
                {
                    typeface = new Typeface(familyName, fontStyle, fontWeight, fontStretch);
                    return true;
                }

            typeface = new Typeface();
            return false;
        }

        public bool TryGetFamilyTypefaces(string familyName, out IReadOnlyList<Typeface> familyTypefaces)
        {
            if (_fontFamilyUris.ContainsKey(familyName) || _fontUris.ContainsKey(familyName) ||
                _typefaceByName.ContainsKey(familyName))
            {
                familyTypefaces = new[]
                {
                    new Typeface(new FontFamily(Key, $"#{familyName}"), FontStyle.Normal, FontWeight.Normal,
                        FontStretch.Normal)
                };
                return true;
            }

            familyTypefaces = Array.Empty<Typeface>();
            return false;
        }

        public bool TryCreateSyntheticGlyphTypeface(GlyphTypeface glyphTypeface, FontStyle style,
            FontWeight weight, FontStretch stretch, out GlyphTypeface syntheticGlyphTypeface)
        {
            syntheticGlyphTypeface = null;
            return false;
        }

        public bool TryGetNearestMatch(string familyName, FontStyle style, FontWeight weight, FontStretch stretch,
            out GlyphTypeface glyphTypeface)
        {
            return TryGetGlyphTypeface(familyName, style, weight, stretch, out glyphTypeface);
        }
        // Resharper enable AssignNullToNotNullAttribute - I just can't seem to get Resharper to understand the out parameter here

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        /// <summary>
        ///     Load a typeface from an embedded resource by name
        /// </summary>
        /// <param name="familyName"></param>
        /// <param name="resources"></param>
        /// <returns></returns>
        private IConsoleTypeface LoadEmbeddedFontFamily(string familyName, params Uri[] resources)
        {
            var familyTypespace = new AsciiArtFamilyTypeface(familyName);

            foreach (Uri resource in resources)
            {
                if (!_typefaceByUri.TryGetValue(resource, out IConsoleTypeface typeface))
                    typeface = LoadEmbeddedFont(resource);
                familyTypespace.AddTypeface(typeface);
            }

            _typefaceByName[familyName] = familyTypespace;
            return familyTypespace;
        }

        /// <summary>
        ///     Load an embedded font from a given asset URI
        /// </summary>
        /// <param name="uri">avares: uri to the font</param>
        /// <returns></returns>
        private IConsoleTypeface LoadEmbeddedFont(Uri uri)
        {
            string resourceName = Path.GetFileName(uri.AbsolutePath);
            string fontName = Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            using Stream stream = AssetLoader.Open(uri);
            IConsoleTypeface typeface =
                AsciiArtTypefaceLoader.Load(stream, Path.GetFileNameWithoutExtension(resourceName));
            if (typeface == null)
                throw new InvalidOperationException($"Failed to load font variation: {resourceName}");
            _typefaceByName[fontName] = typeface;
            _typefaceByUri[uri] = typeface;
            return typeface;
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    foreach (IConsoleTypeface typeface in _typefaceByUri.Values.Distinct()) typeface.Dispose();
                    _typefaceByUri.Clear();
                    _typefaceByName.Clear();
                    _fontFamilies.Clear();
                }

                _disposedValue = true;
            }
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool TryCreateGlyphTypeface(IConsoleTypeface consoleTypeface, out GlyphTypeface glyphTypeface)
        {
            try
            {
                glyphTypeface = CreateGlyphTypeface(consoleTypeface);
                return true;
            }
            catch (InvalidOperationException)
            {
                glyphTypeface = null;
                return false;
            }
        }

        private GlyphTypeface CreateGlyphTypeface(IConsoleTypeface consoleTypeface)
        {
            if (FontManager is FontManagerImpl fontManager)
                return fontManager.CreateGlyphTypeface(consoleTypeface);

            return new GlyphTypeface(new ConsolePlatformTypeface(consoleTypeface, CreateFallbackTypeface(consoleTypeface)));
        }

        private IPlatformTypeface CreateFallbackTypeface(IConsoleTypeface consoleTypeface)
        {
            if (FontManager != null)
            {
                string fallbackFamily = FontManager.GetDefaultFontFamilyName();
                if (!string.IsNullOrEmpty(fallbackFamily) &&
                    FontManager.TryCreateGlyphTypeface(fallbackFamily, consoleTypeface.Style, consoleTypeface.Weight,
                        consoleTypeface.Stretch, out IPlatformTypeface fallbackTypeface))
                    return fallbackTypeface;

                if (FontManager.TryMatchCharacter(' ', consoleTypeface.Style, consoleTypeface.Weight,
                        consoleTypeface.Stretch, fallbackFamily, CultureInfo.CurrentCulture, out fallbackTypeface))
                    return fallbackTypeface;
            }

            return ConsolePlatformTypeface.CreateSystemFontFallbackTypeface(consoleTypeface);
        }
    }
}