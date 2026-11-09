# Bitmaps: how Consolonia draws pictures in a terminal

Architecture notes for the bitmap pipeline. A picture becomes cells of the pixel buffer like any
other drawing; which image protocol shows them, and how, is the console output's business. Image
protocols (sixel, kitty graphics) are not implemented in core: an encoder library gives the console
an `IConsoleImageEncoderFactory`, and without one pictures are block glyphs
([quadbitmap.md](quadbitmap.md)).

## The pipeline at a glance

```
Avalonia Image / DrawingContext.DrawImage
  └─ DrawingContextImpl.DrawBitmap                     (DrawingContextImpl.Bitmaps.cs)
       └─ BitmapConverter.Draw                          (one converter per terminal, owned by RenderTarget)
            ├─ PixelImageBitmapConverter   CellBitmapConverter<CellConversion>: one CellConversion per picture and size,
            │                            each cell a PixelImage (its pixels) in the cell's BACKGROUND
            └─ QuadPixelBitmapConverter   no image protocol: a block glyph in two colours per cell
                 │
                 ▼
            PixelBuffer cells: PixelBackground(paint, PixelImage) + a free foreground
                 │  dirty regions: only cells whose Pixel changed
                 ▼
            RenderTarget.RenderToDevice                (RenderTarget.cs: a cell is a cell)
              BeginUpdate ─ every changed cell → WritePixel ─ caret ─ EndUpdate(screen)
                 │
                 ▼
            AnsiConsoleOutput                          (probes the terminal, asks the factory for an encoder)
              text cells → SGR runs; image cells → their text, then IConsoleImageEncoder.WriteImages
                                                   (from an encoder library), or block glyphs when there is none
                 │
                 ▼
            Console.Out (64 KB StreamWriter over stdout), flushed at EndUpdate
```

Pictures are drawn into the same `PixelBuffer` as text. This means layering, occlusion, dirty
tracking and diffing work for pictures as they do for text. The pixel model knows one thing about
pictures: a cell's background can be a `PixelImage` under a paint colour. Core knows image protocols
only as capabilities it probes for; what is sent for an image cell is the encoder's.

## The pixel model

`PixelBufferImplementation/`

- **`PixelImage`.** The pixels of one cell of a picture: a cell-sized BGRA copy (`Bgra`, a
  `ReadOnlySpan<byte>`), its 128-bit XXH3 digest and a lazily computed `DominantColor`. Compared by
  reference. The converter keeps one instance per distinct content, so two cells showing the same
  pixels share one instance and compare equal, and a cell whose pixels did not change keeps its
  instance and is not written again. It is a copy because the cell's identity has to be its content:
  a picture is edited in place and scaled to the cells it is drawn at, so nothing else holds these
  pixels unchanged. The copy lives in a *slab* shared with the other new cells of the same conversion
  (64 cells, `CellsPerSlab`), at an offset: a new picture is a few hundred allocations, not one per
  cell. See [Memory](#memory).
- **`PixelBackground`** is a `Color` and an optional `Image`. For an image cell the colour is the
  **paint** laid over the picture: transparent for none. Translucent overlays (a modal's backdrop, a
  shade) accumulate into it through the same `Blend` as a plain cell's colour. The output applies
  the paint to the picture when the cell is written. `Pixel.EffectiveBackgroundColor` is what the cell looks: the
  colour, or for an image cell the paint over the picture's dominant colour, for the few places that
  need one colour (the mouse cursor's contrast, a glyph a protocol cannot draw over an image, the
  fallback with no protocol, `Invert`).
- **A picture cell's foreground is a space** (`PixelImageBitmapConverter.PictureCellForeground`,
  `PixelForeground.Space`), like any cell nothing has been drawn in. A space over a picture (any
  one-cell whitespace, `Symbol.IsWhiteSpace`) shows the picture; a glyph is written as text. So the gap between two words of a
  caption shows the picture, as do gaps that layout never drew. A space over a picture always sits on
  a see-through background: an opaque one replaces the picture when it is blended.
