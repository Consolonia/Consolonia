# Bitmaps: how Consolonia draws pictures in a terminal

Architecture notes for the bitmap pipeline. A picture becomes cells of the pixel buffer like any
other drawing; which image protocol shows them, and how, is the console output's business. Each
protocol has its own notes: [kittybitmap.md](kittybitmap.md), [sixelbitmap.md](sixelbitmap.md) and
[quadbitmap.md](quadbitmap.md).

## The pipeline at a glance

```
Avalonia Image / DrawingContext.DrawImage
  └─ DrawingContextImpl.DrawBitmap                     (DrawingContextImpl.Bitmaps.cs)
       └─ BitmapRenderer.Draw                          (one renderer per terminal, owned by RenderTarget)
            ├─ CellImageBitmapRenderer   CellBitmapRenderer<CellRendering>: one CellRendering per picture and size,
            │                            each cell a CellImage (its pixels) in the cell's BACKGROUND
            └─ QuadPixelBitmapRenderer   no image protocol: a block glyph in two colours per cell
                 │
                 ▼
            PixelBuffer cells: PixelBackground(paint, CellImage) + a free foreground
                 │  dirty regions: only cells whose Pixel changed
                 ▼
            RenderTarget.RenderToDevice                (RenderTarget.cs: a cell is a cell)
              BeginFrame ─ every changed cell → WritePixel ─ EndFrame(screen)
                 │
                 ▼
            AnsiConsoleOutput                          (the only place that knows sixel from kitty)
              text cells → SGR runs; image cells → IConsoleImageEncoder
                 ├─ SixelConsoleImageEncoder: changed cells gathered, one sixel per run at EndFrame
                 └─ KittyConsoleImageEncoder: cells on the default background, changed blocks re-sent under their ids at EndFrame
                 │
                 ▼
            frame buffer (UTF-8 bytes) → one write to stdout per frame
```

Pictures are drawn into the same `PixelBuffer` as text. This means layering, occlusion, dirty
tracking and diffing work for pictures as they do for text. The pixel model knows one thing about
pictures: a cell's background can be a `CellImage` under a paint colour. Everything that knows the
word sixel or kitty is in `Consolonia.Core.Infrastructure`, behind `AnsiConsoleOutput`.

## The pixel model

`PixelBufferImplementation/`

- **`CellImage`.** The pixels of one cell of a picture: a cell-sized BGRA copy, its `ContentKey`
  (XXH3-128 of the bytes) and a lazily computed `DominantColor`. Compared by reference. The renderer
  keeps one instance per distinct content, so two cells showing the same pixels share one instance
  and compare equal, and a cell whose pixels did not change keeps its instance and is not written
  again. It is a copy because the cell's identity has to be its content: a picture is edited in
  place and scaled to the cells it is drawn at, so nothing else holds these pixels unchanged.
- **`PixelBackground`** is a `Color` and an optional `Image`. For an image cell the colour is the
  **paint** laid over the picture: transparent for none. Translucent overlays (a modal's backdrop, a
  shade) accumulate into it through the same `Blend` as a plain cell's colour. The output applies
  the paint to the picture when the cell is written. `EffectiveColor` is what the cell looks: the
  colour, or for an image cell the paint over the picture's dominant colour, for the few places that
  need one colour (the mouse cursor's contrast, a glyph a protocol cannot draw over an image, the
  fallback with no protocol, `Invert`).
- **`Pixel.Blend`** has one rule for pictures: an image cell above takes the cell whole (its own
  colours are transparent and would otherwise read as nothing drawn). Everything else is the general
  rule: an opaque background evicts the image through the opaque fast path, a translucent one merges
  into the paint and keeps the image, a glyph lands in the foreground with the image below it. What
  a glyph over an image looks like on a terminal whose protocol cannot draw text over a picture is
  decided at the output, not here. `Shade` and `Brighten` lay a 25 % black or white paint over an
  image cell, since shifting a transparent colour by a step would leave it as it is.
