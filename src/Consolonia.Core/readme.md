# Consolonia.Core
This package is the core Consolonia library.

## Background
Consolonia is a TUI (Text User Interface) (GUI Framework) implementation for [Avalonia UI](https://github.com/AvaloniaUI)

Supports XAML, data bindings, animation, styling and the rest from Avalonia.

## NativeAOT pixel JSON

`PixelBuffer`, `Pixel`, and `PixelForeground` use custom JSON converters.
Their nested calls previously used `JsonSerializer.Serialize/Deserialize` with
only `JsonSerializerOptions`, which requires reflection-based type discovery.
In a NativeAOT app with a source-generated context for `PixelBuffer`, those
calls emitted IL2026/IL3050 warnings and failed at runtime when nested type
metadata was absent.

Nested pixels, foregrounds, backgrounds, and symbols now use their statically
known converters. Explicit converters in `JsonSerializerOptions.Converters`
still take precedence, including converter factories. The default `CaretStyle`
JSON value remains numeric, and a configured converter can still override it.
Callers must supply source-generated metadata for the *root* type they pass to
`JsonSerializer`; the library does not enable reflection-based fallback.

The `Consolonia.NativeAot.Smoke` project under `src\Tests` verifies the
round-trip in a published net10 Windows NativeAOT executable. This upstream
change requires a new `Consolonia.Core` package release in addition to the
updated `Consolonia.Controls` package. It does not address NativeAOT warnings
from Avalonia's DataGrid or other external dependencies.

## NativeAOT platform and theme loading

`ConsoloniaScreen` now initializes Avalonia's screen properties through a
typed `PlatformScreen` subclass rather than reflection. The console window
provides its own `ILauncher` using the OS shell (Windows), `open` (macOS), or
`xdg-open` (Linux) instead of constructing Avalonia's internal launcher by
name. `Consolonia.PlatformSupport` registers a statically constructed
`IClipboard` adapter, preserving ownership and flush semantics without
reflecting into Avalonia's internal clipboard class.

The built-in DataGrid and managed-window auto themes select compiled XAML
style classes rather than loading theme URIs at runtime. Their theme resources
must be present when instantiated; in particular, TurboVision managed-window
styles use resources supplied by the TurboVision theme. Theme-family resource
updates also accept Avalonia's unset sentinel while rejecting values that are
not strings. Typed bindings replace reflection bindings in the console slider,
calendar day-title, and Modern border panel. The TurboVision window shade now
sizes through layout instead of binding to a nonexistent `OuterBorder`.

The native smoke executable checks brush interpolation, pixel JSON, screen
metadata, caret-timer configuration, compiled style roots, clipboard
registration, and Modern-to-TurboVision DataGrid theme-family switching.
It does **not** prove full application NativeAOT safety. The public
`ResourceIncludeBase` API still supports arbitrary dynamic XAML URIs for JIT
clients and is marked `RequiresUnreferencedCode`; callers receive a trimming
warning. Use compiled `Styles` roots in NativeAOT apps instead. The Gallery
example intentionally retains many reflection bindings, and Avalonia Controls
DataGrid, Iciclecreek, and Vanara can produce their own warnings. Do not treat
a successful scoped probe or package build as a warning-free application release.
