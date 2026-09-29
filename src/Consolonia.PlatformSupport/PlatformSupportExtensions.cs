using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Consolonia.Core.Drawing.PixelBufferImplementation;
using Consolonia.Core.Drawing.PixelBufferImplementation.EgaConsoleColor;
using Consolonia.Core.Dummy;
using Consolonia.Core.Infrastructure;
using Consolonia.PlatformSupport;
using Consolonia.PlatformSupport.Clipboard;
using jinek.X11;
using X11Clipboard = Consolonia.PlatformSupport.Clipboard.X11Clipboard;

// ReSharper disable CheckNamespace
#pragma warning disable IDE0161
#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Consolonia
#pragma warning restore IDE0130 // Namespace does not match folder structure
#pragma warning restore IDE0161
{
    public static class PlatformSupportExtensions
    {
        public static AppBuilder UseAutoDetectedConsole(this AppBuilder builder)
        {
            if (Design.IsDesignMode)
                // in design mode we can't use any console operations at all, so we use a dummy IConsole.
                return builder.UseConsole(new DummyConsole());

            IConsole console = Environment.OSVersion.Platform switch
            {
#pragma warning disable CA1416 // Validate platform compatibility
                PlatformID.Win32S or PlatformID.Win32Windows or PlatformID.Win32NT =>
                    new Win32Console(Console.IsOutputRedirected || IsWindowsTerminal()
                        ? new AnsiConsoleOutput()
                        : new WindowsLegacyConsoleOutput()),
#pragma warning restore CA1416 // Validate platform compatibility
                PlatformID.Unix or PlatformID.MacOSX => new CursesConsole(),
                _ => new DefaultNetConsole()
            };

            return builder.UseConsole(console)
                .UseAutoDetectClipboard()
                .UseAutoDetectConsoleColorMode();
        }


        /// <summary>Provides cut, copy, and paste support for the OS clipboard.</summary>
        /// <remarks>
        ///     <para>On Windows, we use the Avalonia Windows Clipboard .</para>
        ///     <para>
        ///         On Linux, when not running under Windows Subsystem for Linux (WSL), we use X11Clipboard to call X11 PInvoke
        ///         calls.
        ///     </para>
        ///     <para>
        ///         On Linux, when running under Windows Subsystem for Linux (WSL), we use WslClipboard class launches
        ///         Windows' powershell.exe via WSL interop and uses the "Set-Clipboard" and "Get-Clipboard" Powershell CmdLets.
        ///     </para>
        ///     <para>
        ///         On the Mac, we use MacClipboard class which uses the MacOS X pbcopy and pbpaste command line tools and
        ///         the Mac clipboard APIs vai P/Invoke.
        ///     </para>
        /// </remarks>
        public static AppBuilder UseAutoDetectClipboard(this AppBuilder builder)
        {
            IClipboardImpl clipboardImpl = null;

            if (OperatingSystem.IsWindows())
            {
                clipboardImpl = new Win32Clipboard();
            }
            else if (OperatingSystem.IsMacOS())
            {
                clipboardImpl = new MacClipboard();
            }
            else if (OperatingSystem.IsLinux())
            {
                if (IsWslPlatform())
                    clipboardImpl = new WslClipboard();
                else
                    try
                    {
                        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")))
                            clipboardImpl = new X11Clipboard();
                    }
                    catch (X11ClipboardException)
                    {
                    }

                if (clipboardImpl == null)
                    try
                    {
                        // alternatively use xclip CLI tool
                        clipboardImpl = new XClipClipboard();
                    }
                    catch (NotSupportedException)
                    {
                        clipboardImpl = new ConsoleClipboard();
                    }
            }
            else
            {
                clipboardImpl = new ConsoleClipboard();
            }

            return builder.UseClipboard(clipboardImpl);
        }

        public static AppBuilder UseClipboard(this AppBuilder builder, IClipboardImpl clipboardImpl)
        {
            ArgumentNullException.ThrowIfNull(clipboardImpl);
            return builder.With<IClipboard>(new PlatformClipboard(clipboardImpl));
        }

        public static bool IsWslPlatform()
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"));
        }

        public static AppBuilder UseAutoDetectConsoleColorMode(this AppBuilder builder)
        {
            IConsoleColorMode result;
            if (Design.IsDesignMode)
                result = new RgbConsoleColorMode();
            else
                switch (Environment.OSVersion.Platform)
                {
                    case PlatformID.Win32S or PlatformID.Win32Windows or PlatformID.Win32NT:
                    {
                        // if output is redirected, or we are a windows terminal we use the win32 ANSI based console.
                        if (Console.IsOutputRedirected || IsWindowsTerminal())
                            result = new RgbConsoleColorMode();
                        else
                            result = new EgaConsoleColorMode(true);
                    }
                        break;
                    case PlatformID.MacOSX:
                        result = new RgbConsoleColorMode();
                        break;
                    case PlatformID.Unix:
                        if (Environment.GetEnvironmentVariable("COLORTERM") is "truecolor" or "24bit")
                        {
                            result = new RgbConsoleColorMode();
                            break;
                        }

                        string term = Environment.GetEnvironmentVariable("TERM");
                        result = term switch
                        {
                            "linux" => new EgaConsoleColorMode(false),
                            "xterm-direct" or "xterm-color" => new EgaConsoleColorMode(true),
                            "xterm-256color" or "screen-256color" or "tmux-256color" => new RgbConsoleColorMode(),
                            _ => new EgaConsoleColorMode(
                                false) // for example "xterm" which is set by Far2l ran from tty
                        };
                        break;
                    default:
                        result = new EgaConsoleColorMode(true);
                        break;
                }

            return builder.UseConsoleColorMode(result);
        }

        private static bool IsWindowsTerminal()
        {
            return Environment.GetEnvironmentVariable("WT_SESSION") is not null ||
                   Environment.GetEnvironmentVariable("VSAPPIDNAME") != null;
        }
    }
}