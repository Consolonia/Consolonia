# Consolonia.PlatformSupport
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
