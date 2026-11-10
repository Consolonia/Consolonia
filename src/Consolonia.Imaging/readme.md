# Consolonia.Imaging

Real pixels for pictures in Consolonia apps, on terminals that support the
[kitty graphics protocol](https://sw.kovidgoyal.net/kitty/graphics-protocol/) or sixel.

Without this package, Consolonia draws an `Image` as block glyphs: each cell shows a quarter-block
character in two of the picture's colours. With it, terminals that can show images get the picture
itself:

- **Kitty graphics** (kitty, WezTerm, Ghostty, Konsole and others): pictures are placed below the
  text, so text, borders and the mouse cursor show over them.
- **Sixel** (Windows Terminal, foot, mlterm, xterm with sixel and others): each cell is either
  picture or text; a glyph over a picture sits on the picture's colour.

Which one is used is detected when the console starts. Terminals with neither keep block glyphs.

## Getting started

Enable it in your app startup, anywhere in the builder chain:

```csharp
using Avalonia;
using Consolonia;

public static class Program
{
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithConsoleLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UseConsolonia()
                  .UseAutoDetectedConsole()
                  .UseConsoloniaImaging(); // <= pictures as pixels where the terminal can show them
}
```

## Choosing the protocol

Detection can be overridden with the `CONSOLONIA_GRAPHICS` environment variable, for terminals that
show a protocol without answering its query, or to force the fallback:

| Value | Effect |
|---|---|
| `kitty` | Use kitty graphics. |
| `sixel` | Use sixel, even if the terminal also has kitty graphics. |
| `quad` | Use block glyphs. |
