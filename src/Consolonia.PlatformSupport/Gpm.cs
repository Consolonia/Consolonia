using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Consolonia.PlatformSupport
{
    /// <summary>
    ///     GPM event types
    /// </summary>
    [Flags]
    internal enum GpmEventType : ushort
    {
        Move = 1,
        Drag = 2,
        Down = 4,
        Up = 8,
        SingleClick = 16, // 0x10
        DoubleClick = 32, // 0x20
        TripleClick = 64, // 0x40
        MFlag = 128, // 0x80
        Hard = 256 // 0x100
    }

    /// <summary>
    ///     GPM mouse button identifiers
    ///     Note: These match the gpm.h definitions exactly
    /// </summary>
    [Flags]
    internal enum GpmButtons : byte // Changed to byte since buttons field is byte
    {
        None = 0,
        Right = 1,
        Middle = 2,
        Left = 4,
        Fourth = 8,
        WheelUp = 16,
        WheelDown = 32 
    }

    /// <summary>
    ///     GPM keyboard modifier flags (matches Linux keyboard modifiers)
    /// </summary>
    [Flags]
    internal enum GpmModifiers : byte
    {
        None = 0x00,
        Shift = 0x01,
        Control = 0x04,
        Alt = 0x08
    }

    /// <summary>
    ///     GPM margin values
    /// </summary>
    [Flags]
    internal enum GpmMargin : ushort
    {
        Top = 1,
        Bottom = 2,
        Left = 4,
        Right = 8
    }

    /// <summary>
    ///     GPM event structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    [DebuggerDisplay("{Dump()}")]
    internal struct GpmEvent
    {
        public GpmButtons Buttons;
        public GpmModifiers Modifiers; 
        public ushort VirtualConsole; 
        public short DeltaX; 
        public short DeltaY; 
        public short X; 
        public short Y; 
        public GpmEventType Type; 
        public int Clicks; 
        public GpmMargin Margin; 

        public string Dump()
        {
            return
                $"Type: {DecodeType(),-30} Buttons: {DecodeButtons(),-15} Mods: {DecodeModifiers(),-15} Pos: [{X,3},{Y,3}] Δ: ({DeltaX,3},{DeltaY,3})";
        }

        private string DecodeType()
        {
            var parts = new List<string>();

            if (Type.HasFlag(GpmEventType.Move)) parts.Add("MOVE");
            if (Type.HasFlag(GpmEventType.Drag)) parts.Add("DRAG");
            if (Type.HasFlag(GpmEventType.Down)) parts.Add("DOWN");
            if (Type.HasFlag(GpmEventType.Up)) parts.Add("UP");
            if (Type.HasFlag(GpmEventType.SingleClick)) parts.Add("SINGLE");
            if (Type.HasFlag(GpmEventType.DoubleClick)) parts.Add("DOUBLE");
            if (Type.HasFlag(GpmEventType.TripleClick)) parts.Add("TRIPLE");
            if (Type.HasFlag(GpmEventType.MFlag)) parts.Add("MFLAG");
            if (Type.HasFlag(GpmEventType.Hard)) parts.Add("HARD");
            // if (type.HasFlag(GpmEventType.GPM_ENTER)) parts.Add("ENTER");
            //if (type.HasFlag(GpmEventType.GPM_LEAVE)) parts.Add("LEAVE");

            return parts.Count > 0 ? string.Join("|", parts) : "NONE";
        }

        private string DecodeButtons()
        {
            var parts = new List<string>();

            if (Buttons.HasFlag(GpmButtons.Left)) parts.Add("LEFT");
            if (Buttons.HasFlag(GpmButtons.Middle)) parts.Add("MIDDLE");
            if (Buttons.HasFlag(GpmButtons.Right)) parts.Add("RIGHT");
            if (Buttons.HasFlag(GpmButtons.Fourth)) parts.Add("FOURTH");
            if (Buttons.HasFlag(GpmButtons.WheelUp)) parts.Add("UP");
            if (Buttons.HasFlag(GpmButtons.WheelDown)) parts.Add("DOWN");

            return parts.Count > 0 ? string.Join("|", parts) : "NONE";
        }

        private string DecodeModifiers()
        {
            var parts = new List<string>();

            if (Modifiers.HasFlag(GpmModifiers.Shift)) parts.Add("SHIFT");
            if (Modifiers.HasFlag(GpmModifiers.Control)) parts.Add("CTRL");
            if (Modifiers.HasFlag(GpmModifiers.Alt)) parts.Add("ALT");

            return parts.Count > 0 ? string.Join("|", parts) : "NONE";
        }
    }

    /// <summary>
    ///     GPM connection structure
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct GpmConnect
    {
        public ushort EventMask;
        public ushort DefaultMask;
        public ushort MinMod;
        public ushort MaxMod;
        public int Pid;
        public int VirtualConsole;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Timeval
    {
        public long Sec;
        public long Usec;
    }

#pragma warning disable CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes

    /// <summary>
    ///     Native bindings for libgpm (General Purpose Mouse) library
    ///     Used for mouse input in TTY environments without X11/Wayland
    /// </summary>
    internal static class Gpm
    {
        private const string LibraryName = "libgpm.so.2";


        /// <summary>
        ///     Open a connection to the GPM daemon
        /// </summary>
        /// <param name="conn">Connection structure to initialize</param>
        /// <param name="flag">Connection flags (0 = default)</param>
        /// <returns>File descriptor on success, -1 on error</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Gpm_Open")]
        public static extern int Open(ref GpmConnect conn, int flag);

        /// <summary>
        ///     Close the GPM connection
        /// </summary>
        /// <returns>0 on success, -1 on error</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Gpm_Close")]
        public static extern int Close();

        /// <summary>
        ///     Get a mouse event (blocking)
        /// </summary>
        /// <param name="event">Event structure to fill</param>
        /// <returns>1 if event received, 0 if no event, -1 on error</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Gpm_GetEvent")]
        public static extern int GetEvent(out GpmEvent @event);


        /// <summary>
        ///     Get file descriptor for select/poll
        /// </summary>
        /// <returns>File descriptor</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Gpm_Fd")]
        public static extern int GetFd();

        /// <summary>
        ///     Check if GPM is available
        /// </summary>
        /// <returns>True if GPM daemon is running and accessible</returns>
        public static bool IsGpmAvailable()
        {
            try
            {
                var conn = new GpmConnect
                {
                    EventMask = 0,
                    DefaultMask = 0,
                    MinMod = 0,
                    MaxMod = 0
                };

                int fd = Open(ref conn, 0);
                if (fd >= 0)
                {
                    _ = Close();
                    return true;
                }

                return false;
            }
            catch (DllNotFoundException)
            {
                return false;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
            catch (BadImageFormatException)
            {
                // Wrong architecture or corrupted library
                return false;
            }
        }

        /// <summary>
        ///     Control GPM cursor visibility
        /// </summary>
        /// <param name="x">X position (-1 to hide cursor)</param>
        /// <param name="y">Y position</param>
        /// <param name="flag">Drawing flag</param>
        /// <returns>0 on success</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "Gpm_DrawPointer")]
        public static extern int DrawPointer(int x, int y, int flag);

        [DllImport("libc", EntryPoint = "select", SetLastError = true)]
        public static extern int Select(int nfds, IntPtr readfds, IntPtr writefds, IntPtr exceptfds,
            ref Timeval timeout);
    }
#pragma warning restore CA5392 // Use DefaultDllImportSearchPaths attribute for P/Invokes
}