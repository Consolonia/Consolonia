# GPM (General Purpose Mouse) Support for Consolonia

This document describes the GPM mouse input support added to Consolonia for TTY environments.

## Overview

GPM (General Purpose Mouse) is a daemon that provides mouse support for Linux console applications running in TTY mode (without X11 or Wayland). The GPM support in Consolonia enables full mouse functionality including:

- Mouse movement tracking
- Button press/release (left, middle, right buttons)
- Mouse wheel scrolling
- Drag operations
- Keyboard modifier detection (Shift, Ctrl, Alt)

## Architecture

### Components

1. **GpmNativeBindings.cs** - P/Invoke definitions for libgpm
   - Native function bindings (Gpm_Open, Gpm_Close, Gpm_GetEvent, Gpm_Fd)
   - Event and connection structures
   - GPM availability detection

2. **GpmConsole.cs** - Console implementation with GPM integration
   - Extends CursesConsole for keyboard and ncurses mouse support
   - Adds GPM event loop for enhanced mouse support in TTY
   - Translates GPM events to Avalonia input events

3. **PlatformSupportExtensions.cs** - Automatic detection and registration
   - Detects TTY environment (no DISPLAY or WAYLAND_DISPLAY)
   - Checks GPM availability
   - Automatically uses GpmConsole when appropriate

## How It Works

### Initialization

When running in a TTY environment:

1. `PlatformSupportExtensions.CreateUnixConsole()` checks environment variables
2. If `DISPLAY` and `WAYLAND_DISPLAY` are empty, it's a TTY environment
3. Calls `GpmNativeBindings.IsGpmAvailable()` to check if GPM daemon is running
4. If available, creates `GpmConsole` instead of standard `CursesConsole`

### Event Processing

`GpmConsole` runs two parallel event loops:

1. **ncurses event loop** (inherited from CursesConsole) - Handles keyboard and fallback mouse
2. **GPM event loop** (new) - Handles enhanced mouse input via libgpm

The GPM event loop:
- Uses `select()` to wait for events with timeout
- Calls `Gpm_GetEvent()` to retrieve mouse events
- Translates GPM events to Avalonia `RawPointerEventArgs`
- Dispatches events to the UI thread

### Event Translation

GPM events are translated as follows:

| GPM Event Type | Avalonia Event Type |
|---------------|-------------------|
| GPM_DOWN | LeftButtonDown, MiddleButtonDown, RightButtonDown |
| GPM_UP | LeftButtonUp, MiddleButtonUp, RightButtonUp |
| GPM_MOVE | Move |
| GPM_DRAG | Move (with button modifiers) |
| GPM_B_UP | Wheel (positive delta) |
| GPM_B_DOWN | Wheel (negative delta) |

## Requirements

### System Requirements

- Linux with TTY console
- GPM daemon installed and running (`gpm` package)
- `libgpm.so.2` shared library

### Installation

On Debian/Ubuntu:
```bash
sudo apt-get install gpm libgpm2
sudo systemctl start gpm
sudo systemctl enable gpm
```

On Fedora/RHEL:
```bash
sudo dnf install gpm gpm-libs
sudo systemctl start gpm
sudo systemctl enable gpm
```

On Arch Linux:
```bash
sudo pacman -S gpm
sudo systemctl start gpm
sudo systemctl enable gpm
```

### Configuration

GPM configuration is typically in `/etc/gpm.conf`. A basic configuration:

```
device=/dev/input/mice
responsiveness=
repeat_type=none
type=exps2
append=""
sample_rate=
```

Start GPM with:
```bash
sudo gpm -m /dev/input/mice -t exps2
```

## Testing

### Verify GPM is Running

```bash
# Check if GPM daemon is running
ps aux | grep gpm

# Test GPM in console
sudo gpm -m /dev/input/mice -t exps2
```

### Test with Consolonia

1. Switch to a TTY console (Ctrl+Alt+F2)
2. Run a Consolonia application:
   ```bash
   cd Consolonia.Gallery
   dotnet run
   ```
3. Move mouse and click - you should see the cursor respond

### Fallback Behavior

If GPM is not available:
- `GpmConsole` falls back to `CursesConsole` behavior
- ncurses mouse support (if available) is still used
- Application continues to work without GPM

## Debugging

### Enable Debug Output

Set environment variable to see GPM initialization:
```bash
export CONSOLONIA_DEBUG=1
```

### Common Issues

1. **Mouse not working in TTY**
   - Check if GPM daemon is running: `systemctl status gpm`
   - Verify libgpm is installed: `ldconfig -p | grep libgpm`
   - Check GPM device: `/dev/input/mice` should exist

2. **Permission denied**
   - GPM typically requires root or proper permissions
   - User must be in `input` group: `sudo usermod -a -G input $USER`

3. **Wrong mouse protocol**
   - Try different mouse types: `ps2`, `imps2`, `exps2`, `ms`
   - Example: `sudo gpm -m /dev/input/mice -t imps2`

## Limitations

1. **TTY Only** - GPM only works in console mode, not in X11/Wayland
2. **Linux Only** - GPM is Linux-specific
3. **Character Grid** - Mouse coordinates are character-based, not pixel-based
4. **Single Application** - Only one application can use GPM at a time

## Performance

- Event loop uses 100ms timeout to balance responsiveness and CPU usage
- `select()` syscall waits efficiently for events
- No polling - events are asynchronous

## Security Considerations

- GPM daemon typically runs as root
- Applications connect via Unix socket
- No direct hardware access from application
- Follows standard Linux permission model

## Future Enhancements

Possible improvements:
1. Support for additional mouse buttons (GPM_B_FOURTH)
2. Configurable timeout values
3. Enhanced gesture support
4. Better integration with ncurses mouse events
5. Coordinate smoothing for high-resolution mice

## References

- [GPM Project](https://www.nico.schottelius.org/software/gpm/)
- [GPM Manual](http://manpages.ubuntu.com/manpages/focal/man8/gpm.8.html)
- [Linux Console Programming](https://tldp.org/HOWTO/Keyboard-and-Console-HOWTO.html)
