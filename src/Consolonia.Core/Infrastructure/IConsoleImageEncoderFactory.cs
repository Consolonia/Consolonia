namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Makes the <see cref="IConsoleImageEncoder" /> for a console: what a library that implements image
    ///     protocols gives the console through <see cref="IConsoleOutput.SetImageEncoderFactory" />. Without
    ///     one, or when it makes none, pictures are drawn as block glyphs.
    /// </summary>
    public interface IConsoleImageEncoderFactory
    {
        /// <summary>
        ///     The encoder for the image protocol the <paramref name="output" />'s capabilities name, or null
        ///     when they name none this factory speaks. Which protocol wins when the terminal has several is
        ///     the factory's choice. Asked again whenever the console is prepared, as its capabilities may
        ///     have changed.
        /// </summary>
        IConsoleImageEncoder CreateEncoder(IConsoleOutput output);
    }
}
