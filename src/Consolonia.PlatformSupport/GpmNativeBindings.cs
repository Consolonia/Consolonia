using System;
using System.Runtime.InteropServices;

namespace Consolonia.PlatformSupport
{
    /// <summary>
    /// Native bindings for libgpm (General Purpose Mouse) library
    /// Used for mouse input in TTY environments without X11/Wayland
    /// </summary>
    internal static class GpmNativeBindings
    {
        private const string LibraryName = "libgpm.so.2";

        /// <summary>
        /// GPM event types
        /// </summary>
        [Flags]
        public enum GpmEventType : ushort
        {
            GPM_MOVE = 1,
            GPM_DRAG = 2,
            GPM_DOWN = 4,
            GPM_UP = 8,
            GPM_SINGLE = 16,
            GPM_DOUBLE = 32,
            GPM_TRIPLE = 64,
            GPM_MFLAG = 128,
            GPM_HARD = 256
        }

        /// <summary>
        /// GPM mouse button identifiers
        /// </summary>
        [Flags]
        public enum GpmButtons : ushort
        {
            GPM_B_LEFT = 1,
            GPM_B_MIDDLE = 2,
            GPM_B_RIGHT = 4,
            GPM_B_UP = 8,
            GPM_B_DOWN = 16,
            GPM_B_FOURTH = 32
        }

        /// <summary>
        /// GPM margin values
        /// </summary>
        [Flags]
        public enum GpmMargin : ushort
        {
            GPM_TOP = 1,
            GPM_BOT = 2,
            GPM_LFT = 4,
            GPM_RGT = 8
        }

        /// <summary>
        /// GPM event structure
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Gpm_Event
        {
            public byte buttons;        // Current button state
            public byte modifiers;      // Shift, control, etc.
            public ushort vc;          // Virtual console number
            public short dx;           // Delta x (movement)
            public short dy;           // Delta y (movement)
            public short x;            // Absolute x position (1-based)
            public short y;            // Absolute y position (1-based)
            public GpmEventType type;  // Event type
            public int clicks;         // Click count
            public GpmMargin margin;   // Screen margin
        }

        /// <summary>
        /// GPM connection structure
        /// </summary>
        [StructLayout(LayoutKind.Sequential)]
        public struct Gpm_Connect
        {
            public ushort eventMask;      // Event types to receive
            public ushort defaultMask;    // Default event mask
            public ushort minMod;         // Minimum modifier
            public ushort maxMod;         // Maximum modifier
            public int pid;               // Process ID
            public int vc;                // Virtual console
        }

        /// <summary>
        /// Open a connection to the GPM daemon
        /// </summary>
        /// <param name="conn">Connection structure to initialize</param>
        /// <param name="flag">Connection flags (0 = default)</param>
        /// <returns>File descriptor on success, -1 on error</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Gpm_Open(ref Gpm_Connect conn, int flag);

        /// <summary>
        /// Close the GPM connection
        /// </summary>
        /// <returns>0 on success, -1 on error</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Gpm_Close();

        /// <summary>
        /// Get a mouse event (blocking)
        /// </summary>
        /// <param name="event">Event structure to fill</param>
        /// <returns>1 if event received, 0 if no event, -1 on error</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Gpm_GetEvent(out Gpm_Event @event);

        /// <summary>
        /// Get file descriptor for select/poll
        /// </summary>
        /// <returns>File descriptor</returns>
        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl)]
        public static extern int Gpm_Fd();

        /// <summary>
        /// Check if GPM is available
        /// </summary>
        /// <returns>True if GPM daemon is running and accessible</returns>
        public static bool IsGpmAvailable()
        {
            try
            {
                var conn = new Gpm_Connect
                {
                    eventMask = (ushort)(GpmEventType.GPM_MOVE | GpmEventType.GPM_DOWN | GpmEventType.GPM_UP | GpmEventType.GPM_DRAG),
                    defaultMask = 0,
                    minMod = 0,
                    maxMod = 0
                };

                int fd = Gpm_Open(ref conn, 0);
                if (fd >= 0)
                {
                    Gpm_Close();
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
    }
}