- **`Pixel.Blend`** has one rule for pictures: an image cell above takes the cell whole (its own
  colours are transparent and would otherwise read as nothing drawn). Everything else is the general
  rule: an opaque background evicts the image through the opaque fast path, a translucent one merges
  into the paint and keeps the image, a glyph lands in the foreground with the image below it. What
  a glyph over an image looks like on a terminal whose protocol cannot draw text over a picture is
  decided at the output, not here. `Shade` and `Brighten` lay a 25 % black or white paint over an
  image cell, since shifting a transparent colour by a step would leave it as it is.
- **Size.** A `Pixel` holds `Foreground` and `Background` as readonly struct fields, as it did before
  pictures; the image reference makes it 72 bytes. Packing it smaller is left to a separate change.
  The render target's cache of what the terminal shows is a `PixelBuffer`, initialised to `Pixel.Empty`.

## Choosing a converter

`BitmapConverter.Create` picks the converter for the console. The render target makes one per terminal,
and the drawing contexts (replaced on every resize) borrow it:

1. **Pixel images** when the console reports `SupportsPixelImages`, which `AnsiConsoleOutput` sets
   when the registered factory gave it an encoder.
2. **Quad pixels** otherwise. Each cell shows 2×2 pixels as a block glyph with a foreground and a
   background colour. See [quadbitmap.md](quadbitmap.md). Its output is a text cell, which every
   console can show and the snapshot tests can compare, so it stays a converter rather than an
   encoder.

`AnsiConsoleOutput.PrepareConsole` detects the capabilities, the queries in one round trip:

- `SupportsKittyGraphics`: a kitty query (`Esc.QueryKittyGraphics`, `a=q`).
- `SupportsSixel`: feature 4 of the DA1 reply.
- `SupportsSynchronizedOutput`: a DEC 2026 mode request (DECRQM).

A Device Attributes request follows the queries as a fence. The cell size in pixels is asked for
too. `CONSOLONIA_GRAPHICS=kitty|sixel|quad` overrides detection (`ApplyGraphicsProtocolOverride`).

An encoder library registers its `IConsoleImageEncoderFactory` with Avalonia's locator
(`AppBuilder.With`). Consoles probe the terminal in their constructors, before the builder binds
anything, so `UseConsolonia` has the output look the factory up once the application is set up
(`AfterPlatformServicesSetup`), and `ConsoleBase` takes the output's `SupportsPixelImages` again.
The output asks the factory for an encoder
(`CreateEncoder(IConsoleOutput)`) then, and again whenever the console is prepared. The factory
reads the capabilities and picks the protocol (its choice when the terminal has several) or returns
null. With an encoder the output sets `SupportsPixelImages`; without one it clears it.

## Getting the visible pixels

`BitmapConverter.GetVisiblePixels` is the only place a picture is read and scaled.

- **Only the on-screen part is scaled.** The converter takes the source pixels behind the visible
  rectangle, widens them to whole source pixels, scales that piece with the requested
  `BitmapInterpolationMode`, and cuts it back to the visible rectangle. A picture zoomed far past the
  screen is never scaled whole. That would be slow. For the image protocols it would also produce an
  image the terminal refuses: XTerm.NET stops at four megapixels.
- **No scaling at native size.** When the picture is drawn at its own size and is already BGRA, the
  visible window is a plain copy.
- **The screen bounds the size, not the clip.** `OnScreenCellsInTarget` uses the screen, not the
  redraw clip. Converting by the clip made a separate conversion for each partial redraw, such as a
  dialog opening over the picture.
- **Size from the framebuffer.** Sizes are measured from the locked framebuffer, not `PixelSize`.
  `AspectRatioAdjustedBitmap` reports half its height to layout.
- **The caller owns the destination.** The destination span is passed in, so a converter refilling
  a conversion on every brush stroke reuses one buffer. A full HD screen of BGRA is 8 MB.
- The interpolation mode comes from the innermost `RenderOptions.BitmapInterpolationMode`, or
  `MediumQuality` when none is set. Pixel-art pictures can stay hard-edged when enlarged.

## One conversion per picture and size

`CellBitmapConverter<TConversion>` (`BitmapConverter.cs`) is the base of `PixelImageBitmapConverter`.

- **Key.** `ConversionKey(TargetSize, InterpolationMode)`. The picture's version and the visible part
  are deliberately not part of the key.
