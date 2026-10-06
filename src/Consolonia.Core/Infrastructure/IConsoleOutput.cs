using System.Threading.Tasks;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;

// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedMemberInSuper.Global

namespace Consolonia.Core.Infrastructure
{
    /// <summary>
    ///     Defines properties and methods for console output
    /// </summary>
    public interface IConsoleOutput : IConsoleCapabilities
    {
        PixelBufferSize Size { get; set; }

        /// <summary>
        ///     Set the title of the console window
        /// </summary>
        /// <param name="title"></param>
        void SetTitle(string title);

        /// <summary>
        ///     Set physical caret position
        /// </summary>
        /// <param name="bufferPoint"></param>
        void SetCaretPosition(PixelBufferCoordinate bufferPoint);

        /// <summary>
        ///     Get physical Caret position
        /// </summary>
        /// <returns></returns>
        PixelBufferCoordinate GetCaretPosition();

        /// <summary>
        ///     Change caret style
        /// </summary>
        /// <param name="caretStyle"></param>
        void SetCaretStyle(CaretStyle caretStyle);

        /// <summary>
        ///     Hide the caret
        /// </summary>
        void HideCaret();

        /// <summary>
        ///     Show the caret
        /// </summary>
        void ShowCaret();

        /// <summary>
        ///     Prepare the console
        /// </summary>
        void PrepareConsole();

        /// <summary>
        ///     Restore the console
        /// </summary>
        void RestoreConsole();

        /// <summary>
        ///     Clear the screen
        /// </summary>
        void ClearScreen();

        /// <summary>
        ///     Write a pixel to the console
        /// </summary>
        /// <param name="position">location for pixel</param>
        /// <param name="pixel">pixel to print</param>
        void WritePixel(PixelBufferCoordinate position, in Pixel pixel);

        /// <summary>
        ///     Write raw text to the console
        /// </summary>
        /// <param name="str"></param>
        void WriteText(string str);

        /// <summary>
        ///     Flush any buffered output
        /// </summary>
        void Flush();

        /// <summary>
        ///     Pause IO operations
        /// </summary>
        /// <param name="task"></param>
        void PauseIO(Task task);
    }
}