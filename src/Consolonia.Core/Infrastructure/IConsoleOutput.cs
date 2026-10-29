using System;
using System.Text;
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
        ///     The render target is about to write a frame: the cells that changed since the last one.
        /// </summary>
        void BeginFrame()
        {
        }

        /// <summary>
        ///     Every changed cell of the frame has been written; <paramref name="screen" /> is what the
        ///     terminal should now show. An output with per-frame work besides the cells (an image protocol
        ///     placing its pictures) does it here.
        /// </summary>
        void EndFrame(PixelBuffer screen)
        {
        }

        /// <summary>
        ///     The render target no longer trusts what the terminal shows (the screen was resized, another
        ///     program had the terminal) and will write every cell again. Whatever the output believes about
        ///     the terminal's state is no longer true either.
        /// </summary>
        void ResetCache()
        {
        }

        /// <summary>
        ///     Write raw text to the console
        /// </summary>
        /// <param name="str"></param>
        void WriteText(string str);

        /// <summary>
        ///     Write raw ASCII escape sequences given as bytes, such as image payloads, which an output that
        ///     buffers bytes takes without converting them to text and back.
        /// </summary>
        /// <remarks>Like <see cref="WriteText" />, this does not move the caret position.</remarks>
        void WriteBytes(ReadOnlySpan<byte> ascii)
        {
            WriteText(Encoding.ASCII.GetString(ascii));
        }

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