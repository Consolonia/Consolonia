# Bitmaps: how Consolonia draws pictures in a terminal

Architecture notes for the bitmap pipeline shared by the three renderers: kitty, sixel and quad
pixels. Each renderer has its own notes: [kittybitmap.md](kittybitmap.md),
[sixelbitmap.md](sixelbitmap.md) and [quadbitmap.md](quadbitmap.md).

## The pipeline at a glance

```
Avalonia Image / DrawingContext.DrawImage
  └─ DrawingContextImpl.DrawBitmap                     (DrawingContextImpl.Bitmaps.cs)
       └─ BitmapRenderer.Draw                          (one renderer per drawing context)
            ├─ KittyBitmapRenderer   ─┐
            ├─ SixelBitmapRenderer   ─┤ CellBitmapRenderer<TRendering>: one CellRendering per picture and size
            └─ QuadPixelBitmapRenderer  (no rendering kept: blends quad glyphs straight into the buffer)
                 │
                 ▼
            PixelBuffer cells (a sixel in the foreground, or a KittyTile in the background)
                 │  dirty regions: only cells whose Pixel changed
                 ▼
            RenderTarget.RenderToDevice                (RenderTarget.cs)
              pass 1  RenderSixelRegions        sixel cells → combined rectangles → WriteSixel
              pass 2  text cells                → WritePixel (kitty tile cells are written as blank cells)
              then    EmitKittyRectPlacements   kitty tiles → placements, washes, orphan deletes
                 │
                 ▼
            AnsiConsoleOutput frame buffer (UTF-8 bytes) → one write to stdout per frame
```

Pictures are drawn into the same `PixelBuffer` as text. This means layering, occlusion, dirty
tracking and diffing work for pictures as they do for text. Only the last step, turning cells into
escape sequences, knows about image protocols.

## Choosing a renderer

`DrawingContextImpl.CreateBitmapRenderer` picks the best renderer the terminal supports. It decides
once per drawing context:

1. **Kitty** when the console reports `SupportsKittyGraphics` and the colour mode is RGB.
2. **Sixel** when it reports `SupportsSixel`.
3. **Quad pixels** otherwise. Each cell shows 2×2 pixels as a block glyph with a foreground and a
   background colour. See [quadbitmap.md](quadbitmap.md).

`AnsiConsoleOutput.PrepareConsole` detects these capabilities:

- Kitty: a kitty query (`a=q`).
- Sixel: the DA1 reply.
- Synchronized output: a DEC 2026 mode request.

A Device Attributes request follows the queries as a fence.

`CONSOLONIA_GRAPHICS=kitty|sixel|quad` overrides detection (`ApplyGraphicsProtocolOverride`).

## Getting the visible pixels

`BitmapRenderer.GetVisiblePixels` is the only place a picture is read and scaled.

- **Only the on-screen part is scaled.** The renderer takes the source pixels behind the visible
  rectangle, widens them to whole source pixels, scales that piece with the requested
  `BitmapInterpolationMode`, and cuts it back to the visible rectangle. A picture zoomed far past the
  screen is never scaled whole. That would be slow. For the image protocols it would also produce an
  image the terminal refuses: XTerm.NET stops at four megapixels.
- **No scaling at native size.** When the picture is drawn at its own size and is already BGRA, the
  visible window is a plain copy.
- **The screen bounds the size, not the clip.** `OnScreenCellsInTarget` uses the screen, not the
  redraw clip. Rendering by the clip made a separate rendering for each partial redraw, such as a
  dialog opening over the picture. When the cache evicted one of those, the kitty images under it
  went blank.
- **Size from the framebuffer.** Sizes are measured from the locked framebuffer, not `PixelSize`.
  `AspectRatioAdjustedBitmap` reports half its height to layout.
- **The caller owns the destination.** The destination array is passed in, so a rendering refilled
  on every brush stroke reuses one buffer. A full HD screen of BGRA is 8 MB.
- The interpolation mode comes from the innermost `RenderOptions.BitmapInterpolationMode`, or
  `MediumQuality` when none is set. Pixel-art pictures can stay hard-edged when enlarged.

## One rendering per picture and size

`CellBitmapRenderer<TRendering>` (`BitmapRenderer.cs`) is the shared base of the kitty and sixel
renderers.

- **Key.** `RenderingKey(TargetSize, InterpolationMode)`. The picture's version and the visible part
  are deliberately not part of the key.
- **Storage.** Renderings live in a `ConditionalWeakTable<IBitmapImpl, …>` per renderer type, so they
  die with the bitmap. A bitmap keeps at most four renderings (`MaxRenderingsPerBitmap`), one per
  zoom level. The least recently drawn is dropped first (`FindOrAdd`).
- **Refill in place.** A `CellRendering` holds `Pixels` (BGRA of exactly the visible cells), `Cells`
  (a `PixelBuffer` of one cell per visible cell), `Version` and `VisibleCells`. When the version or
  the visible part changes, the rendering is refilled in place. Its buffers are replaced only when
  their size changes. An edit or a scroll therefore allocates nothing the size of the screen.