- **Size.** A `Pixel` is 56 bytes, within one cache line, and the nullable terminal cache entry is
  exactly 64. `PixelForeground` stores its three optional attributes (`FontWeight?`, `FontStyle?`,
  `TextDecorationLocation?`) packed in 6 bytes rather than three nullable enums of 8 each; the
  properties are unchanged. `PixelTests.PixelIsFiftySixBytes` keeps it so.

## Choosing a renderer

`BitmapRenderer.Create` picks the renderer for the console. The render target makes one per terminal,
and the drawing contexts (replaced on every resize) borrow it:

1. **Cell images** when the console reports `SupportsKittyGraphics` or `SupportsSixel`.
2. **Quad pixels** otherwise. Each cell shows 2×2 pixels as a block glyph with a foreground and a
   background colour. See [quadbitmap.md](quadbitmap.md). Its output is a text cell, which every
   console can show and the snapshot tests can compare, so it stays a renderer rather than an
   encoder.

`AnsiConsoleOutput.PrepareConsole` detects the capabilities and chooses its encoder:

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

`CellBitmapRenderer<TRendering>` (`BitmapRenderer.cs`) is the base of `CellImageBitmapRenderer`.

- **Key.** `RenderingKey(TargetSize, InterpolationMode)`. The picture's version and the visible part
  are deliberately not part of the key.
- **Storage.** Renderings live in a `ConditionalWeakTable<IBitmapImpl, …>`, so they die with the
  bitmap. A bitmap keeps at most four renderings (`MaxRenderingsPerBitmap`), one per zoom level. The
  least recently drawn is dropped first (`FindOrAdd`).
- **Refill in place.** A `CellRendering` holds `Pixels` (BGRA of exactly the visible cells), `Cells`
  (a `PixelBuffer` of one cell per visible cell), `Version` and `VisibleCells`. When the version or
  the visible part changes, the rendering is refilled in place. Its buffers are replaced only when
  their size changes. An edit or a scroll therefore allocates nothing the size of the screen.
- **Shows nothing until complete.** `Prepare` marks the rendering as showing nothing until `Complete`.
  A render that throws leaves a rendering that is made again, not one claiming to show the previous
  version.
- **Copied like any drawing.** `CopyRenderedBitmap` blends each rendered cell into the pixel buffer
  (`Pixel.Blend` lets an image cell take the cell) and marks the whole drawn rectangle dirty. The
  render target then skips every dirty cell that matches what it last wrote, so a redraw that
  changes nothing writes nothing to the terminal.

The quad-pixel renderer has no rendering to keep. It scales just the intersected cells at two
pixels per cell each way and blends a glyph per cell straight into the buffer.

## Content-addressed caches

Everything made from pixels is cached by **what the pixels show**, not by where they came from
(`ContentCache.cs`):

- **`ContentKey`.** An XXH3-128 hash of a block's BGRA bytes plus its size. 128 bits never collide
  in practice, and nothing needs keeping to compare against. Hashing every visible cell is the main
  cost of a changed frame. XXH3 replaced SHA-256 and made it about 10× cheaper.
- **`ContentCache<T>`.** A least-recently-used cache with a cost budget. `GetOrAdd` returns the value
  already held, so every cell showing the same block shares one instance and compares equal.

Two caches use them: the renderer's `CellImage`s (three screens of pixels; eviction only ends
deduplication, a cell on screen holds its image by reference) and the sixel encoder's rendered runs
(16 MB of bytes). The kitty encoder keeps no cache: it sends a block when its key changes and
remembers only the key it sent (see [kittybitmap.md](kittybitmap.md)).

A blank canvas is one block repeated. With content keys it is one `CellImage` and one sixel run per
row, not one per cell, and the first edit changes only the cells it touched.

## Writing a frame

`RenderTarget.RenderToDevice` turns the dirty cells into output. It knows nothing about image
protocols: a cell is a cell.

1. **`BeginFrame`.** The console is told a frame starts.
2. **Cells.** Every dirty cell that differs from what the terminal shows (the `_cache` of written
   pixels) goes through `IConsoleOutput.WritePixel`. The cell under the mouse cursor gets the cursor
   glyph in its foreground; its contrast colour comes from `PixelBackground.EffectiveColor`, so over
   a picture it is computed against the paint over the dominant colour.
