using System;
using System.IO;
using System.Reflection;
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
                PlatformID.Unix or PlatformID.MacOSX => CreateUnixConsole(),
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
            // Clipboard is new Avalonia wrapper around platform IClipboardImpl, but unfortunately is marked as internal.
            // This can be replaced with: ```new Clipboard(clipboardImpl);``` when/if avalonia changes the visibility of
            // Clipboard to public.
            return builder.With(CreateInternalInstance<IClipboard>("Avalonia.Base",
                "Avalonia.Input.Platform.Clipboard",
                [clipboardImpl]));
        }

        public static bool IsWslPlatform()
        {
            return !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WSL_DISTRO_NAME"));
        }

        private static T CreateInternalInstance<T>(string assembly, string name, object[] args = null)
            where T : class
        {
            try
            {
                Assembly asm = Assembly.Load(assembly);
                Type type = asm.GetType(name, true);

                ArgumentNullException.ThrowIfNull(type, nameof(type));

                object instance = Activator.CreateInstance(
                    type,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    args,
                    null);
                ArgumentNullException.ThrowIfNull(instance, nameof(instance));
                return (T)instance!;
            }
            catch (Exception ex) when (ex is FileNotFoundException or BadImageFormatException or TypeLoadException or
                                           MissingMethodException or TargetInvocationException or InvalidCastException)
            {
                throw new InvalidOperationException(
                    $"Failed to create internal instance of type '{name}' from assembly '{assembly}'. " +
                    "This may indicate an incompatible Avalonia version.", ex);
            }
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
                            result = new EgaConsoleColorMode();
                    }
                        break;
                    case PlatformID.MacOSX:
                        result = new RgbConsoleColorMode();
                        break;
                    case PlatformID.Unix:
                        string term = Environment.GetEnvironmentVariable("TERM");
                        result = term switch
                        {
                            "linux" or "xterm-direct" or "xterm-color" => new EgaConsoleColorMode(),
                            "xterm-256color" or "screen-256color" or "tmux-256color" => new RgbConsoleColorMode(),
                            _ => new EgaConsoleColorMode()
                        };
                        break;
                    default:
                        result = new EgaConsoleColorMode();
                        break;
                }

            return builder.UseConsoleColorMode(result);
        }

        private static bool IsWindowsTerminal()
        {
            return Environment.GetEnvironmentVariable("WT_SESSION") is not null ||
                   Environment.GetEnvironmentVariable("VSAPPIDNAME") != null;
        }

        /// <summary>
        /// Creates the appropriate console implementation for Unix/MacOSX platforms.
        /// Prefers GpmConsole if GPM is available in TTY environments, otherwise falls back to CursesConsole.
        /// </summary>
        private static IConsole CreateUnixConsole()
        {
            Console.ReadKey();
            // Check if we're in a TTY environment (not X11/Wayland)
            bool isTTY = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY")) &&
                         string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));
isTTY = true;
            // If in TTY and GPM is available, use GpmConsole for better mouse support
            if (isTTY && GpmNativeBindings.IsGpmAvailable())
            {
                return new GpmConsole();
            }
            // if (isTTY)
            // {
            //     Console.WriteLine("Using EvdevConsole for TTY environment.");
            //     Console.ReadKey();
            //     return new EvdevConsole();

            // }

            // Otherwise use standard CursesConsole
            return new CursesConsole();
        }
    }
}
