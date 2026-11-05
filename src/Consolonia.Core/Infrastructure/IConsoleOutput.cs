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
        ///     A frame starts: what is written until <see cref="EndUpdate" /> is one frame. An output that
        ///     can have the terminal show a frame all at once (a DEC 2026 synchronized update) opens it here.
        /// </summary>
        void BeginUpdate()
        {
        }

        /// <summary>
        ///     The frame <see cref="BeginUpdate" /> started is written, caret included; <paramref name="screen" />
        ///     is what the terminal should now show. An output with per-frame work besides the cells (an image
        ///     protocol placing its pictures) does it here, leaving the cursor where the caret was put, then
        ///     closes the update and flushes.
        /// </summary>
        void EndUpdate(PixelBuffer screen)
        {
            Flush();
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
        ///     Gives the output the factory of an image library (<c>UseConsoloniaImaging</c>), once the
        ///     application is set up: an output that speaks image protocols asks it for the encoder of the
        ///     protocol its terminal has, and shows pixel images
        ///     (<see cref="Consolonia.Controls.ConsoleCapabilities.SupportsPixelImages" />) when it gets one.
        ///     Other outputs ignore it.
        /// </summary>
        void SetImageEncoderFactory(IConsoleImageEncoderFactory factory)
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
        /// <remarks>
        ///     Where the terminal's cursor is afterwards is the payload's business (a sixel moves it, by an
        ///     amount that depends on the terminal), so the output assumes nothing: the next
        ///     <see cref="SetCaretPosition" /> moves it whatever position it last set.
        /// </remarks>
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