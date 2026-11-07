using Avalonia;
using Consolonia.Core.Infrastructure;
using Consolonia.Imaging;

// ReSharper disable CheckNamespace
namespace Consolonia
{
    public static class ImagingStartupExtensions
    {
        /// <summary>
        ///     Shows pictures as pixels on terminals that have the kitty graphics protocol or sixel, rather
        ///     than as block glyphs. Can be called anywhere in the builder chain: the console gets the
        ///     encoder once the application is set up.
        /// </summary>
        public static AppBuilder UseConsoloniaImaging(this AppBuilder builder)
        {
            return builder.AfterPlatformServicesSetup(_ =>
                AvaloniaLocator.Current.GetService<IConsoleOutput>()
                    ?.SetImageEncoderFactory(new ConsoleImageEncoderFactory()));
        }
    }
}
