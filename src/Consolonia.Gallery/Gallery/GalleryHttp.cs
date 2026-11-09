using System.Net.Http;

namespace Consolonia.Gallery.Gallery
{
    /// <summary>
    ///     Holds the single <see cref="HttpClient" /> shared by the gallery pages, and tells pages whether
    ///     they may use the network.
    /// </summary>
    internal static class GalleryHttp
    {
        public static readonly HttpClient Client = new();

        /// <summary>
        ///     False when the gallery runs under its unit tests (the console is UnitTestConsole).
        ///     The tests run offline and should not wait for downloads.
        /// </summary>
        public static bool UseNetwork => ConsoloniaLifetime.Console.GetType().Name != "UnitTestConsole";
    }
}