3. **`EndFrame(screen)`.** Every changed cell has been written; the console gets the whole buffer,
   which is what the terminal should now show.
4. **Caret, then `Flush`.** The whole frame, caret included, goes out in one write. A DEC 2026
   synchronized update covers all of it.

`BeginFrame`, `EndFrame` and `ResetCache` are default-implemented members of `IConsoleOutput`
(`ConsoleBase` forwards them), so an output with no per-frame work ignores them. `AnsiConsoleOutput`
forwards them to its image encoder.

### The output decides what an image cell is (`AnsiConsoleOutput`)

`WritePixel` writes a plain cell as a glyph in colours (`WriteTextCell`: SGR state diffed against
the last cell, consecutive cells with no cursor move between them). An image cell goes to the
console's `IConsoleImageEncoder`:

| | `WriteCell` (per changed cell) | `EndFrame` (once per frame) |
|---|---|---|
| **Sixel** | A cell with a glyph is written as text on `EffectiveColor`: a terminal shows a sixel or text in a cell, never both. A cell without one is recorded. | The recorded cells, in row order, form runs of neighbours; each run is one sixel: pixels side by side, the paint composited in, one palette, encoded. Rendered runs are cached by content. |
| **Kitty** | Written as text on the terminal's default background (Konsole hides below-text placements under any explicit cell background), glyph or blank. | The screen is walked in 8×4-cell blocks. A block whose key (its cells' image digests and paint) changed since it was last sent is transmitted again under the block's own image id, which replaces the image and its placements in the terminal, and its image cells are placed as crops. A block with no picture left has its image deleted. |
| **None** (a console that reports no image protocol, given an image cell anyway) | The cell as a block glyph in the picture's colours under its paint (`QuadPixelBitmapRenderer.FromPixels`), its own glyph kept. | Nothing. |

Why the per-cell call is needed: the dirty rectangles say which cells were redrawn, but only the
render target's compare knows which cells *changed*, and it says so by calling `WritePixel`. The
sixel encoder learns its changed cells from that call; kitty compares every block of the screen
with what it last sent, dirty or not, because a block's key also changes when a cell under it
stops or starts showing a picture.

`ResetCache` forgets what the output believes the terminal shows: the last colours and attributes
set (another program may have changed them) and the encoder's placements.

### The byte frame

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
  `AnsiConsoleOutput` overrides it. `WriteImageBytes` appends a sixel run after an explicit cursor
  move and moves the cursor past it. Kitty transmits are composed as bytes
  (`KittyGraphics.WriteTransmitSequence`).
- **Flush.** `Flush` fills in the begin marker and appends the end marker when the terminal supports
  synchronized output. It flushes `Console.Out`, so text written there directly goes first, then
  writes the frame to stdout in **one** `Stream.Write`.
- **Diagnostics.** `CONSOLONIA_DEBUG_FLUSH=<file>` logs each flushed frame's size and kitty
  transmit count; `CONSOLONIA_DEBUG_FRAMES=<file>` appends every frame's bytes as sent, to read
  back what a terminal was given.
- **Redirected output.** `PrepareConsole` installs the writer over the stdout stream. If
  `Console.Out` was replaced since then (a test, or a host capturing output), the frame is decoded
  and written to that writer instead. `RedirectOutput(stream, capabilities)` sets the same up
  without a terminal, with the encoder for the capabilities given, for tests and benchmarks.

Before this change, the frame was a `StringBuilder`. Image payloads were widened to UTF-16 and
encoded back to UTF-8 by the `StreamWriter`. A full-screen kitty picture cost about 22 MB of
garbage per frame, with gen-1 collections.

### Lifecycle: resize, resume, exit

- **Resize.** A size change calls `RenderTarget.ResetCache`: the written-pixel cache is rebuilt and
  the console's `ResetCache` runs. Kitty then sends every block again under its id, which replaces
  what the terminal still holds for it.
- **Resume after a suspend.** `ConsoleWindowImpl.TerminalContentsLost` is raised when another
  program had the terminal (`ConsoloniaLifetime.DisconnectFromConsoleAsync`). It calls the same
  `RenderTarget.ResetCache`, which also marks the whole screen dirty. A kitty block is replaced in
  one transmit rather than deleted and re-sent, so where the terminal still showed it nothing flashes.