- **Storage.** Conversions live in a `ConditionalWeakTable<IBitmapImpl, …>`, so they die with the
  bitmap. A bitmap keeps at most four conversions (`MaxConversionsPerBitmap`), one per zoom level. The
  least recently drawn is dropped first (`FindOrAdd`).
- **Refill in place.** A `CellConversion` holds `Cells` (a `PixelBuffer` of one cell per visible
  cell), `Version` and `VisibleCells`. When the version or the visible part changes, the conversion
  is refilled in place; the cells are replaced only when their size changes. The pixels it is made
  from pass through one buffer the converter keeps (`CellBitmapConverter._pixels`), at most a screen
  of BGRA, rather than a buffer per conversion: a cell copies the pixels it needs, so nothing reads
  them once the cells are made, and a picture at four zoom levels kept four screens of them (32 MB
  at full HD). An edit or a scroll therefore allocates nothing the size of the screen.
- **Shows nothing until complete.** `Prepare` marks the conversion as showing nothing until `Complete`.
  A conversion that throws leaves a conversion that is made again, not one claiming to show the previous
  version.
- **Copied like any drawing.** `CopyConvertedBitmap` blends each converted cell into the pixel buffer
  (`Pixel.Blend` lets an image cell take the cell) and marks the whole drawn rectangle dirty. The
  render target then skips every dirty cell that matches what it last wrote, so a redraw that
  changes nothing writes nothing to the terminal.

The quad-pixel converter has no conversion to keep. It scales just the intersected cells at two
pixels per cell each way and blends a glyph per cell straight into the buffer.

## Pixel images by content

The converter finds a cell's `PixelImage` by **what the pixels show**, not by where they came from
(`PixelImageCache`, internal):

- **The digest.** An XXH3-128 hash of the cell's BGRA bytes, kept as `PixelImage.Digest`, with the
  size. 128 bits never collide in practice, and nothing needs keeping to compare against. Hashing
  every visible cell is the main cost of a changed frame. XXH3 replaced SHA-256 and made it about
  10× cheaper.
- **The cache.** Least recently used first out, beyond three screens of pixel bytes; eviction only
  ends the sharing, a cell on screen holds its image by reference. `GetOrAdd` returns the image
  already held, so every cell showing the same pixels shares one instance and compares equal.

`PixelImage.Digest` and `PixelImage.Paint` are public, so an encoder can describe a cell's picture
and composite its paint; what an encoder caches is its own.

A blank canvas is one block repeated. With content keys it is one `PixelImage`, not one per cell,
and the first edit changes only the cells it touched.

## Writing a frame

