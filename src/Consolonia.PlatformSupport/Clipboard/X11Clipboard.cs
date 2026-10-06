using System.Threading.Tasks;
using Avalonia.Threading;
using Consolonia.Controls;
using Consolonia.Core.Infrastructure;
using jinek.X11;

namespace Consolonia.PlatformSupport.Clipboard
{
    internal class X11Clipboard : ConsoleClipboard
    {
        public X11Clipboard()
        {
            jinek.X11.X11Clipboard.Clipboard.UnhandledException += ClipboardOnUnhandledException;
        }

        public override async Task ClearAsync()
        {
            await base.ClearAsync();

            jinek.X11.X11Clipboard.Clipboard.Clear();
        }

        public override Task<string> GetTextAsync()
        {
            string text = jinek.X11.X11Clipboard.Clipboard.GetText();
            return Task.FromResult(text);
        }

        public override Task SetTextAsync(string text)
        {
            jinek.X11.X11Clipboard.Clipboard.SetText(text ?? string.Empty);
            return Task.CompletedTask;
        }

        private static void ClipboardOnUnhandledException(object sender, X11ClipboardLoopExceptionEventArgs e)
        {
            e.Handled = true;
            Dispatcher.UIThread.Post(
                () => throw new ConsoloniaException("Exception in clipboard loop", e.Exception),
                DispatcherPriority.MaxValue);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) jinek.X11.X11Clipboard.Clipboard.UnhandledException -= ClipboardOnUnhandledException;
        }
    }
}