- **Exit.** `RestoreConsole` deletes every kitty image still in the terminal, then the visible
  placements.

## Benchmarks

`src/Consolonia.Benchmarks`, BenchmarkDotNet with `[MemoryDiagnoser]`:

```
dotnet run -c Release --project src/Consolonia.Benchmarks -- --filter "*CellImageFrame*" --job short
dotnet run -c Release --project src/Consolonia.Benchmarks -- --filter "*ConsoleOutput*"
dotnet run -c Release --project src/Consolonia.Benchmarks -- --filter "*BitmapFrame*"
dotnet run -c Release --project src/Consolonia.Benchmarks -- --digest
```

| Class | What it measures |
|---|---|
| `CellImageFrameBenchmarks` | The renderer's per-frame work for a full-screen picture (240×67 cells of 8×16, 1920×1072): unchanged, one cell changed, one cell changed including the copy out of the bitmap, every cell new. Nothing reaches a terminal. |
| `ConsoleOutputBenchmarks` | Getting a frame out through `AnsiConsoleOutput` to a null stream, as the render target drives it (changed cells written, then `EndFrame`): a screen of text; for sixel a new full-screen picture, the picture shown again after a dialog dimmed it (the run cache), one cell; for kitty a new full-screen picture, every block sent again after a reset, one cell. The terminal's side is not measured. |
| `BitmapFrameBenchmarks` | A whole frame (draw into the pixel buffer, then `RenderToDevice`) of a full-screen picture on the unit test console, for the sixel and kitty capabilities: redrawn unchanged, one pixel painted, a new picture. The unit test console keeps no output, so this is drawing and diffing; the encoders are measured by `ConsoleOutputBenchmarks`. |
| `SixelBenchmarks` | Quantising (flat cell, photo cell, full image) and serialising sixels. |

`--digest` prints the SHA-256 of the bytes each `ConsoleOutputBenchmarks` scenario writes. A change
to the output path should leave the digests unchanged:

| Scenario | Bytes |
|---|---|
| TextFullScreen | 86,966 |
| SixelNewPicture | 629,322 |
| SixelDialogClosed | 625,332 |
| SixelOneCell | 3,033 |
| KittyNewPicture | 3,237,858 |
| KittyAfterReset | 3,232,625 |
| KittyOneCell | 12,635 |

### Results

Measured on the developer machine; read them as ratios, not absolutes. Noise is about ±5 ms on the
whole-frame numbers between runs.

**Output path per frame** (`ConsoleOutputBenchmarks`, default job, in-process, null stream), with
the encoders in the output:

| Scenario | Time | Allocated | Bytes on the wire |
|---|---|---|---|
| Screen of text | 2.0 ms | 514 KB | 86,966 |
| Sixel, new full-screen picture (a run per row, quantised and encoded) | 101 ms | 8.2 MB | 629,322 |
| Sixel, dialog closed (every run from the cache) | 1.5 ms | 1.3 MB | 625,332 |
| Sixel, one cell | 1.9 µs | 6 KB | 3,033 |
| Kitty, new full-screen picture (every block transmitted and placed) | 82 ms | 13.4 MB | 3,237,858 |
| Kitty, every block sent again after a reset | 81 ms | 6.5 MB | 3,232,625 |
| Kitty, one cell | 42 µs | 40 B | 12,635 |

**Whole frame per protocol** (`BitmapFrameBenchmarks`, default job, unit test console). The first
three columns are the states before this model (the image renderers comparing cells themselves;
copying like every other drawing; the frame work in a render target hook, with the sixel join gone).
The last is the image-as-background model: one renderer, the encoders in the output, nothing encoded
on this console.

| Frame | Per-cell compare in the renderer | Blend + one dirty rect | Hooks | Cell images |
|---|---|---|---|---|
| Sixel, redrawn unchanged | 1.1 ms | 3.6 ms | 4.0 ms | 1.2 ms |
| Sixel, brush stroke | 12.0 ms | 9.4 ms | 10.0 ms | 6.5 ms |
| Sixel, new picture | — | — | 111 ms | 26 ms |
| Kitty, redrawn unchanged | 1.6 ms | 5.6 ms | 5.8 ms | 1.1 ms |
| Kitty, brush stroke | 9.0 ms | 8.7 ms | 8.3 ms | 6.5 ms |
| Kitty, new picture | — | — | 10.9 ms | 26 ms |