- **Shows nothing until complete.** `Prepare` marks the rendering as showing nothing until `Complete`.
  A render that throws leaves a rendering that is made again, not one claiming to show the previous
  version.
- **Reuse check.** `IsReusable` lets a renderer refuse a rendering that would otherwise be reused.
  Kitty uses it to redo a rendering when one of its tile images has been evicted.
- **Only changed cells are copied.** `CopyRenderedBitmapTrackingDirtyRegions` compares each rendered
  cell with the pixel buffer and marks dirty only cells that changed, in horizontal runs. A redraw
  that changes nothing marks nothing.

The quad-pixel renderer has no rendering to keep. It scales just the intersected cells at two
pixels per cell each way and blends a glyph per cell straight into the buffer.

## Content-addressed caches

Both image protocols cache what they made from pixels by **what the pixels show**, not by where
they came from (`ContentCache.cs`):

- **`ContentKey`.** An XXH3-128 hash of a block's BGRA bytes plus its size. 128 bits never collide
  in practice, and nothing needs keeping to compare against. Hashing every visible cell is the main
  cost of a changed frame. XXH3 replaced SHA-256 and made it about 10× cheaper.
- **`ContentCache<T>`.** A least-recently-used cache with a cost budget. `GetOrAdd` returns the value
  already held, so every cell showing the same block shares one instance and compares equal. An
  eviction callback lets kitty delete the image from the terminal later.

A blank canvas is one block repeated. With content keys it is one sixel or one kitty image, not one
per cell, and the first edit changes only the cells it touched.

## Writing a frame

`RenderTarget.RenderToDevice` turns the dirty cells into output:

1. **Sixel pass** (`RenderSixelRegions`). It joins dirty sixel cells that share a palette into
   rectangles and writes one sixel for each. See [sixelbitmap.md](sixelbitmap.md).
2. **Text pass.** It writes the remaining dirty cells that differ from what the terminal shows (the
   `_cache` of written pixels). It also detects whether any kitty tile cell changed.
3. **Kitty placements** (`EmitKittyRectPlacements`). This runs only when a tile cell changed or an
   evicted image is waiting. See [kittybitmap.md](kittybitmap.md).
4. **Caret, then `Flush`.** The whole frame, caret included, goes out in one write. A DEC 2026
   synchronized update covers all of it.

### The byte frame (`AnsiConsoleOutput`)

The frame is built as the bytes the terminal receives:

- **Buffer.** `_frame` is a pooled `byte[]` with 8 bytes reserved in front for the DEC 2026 begin
  marker. Starting at 64 KB, it grows by doubling. A buffer grown past 1 MB (a full-screen kitty
  picture is megabytes) goes back to the pool after the write.
- **Text.** Text is encoded as UTF-8 on the way in. `Ascii.FromUtf16` takes ASCII, which is almost
  everything, in one vectorised pass, and `WriteChar` stores ASCII directly. A stateful `Encoder`
  handles the rest. It keeps a surrogate pair split across two writes together. `EndText` writes a
  lone high surrogate as U+FFFD before any image bytes.
- **Image payloads.** These are ASCII, so they go in as they are. `IConsoleOutput.WriteBytes` is a
  default interface method that falls back to `WriteText`, so other outputs keep working.
  `AnsiConsoleOutput` overrides it. `WriteSixel` appends `Sixel.Render()`. Kitty transmits are
  composed as bytes (`KittyGraphics.WriteTransmitSequence`).
- **Flush.** `Flush` fills in the begin marker and appends the end marker when the terminal supports
  synchronized output. It flushes `Console.Out`, so text written there directly goes first, then
  writes the frame to stdout in **one** `Stream.Write`.
- **Redirected output.** `PrepareConsole` installs the writer over the stdout stream. If
  `Console.Out` was replaced since then (a test, or a host capturing output), the frame is decoded
  and written to that writer instead.

Before this change, the frame was a `StringBuilder`. Image payloads were widened to UTF-16 and
encoded back to UTF-8 by the `StreamWriter`. A full-screen kitty picture cost about 22 MB of
garbage per frame, with gen-1 collections.

### Lifecycle: resize, resume, exit

- **Resize.** A size change resets the written-pixel cache. Every kitty placement is then placed
  again from the cells, and the old placements are deleted at the end of the frame.
- **Resume after a suspend.** `ConsoleWindowImpl.TerminalContentsLost` is raised when another
  program had the terminal (`ConsoloniaLifetime.DisconnectFromConsoleAsync`).
  `RenderTarget.ForgetTerminalContents` resets the cache, forgets placements and marks the whole
  screen dirty. Kitty images are kept and only placed again, so the picture does not flash.
- **Exit.** `RestoreConsole` deletes every kitty image still in the terminal, then the visible
  placements.

## Benchmarks

`src/Consolonia.Benchmarks`, BenchmarkDotNet with `[MemoryDiagnoser]`:

