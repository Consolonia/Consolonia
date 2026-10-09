using System.Net.Http;

namespace Consolonia.Gallery.Gallery
{
    /// <summary>The gallery's one <see cref="HttpClient" />, and whether pages should use the network.</summary>
    internal static class GalleryHttp
    {
        public static readonly HttpClient Client = new();

        /// <summary>
        ///     False under the gallery's own tests, which run offline and should not wait on a download.
        /// </summary>
        public static bool UseNetwork => ConsoloniaLifetime.Console.GetType().Name != "UnitTestConsole";
    }
}