The two capabilities now share the renderer, so their columns measure the same work; the
difference between them is noise. Quantising moved out of the draw and into the sixel encoder, so a
new sixel picture no longer costs 111 ms here: that cost is in `ConsoleOutputBenchmarks` above,
where a new full-screen sixel picture is about 100 ms (the runs of a frame quantised together; done
per run it was 1.6 s, Wu's quantiser having a large fixed cost per call) and a new kitty picture
about 80 ms, almost all zlib and base64 of 8 MB of RGBA (the benchmark has no PNG encoder;
in an app the host registers one and the blocks go as PNG, about half the bytes). The bytes on the wire for a new sixel picture are 629 KB, where the
old joined whole-picture sixel was 552 KB and the unjoined per-cell form about 16 MB.

**Renderer per frame** (`CellImageFrameBenchmarks`, ShortRun), with the earlier sixel renderer's
numbers for comparison (it quantised the new cells; this one copies them):

| Frame | Sixel renderer, after rendering reuse | Cell images |
|---|---|---|
| Nothing changed | 3.8 ms / 536 B | 4.6 ms / 536 B |
| One cell changed (a brush stroke) | 4.2 ms / 4 KB | 4.3 ms / 1.2 KB |
| One cell changed, from the bitmap | 5.0 ms / 568 B | 6.3 ms / 1.3 KB |
| Every cell new (new picture, scroll) | ~60 ms / 13.9 MB | 28 ms / 11.3 MB |

The remaining per-frame cost of an unchanged or one-cell frame is hashing every visible cell. The
unchanged redraw of the whole frame got slower when the renderers stopped comparing cells
themselves: every dirty cell goes through the render target's compare with its `_cache`, as every
other drawing does. That compare (a 56-byte `Pixel` by value, a nullable cache) is the general cost
to bring down next.

## Tests

In `src/Tests/Consolonia.Core.Tests`:

| Tests | Cover |
|---|---|
| `BitmapRendererTests` | The visible pixels of a picture at its own size, measured by the framebuffer rather than the layout size. |
| `CellRenderingTests` | Buffer reuse across versions, reallocation on a size change, showing nothing until complete, least-recently-drawn eviction. |
| `CellImageRenderCellsTests`, `WithLifetimeFixture/CellImageBitmapRendererTests` | The renderer: an edited cell is the only one made again, identical cells share one image, a cell keeps its exact pixels; drawing at target size, over a picture, clipped, partially redrawn, edited, at a new version. The latter runs once per capability to show the renderer is the same. |
| `WithLifetimeFixture/AnsiConsoleOutputImageTests` | The encoders: sixel neighbours are one sixel and cells apart are not, a glyph over a picture is text on its colour, runs come from the cache; a kitty block is one image and one placement on the default background, a stroke replaces the block it touched, paint is a wash placement, a reset places everything again; without a protocol a picture is block glyphs. |
| `SixelPaletteTests` | Only used colours are defined, a transient image renders the same bytes without keeping them, too-small arrays are refused, exact versus Wu quantisation, rounded percentages. |
| `KittyGraphicsTests`, `KittyImageLifetimeTests` | The kitty sequences, ids and registry; orphaned images and the cache budget. |
| `WithLifetimeFixture/RenderTargetTests` | After the terminal's contents are lost, every cell is written again. |
| `AnsiConsoleOutputWriteTests` | The byte frame: order of text and bytes, UTF-8, split and lone surrogates, text written to `Console.Out` first, the redirected-`Console.Out` fallback, kitty bytes matching the string builder, an empty frame. |
| `WithLifetimeFixture/PixelTests` | A `Pixel` is 56 bytes. Image cells compare by image; an image background composes with a glyph, keeps the image under paint, dies by an opaque background, takes the cell when drawn over text; shade and brighten paint it; the effective colour. |