```
dotnet run -c Release --project src/Consolonia.Benchmarks -- --filter "*SixelFrame*" --job short
dotnet run -c Release --project src/Consolonia.Benchmarks -- --filter "*ConsoleOutput*" -i
dotnet run -c Release --project src/Consolonia.Benchmarks -- --digest
```

| Class | What it measures |
|---|---|
| `SixelFrameBenchmarks` | The sixel renderer's per-frame work for a full-screen picture (240×67 cells of 8×16, 1920×1072): unchanged, one cell changed, one cell changed including the copy out of the bitmap, every cell new. |
| `SixelBenchmarks` | Quantising (flat cell, photo cell, full image) and serialising sixels. |
| `ConsoleOutputBenchmarks` | Getting a frame out through `AnsiConsoleOutput` to a null stream: a screen of text, full-screen and one-cell sixels, a full-screen kitty transmit (510 tiles of 8 KB) and one tile. The terminal's side is not measured. |

`--digest` prints the SHA-256 of the bytes each `ConsoleOutputBenchmarks` scenario writes. A change
to the output path should leave the digests unchanged:

| Scenario | Bytes |
|---|---|
| TextFullScreen | 86,965 |
| SixelFullScreen | 552,336 |
| SixelOneCell | 3,036 |
| KittyFullScreen | 5,595,102 |
| KittyOneTile | 10,969 |

### Results on the way here

The tables below were measured on the developer machine, from 2026-10-09 to 2026-10-10. Read them
as ratios, not absolutes.

**Renderer per frame, full-screen picture** (`SixelFrameBenchmarks`, ShortRun). The first column is
the state before the optimisation work. The last column has one reused rendering per picture and
size.

| Frame | Original | After cell reuse | After rendering reuse |
|---|---|---|---|
| Nothing changed | 17.2 ms / 1.2 MB | 4.8 ms / 1.2 MB | 3.8 ms / 536 B |
| One cell changed (a brush stroke) | 79.6 ms / 3.3 MB | 4.9 ms / 1.1 MB | 4.2 ms / 4 KB |
| One cell changed, from the bitmap | — | ~9.5 MB | 5.0 ms / 568 B |
| Every cell new (new picture, scroll) | 146 ms / 8.5 MB | 52.6 ms / 14.3 MB | ~60 ms / 13.9 MB (noisy) |

Where the time went, from most to least saved:

- Quantising only the cells that changed.
- An exact palette path for cells with 256 colours or fewer.
- Defining only the palette colours a cell uses. One cell's bytes dropped from 4.1 KB to under 1 KB.
- Not re-sending sixel cells the terminal already shows.
- XXH3 instead of SHA-256.
- Reusing per-frame scratch buffers.

The every-cell-new frame allocates more than the original because of the bookkeeping that makes
identical cells share one sixel. It happens when a picture loads or scrolls, not on edits.

**Output path per frame** (`ConsoleOutputBenchmarks`, default job, in-process, null stream). Before
and after the byte frame:

| Scenario | Before | After | Speed-up | Allocated before | Allocated after |
|---|---|---|---|---|---|
| Screen of text | 2.08 ms | 2.05 ms | same | 332 KB | 332 KB |
| Full-screen sixel | 293 µs | 17.5 µs | 17× | 1.1 KB | 80 B |
| One-cell sixel | 714 ns | 298 ns | 2.4× | 80 B | 80 B |
| Full-screen kitty transmit | 5.22 ms | 1.38 ms | 3.8× | 22.6 MB (gen 0 and 1) | 32 KB (no GC) |
| One kitty tile | 7.08 µs | 0.81 µs | 8.8× | 43 KB | 0 B |

The output bytes are identical before and after in all five scenarios. The text path costs the
same because the ASCII fast path matches the old buffered writer. A full-screen kitty transmit
still allocates about 64 B per tile, from a source not yet identified.

## Tests

In `src/Tests/Consolonia.Core.Tests`:

| Tests | Cover |
|---|---|
| `BitmapRendererTests` | The visible pixels of a picture at its own size, measured by the framebuffer rather than the layout size. |
| `CellRenderingTests` | Buffer reuse across versions, reallocation on a size change, showing nothing until complete, least-recently-drawn eviction. |
| `SixelRenderCellsTests`, `SixelPaletteTests`, `WithLifetimeFixture/SixelBitmapRendererTests` | The sixel side. |
| `KittyGraphicsTests`, `KittyImageLifetimeTests`, `WithLifetimeFixture/KittyBitmapRendererTests` | The kitty side. |
| `WithLifetimeFixture/RenderTargetTests` | After the terminal's contents are lost, every cell is written again. |
| `AnsiConsoleOutputWriteTests` | The byte frame: order of text and bytes, UTF-8, split and lone surrogates, text written to `Console.Out` first, the redirected-`Console.Out` fallback, kitty bytes matching the string builder, an empty frame. |
| `WithLifetimeFixture/PixelTests` | Blending over sixel cells (palette wash, dominant colour, block glyphs) and kitty tile backgrounds. |
