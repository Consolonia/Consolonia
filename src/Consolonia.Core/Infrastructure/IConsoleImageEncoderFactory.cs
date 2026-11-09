namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Creates the <see cref="IConsoleImageEncoder" /> for a console. A library that implements image
    ///     protocols registers a factory with Avalonia's locator (<c>AppBuilder.With</c>). If no factory is
    ///     registered, or it returns no encoder, images are drawn with block characters.
    /// </summary>
    public interface IConsoleImageEncoderFactory
    {
        /// <summary>
        ///     Returns an encoder for an image protocol in the capabilities of <paramref name="output" />.
        ///     Returns null if this factory supports none of them. If the terminal supports several protocols,
        ///     the factory chooses one. The console calls this again each time it is prepared, because its
        ///     capabilities may have changed.
        /// </summary>
        IConsoleImageEncoder CreateEncoder(IConsoleOutput output);
    }
}