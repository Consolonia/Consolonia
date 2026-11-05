namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Makes the <see cref="IConsoleImageEncoder" /> for a console, registered with Avalonia's locator by
    ///     a library that implements image protocols. <see cref="AnsiConsoleOutput" /> asks it once the
    ///     terminal's capabilities are known; with none registered, or none created, pictures are drawn as
    ///     block glyphs.
    /// </summary>
    public interface IConsoleImageEncoderFactory
    {
        /// <summary>
        ///     The encoder for the image protocol the <paramref name="output" />'s capabilities name, or null
        ///     when they name none this factory speaks. Which protocol wins when the terminal has several is
        ///     the factory's choice.
        /// </summary>
        IConsoleImageEncoder CreateEncoder(IConsoleOutput output);
    }
}