**One frame at a time, from a finished drawing.** Each window has a `RenderLock`, held for a drawing
context's whole life (from `CreateDrawingContext` until `Dispose` has written the frame), for every
frame written (`RenderToDevice`, and the mouse cursor's frames from their timer thread), and while the
pixel buffer is replaced on a resize. Without it, a cursor frame went out while Avalonia was half way
through drawing: the page under a resizing dialog drawn, the backdrop and the dialog not yet. Its own
cells were put right by the next frame, but an image encoder, given the pixel buffer, sent the
undimmed pictures it saw there, and the next frame skipped every cell that ended up as before.

`RenderTarget.RenderToDevice` turns the dirty cells into output. It knows nothing about image
protocols: a cell is a cell.

1. **`BeginUpdate`.** The console is told a frame starts. `AnsiConsoleOutput` opens a DEC 2026
   synchronized update here when the terminal has them.
2. **Cells.** Every dirty cell that differs from what the terminal shows (the `_cache` of written
   pixels) goes through `IConsoleOutput.WritePixel`. The cell under the mouse cursor gets the cursor
   glyph in its foreground; its contrast colour comes from `Pixel.EffectiveBackgroundColor`, so over
   a picture it is computed against the paint over the dominant colour.
3. **Caret.** The caret, hidden at the start of the frame, is placed and shown.
4. **`EndUpdate(screen)`.** Every changed cell has been written; the console gets the `_cache`, every
   cell as it was last written, which is what the terminal should now show. Not the pixel buffer: a
   cell is written only when it is dirty, so the buffer shows the terminal only while every drawing
   marks the cells it changes, and the cell under the mouse cursor is written as the cursor's glyph,
   so an encoder sends the picture there again when the cursor moves off. `AnsiConsoleOutput` has its image encoder write its
   pictures, moves the cursor back to the caret, closes the synchronized update and flushes: the whole
   frame, caret included, goes out in one write. `RenderTarget` calls it in a `finally`, and
   `AnsiConsoleOutput` closes the update in one, so an exception never leaves an update open.

`BeginUpdate`, `EndUpdate` and `ResetCache` are default-implemented members of `IConsoleOutput`
(`ConsoleBase` forwards them); the default `EndUpdate` flushes. `AnsiConsoleOutput` has its image
encoder write its pictures in `EndUpdate`, and passes `ResetCache` (as `Reset`) and `RestoreConsole`
on to it.

### The output writes the text, the encoder the pictures (`AnsiConsoleOutput`)

`WritePixel` writes a plain cell as a glyph in colours (SGR state diffed against the last cell,
consecutive cells with no cursor move between them). With no encoder the pixel buffer holds no image
cells: pictures were drawn as block glyphs, which are plain cells.

With an encoder, `WritePixel` writes only the text of an image cell, and the encoder draws every
picture at the end of the frame. The text depends on one fact about the protocol,
`SupportsTextOverlay`: whether it draws pictures below the text (kitty graphics) or in place of the
cells they cover (sixel).

| Image cell | `SupportsTextOverlay` | Without it |
|---|---|---|
| A glyph over the picture | the glyph on the terminal's default background (`ESC[49m`), the picture showing through | the glyph on `EffectiveBackgroundColor`, the paint over the picture's dominant colour |
| A space: nothing drawn over it, or a space drawn | the space on the default background | nothing: the picture covers the cell |

Some terminals hide a picture drawn below the text under any explicit cell background, even black;
the default background is the one every terminal shows it through. A terminal cell has no
translucent background, so the paint over a picture (a modal's backdrop) is composited into the
picture's pixels by the encoder.

`IConsoleImageEncoder` (public, in `Consolonia.Core.Infrastructure`):

| Member | Called | Contract |
|---|---|---|
| `SupportsTextOverlay` | by `WritePixel`, for an image cell | Whether the protocol draws its pictures below the text. |
| `WriteImages(screen)` | by `AnsiConsoleOutput.EndUpdate`, after the cells, before the end marker | `screen` is what the terminal should show: draw, move or remove pictures to match it, sending only what differs from what the encoder sent before. |
| `Reset` | by `ResetCache`, and after `RestoreConsole` | Forget what the encoder sent: the next `WriteImages` sends everything. Writes nothing. |
| `RestoreConsole` (default no-op) | by `AnsiConsoleOutput.RestoreConsole`, before the alternate screen is left | Free what the terminal holds for the encoder (stored images). |

Pictures are drawn after the text. The render target writes only the cells that changed, so a
picture drawn first could cover text in cells it was not rewriting; drawn last, a sixel covers only
cells with nothing drawn over them.

The encoder finds what changed itself, from `screen` and its own record of what it sent (each
picture's `PixelImage.Digest` and paint). It does not depend on the render target's compare, and a
walk of the screen is a pass over memory, small next to writing the frame.

An encoder writes through the plain `IConsoleOutput` it was created for: `SetCaretPosition`,
`WriteText` and `WriteBytes`. Everything lands in the frame being written, in order. Two rules make
`SetCaretPosition` safe for placing a payload at the cursor:

- **After `WriteBytes` the cursor is unknown.** Where a payload leaves the cursor is the payload's
  business (a sixel moves it by an amount that depends on the terminal), so the next
  `SetCaretPosition` writes a move even to the position last set.
- **After a glyph in the last column the cursor waits to wrap.** The output models it at the start
  of the next row, which is where the next character goes, but the terminal's cursor is still on the
  old row until a character resolves the wrap. Text written there needs no move; `SetCaretPosition`
  (an image, the caret) writes one.

`ResetCache` forgets what the output believes the terminal shows: the last colours and attributes
set (another program may have changed them) and, through `Reset`, what the encoder believes.

### Writing the frame

Everything goes through `Console.Out`, the writer `PrepareConsole` installs over stdout (64 KB
buffer, flushed by `EndUpdate` and `Flush` only):

- **Text.** `WriteText` and `WriteChar` write to `Console.Out`; the writer encodes UTF-8, surrogate
  pairs split across writes included.
- **Image payloads.** These are ASCII. `IConsoleOutput.WriteBytes` is a default interface method
  that falls back to `WriteText`, so other outputs keep working. `AnsiConsoleOutput` overrides it to
  widen the bytes to `Console.Out` in 4096-character chunks on the stack, so a payload of megabytes
  never becomes a string. Text and payloads share the one writer, so they reach the terminal in the
  order written.
- **Synchronized output.** When the terminal supports DEC 2026, `BeginUpdate` writes the begin marker
  and `EndUpdate` writes the end marker. The writer may send parts of a large frame before then; the
  terminal applies none of it until the end marker. `RestoreConsole` closes an update left open.
- **Diagnostics.** Each flushed frame's text and image payload sizes go to Avalonia's logger at
  `Verbose` in the `Consolonia.Output` area
  (`.LogToTrace(LogEventLevel.Verbose, LogExtensions.GetAreaName(LogCategory.Output))`).
- **Tests and benchmarks.** `RedirectOutput(stream, capabilities)` installs the same writer over a
  stream, without a terminal, with the capabilities given, and asks the factory for an encoder as
  `PrepareConsole` does. Replacing `Console.Out` otherwise while the app runs is not supported.

A full-screen picture's payloads run to megabytes; built as strings they would be that much garbage
per frame, hence the chunks.

### Lifecycle: resize, resume, exit

- **Resize.** A size change calls `RenderTarget.ResetCache`: the written-pixel cache is rebuilt and
  the console's `ResetCache` runs, which resets the encoder.
- **Resume after a suspend.** `ConsoleWindowImpl.TerminalContentsLost` is raised when another
  program had the terminal (`ConsoloniaLifetime.DisconnectFromConsoleAsync`). It calls the same
  `RenderTarget.ResetCache`, which also marks the whole screen dirty.
- **Exit.** `RestoreConsole` closes an update left open, has the encoder free what the terminal
  holds for it, then leaves the alternate screen.

## Benchmarks

`src/Consolonia.Benchmarks`, BenchmarkDotNet with `[MemoryDiagnoser]`:

```
dotnet run -c Release --project src/Consolonia.Benchmarks -- --filter "*PixelImageFrame*" --job short
dotnet run -c Release --project src/Consolonia.Benchmarks -- --filter "*ConsoleOutput*"
dotnet run -c Release --project src/Consolonia.Benchmarks -- --filter "*BitmapFrame*"
dotnet run -c Release --project src/Consolonia.Benchmarks -- --digest
dotnet run -c Release --project src/Consolonia.Benchmarks -- --alloc
```

| Class | What it measures |
|---|---|
| `PixelImageFrameBenchmarks` | The converter's per-frame work for a full-screen picture (240×67 cells of 8×16, 1920×1072): unchanged, one cell changed, one cell changed including the copy out of the bitmap, every cell new. Nothing reaches a terminal. |
| `ConsoleOutputBenchmarks` | Getting a frame out through `AnsiConsoleOutput` to a null stream, as the render target drives it (`BeginUpdate`, changed cells written, `EndUpdate`): a screen of text. The terminal's side is not measured. |
| `BitmapFrameBenchmarks` | A whole frame (draw into the pixel buffer, then `RenderToDevice`) of a full-screen picture on the unit test console with `SupportsPixelImages`: redrawn unchanged, one pixel painted, a new picture. The unit test console keeps no output and has no encoder, so this is drawing and diffing. |

`--alloc` prints the bytes each `ConsoleOutputBenchmarks` scenario allocates in one run after a
warm-up run, from the thread's allocation counter: exact, quick, and the figure to compare between
two builds.

`--digest` prints the SHA-256 of the bytes each `ConsoleOutputBenchmarks` scenario writes. A change
to the output path should leave the digests unchanged.

The remaining per-frame cost of an unchanged or one-cell frame is hashing every visible cell. Every
dirty cell goes through the render target's compare with its `_cache`, as every other drawing does.

## Memory

What the image model costs in memory, and the choices made to keep it down. A full HD screen is
16,080 cells of 8×16 pixels.

| Where | Per | Bytes | Notes |
|---|---|---|---|
| Pixel buffer | cell, image or not | 72 | `Pixel`; 1.16 MB a screen |
| Render target cache | cell | 72 | `Pixel[,]` |
| `PixelImage` | distinct cell of picture | 56 + 512 + ~130 | the object, its pixels in a slab, the content cache's node, entry and dictionary slot |
| Pixel image cache | terminal | 24 MB min, 3 screens | pixels of distinct cells kept, LRU |
| Converter scratch | terminal | one screen of BGRA | the visible pixels of the picture being converted, 8 MB at full HD, grown never shrunk |
| Output writer | terminal | 64 KB | the `StreamWriter` behind `Console.Out`; image payloads pass through an 8 KB (4096-character) stack chunk |
| Text-only app | | 0 | none of the above but the two per-cell buffers |

An encoder's own memory is the encoder library's.

- **No per-cell allocations on the hot path.** A frame that changes nothing allocates a few hundred
  bytes; a brush stroke about a kilobyte in the converter (one pixel image into the current slab).
- **Slabs.** New pixel images of one conversion are copied into one 32 KB array of 64 cells rather than a
  536-byte array each: a new picture is 250 allocations rather than 16,000, and nothing sits in the
  large object heap. A cell that outlives its slab-mates keeps their 63 cells' bytes alive, which is
  the trade: the cache counts each cell's own bytes, so its budget understates memory only while a
  slab is partly held, and never by more than a slab.
- **Dominant colour without a dictionary.** The colours of the cell are sorted on the stack and the
  longest run wins.
- **Spans at the seams.** `PixelImage.Bgra`, the visible pixels into `ConvertCells` and out of
  `GetVisiblePixels`, and image payloads (`WriteBytes`) are
  `ReadOnlySpan<byte>`/`Span<byte>`: callers pass
  slices of pooled or reused arrays, and nothing is sized to the array it happens to be in.

## Tests

In `src/Tests/Consolonia.Core.Tests`:

| Tests | Cover |
|---|---|
| `BitmapConverterTests` | The visible pixels of a picture at its own size, measured by the framebuffer rather than the layout size. |
| `CellConversionTests` | Cell reuse across versions, reallocation on a size change, showing nothing until complete, least-recently-drawn eviction. |
| `PixelImageConvertCellsTests`, `WithLifetimeFixture/PixelImageBitmapConverterTests` | The converter: an edited cell is the only one made again, identical cells share one image, a cell keeps its exact pixels; drawing at target size, over a picture, clipped, partially redrawn, edited, at a new version, on a console with `SupportsPixelImages`. |
| `WithLifetimeFixture/AnsiConsoleOutputImageTests` | With a recording encoder: the factory is asked with the output and its capabilities; `SupportsPixelImages` is set with an encoder and cleared without; the encoder writes once a frame with the screen; the text over a picture with and without `SupportsTextOverlay` (default background, or the picture's colour; a space, or nothing); after `WriteBytes`, and after the last column, `SetCaretPosition` moves the cursor; the encoder's output is inside the synchronized update; the cursor goes back to the caret after the encoder moves it; an encoder that throws still leaves the update closed; `Reset` and `RestoreConsole` are forwarded, and `Reset` follows `RestoreConsole`. |
| `WithLifetimeFixture/RenderTargetTests` | After the terminal's contents are lost, every cell is written again; a frame ends with the screen as written, not the pixel buffer; a frame from another thread waits for the drawing under way. |
| `AnsiConsoleOutputWriteTests` | Writing the frame: order of text and bytes, UTF-8, split and lone surrogates, text written to `Console.Out` first, a payload longer than a chunk, one synchronized update per frame, an empty frame. |
| `WithLifetimeFixture/PixelTests` | Image cells compare by image; an image background composes with a glyph, keeps the image under paint, dies by an opaque background, takes the cell when drawn over text; shade and brighten paint it; the effective colour. |
