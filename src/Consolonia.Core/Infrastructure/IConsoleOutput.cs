using System;
using System.Collections.Generic;
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
        private static readonly IReadOnlySet<string> NoPointerShapes = new HashSet<string>();
        PixelBufferSize Size { get; set; }

        /// <summary>
        ///     The mouse pointer shapes the terminal can draw itself (Kitty's pointer shape protocol,
        ///     OSC 22), as CSS cursor names. Empty when it cannot, and the pointer is drawn as a
        ///     character instead.
        /// </summary>
        IReadOnlySet<string> SupportedPointerShapes => NoPointerShapes;

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
        ///     Starts a frame. Everything written until <see cref="EndUpdate" /> belongs to one frame. If the
        ///     terminal can show a whole frame at once (synchronized update, DEC mode 2026), the output starts
        ///     that here.
        /// </summary>
        void BeginUpdate()
        {
        }

        /// <summary>
        ///     Ends the frame started by <see cref="BeginUpdate" />. All cells and the caret are already written.
        ///     The output does any other per-frame work here (for example, an image protocol places its images)
        ///     and leaves the cursor at the caret position. Then it closes the synchronized update and flushes.
        /// </summary>
        /// <param name="screen">
        ///     What the terminal should show now. This is every cell as it was last written, not the pixel
        ///     buffer that was drawn into. For example, the cell under the mouse cursor contains the cursor.
        /// </param>
        void EndUpdate(PixelBuffer screen)
        {
            Flush();
        }

        /// <summary>
        ///     Clears any cached terminal state. The render target calls this when the terminal may no longer
        ///     show what was written (the screen was resized, or another program used the terminal). The render
        ///     target then writes every cell again.
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
        ///     Writes raw escape sequences given as ASCII bytes, such as image payloads. An output that buffers
        ///     bytes can write them without converting them to text first.
        /// </summary>
        /// <remarks>
        ///     The payload may move the terminal's cursor. For example, a sixel image moves it by an amount
        ///     that depends on the terminal. So the output must treat the cursor position as unknown. The next
        ///     <see cref="SetCaretPosition" /> must move the cursor, even to the position that was last set.
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

        /// <summary>
        ///     Have the terminal draw its mouse pointer as <paramref name="shape" />, one of
        ///     <see cref="SupportedPointerShapes" />.
        /// </summary>
        void SetPointerShape(string shape)
        {
        }
    }
}