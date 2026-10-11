# Sixel: per-cell images, how text and images share the screen

Architecture notes for `SixelBitmapRenderer`, `Sixel` and the sixel pass of `RenderTarget`. The
pipeline shared with the other renderers is in [bitmap.md](bitmap.md). The later sections are
design notes from 2026-10-09 on compositing text over sixel images.

## How it works

A sixel image never has text drawn *on top of it* in the terminal. Instead, the picture is cut into
one-cell sixels, and every cell is either a sixel or text. The choice is made in the pixel buffer,
before anything reaches the terminal.

1. **Render once per picture and size.** `SixelBitmapRenderer` is a
   `CellBitmapRenderer<CellRendering>`. It keeps one rendering per picture and size and refills it
   in place when the picture changes or scrolls (see [bitmap.md](bitmap.md)).
2. **One sixel per cell, by content.** `RenderCells` hashes every visible cell's pixels
   (`ContentKey`, XXH3-128) and looks them up in `CellSixels`, a `ContentCache<Sixel>` of 32K cells.
   A cell seen before gets **the same `Sixel` instance**. `Symbol` compares sixels by reference, so
   that cell is not dirty and is not sent again. A brush stroke rewrites only the cells it touched.
3. **Quantise only the new cells, together.** The cells not in the cache are grouped by distinct
   content. A flat canvas is one block repeated, and every cell showing it must get the same sixel.
   They are then quantised in one go, so they share one palette, which lets the sixel pass join
   neighbours into one image:
   - **Every cell new** (a new picture, a scroll): the visible image is quantised as it is, and each
     block's indices are sliced from its first cell's rows.
   - **Some cells new**: the distinct blocks are copied side by side and quantised as one image.

   Each cell's pixels are a slice of the quantiser's own output, never a nearest-colour match
   against it. A nearest-colour match gave grey halos around antialiased lines.
4. **Quantise exactly when possible** (`Sixel.Quantize`). `TryIndexExactly` indexes up to 256
   distinct colours exactly and gives up at the 257th. The cells under a brush stroke hold a
   handful of colours, and Wu's quantiser (`JeremyAnsel.ColorQuant`) has a large fixed cost however
   small the image is. Photographs go to Wu, reduced to 256 colours.
5. **Layering in `Pixel.Blend`** (`PixelBufferImplementation/Pixel.cs`):
   - *Opaque layer on top*: the fast path returns the upper pixel, and the sixel is gone from that cell.
   - *Glyph on top*: the glyph replaces the sixel, because a glyph needs a text cell. The cell's
     background becomes the sixel's dominant colour (below).
   - *Translucent backdrop with no glyph* (modal dimmer, shade): `Sixel.Wash` alpha-blends the wash
     into the sixel's **palette**. The pixels are shared and the variant is cached per colour (up to
     8), so a dimmed cell is the same instance every frame and is sent once.
   - Block element glyphs (`▀`–`▐`, `▔`–`▟`: window edges, shadows) are ordinary glyphs and take a
     text cell too. See "Block glyphs: removed" below.
6. **The sixel pass** (`RenderTarget.RenderSixelRegions`). This is pass 1, before text:
   - **What is written.** A cell is written when it holds a sixel, is dirty, isn't the instance the
     terminal already shows (`_cache`), and isn't under the mouse cursor (`SixelToWrite`). A mouse
     move or a neighbour repainting marks cells dirty without changing them, and those send nothing.
   - **Joining cells.** Cells to write that share a palette instance are grown into maximal
     rectangles. Each rectangle is `BitBlt`ted into one combined sixel and written with a single
     escape sequence. The combined sixel is `IsTransient`: its bytes come from a per-thread scratch
     buffer and are not kept, and its pixel array is reused across frames.
   - **Cursor moves.** `AnsiConsoleOutput.WriteSixel` always moves the cursor explicitly first.
     After a glyph in the last column, the terminal's cursor still sits on the old row with a
     pending wrap, and a sixel would otherwise land a row up.
7. **The text pass** (pass 2) skips sixel cells. The one exception is the cell under the mouse
   cursor: the cursor is text, so it is drawn on the sixel's dominant colour.
8. **The terminal does the rest.** Writing a character into a cell replaces the sixel pixels in
   that cell. Neighbouring cells are not sent again because they did not change, so the rest of the
   picture stays on screen.

**What it can't do:** real transparency. Anti-aliased text with the image showing through behind
the letters is not possible. Any cell holding a glyph shows no image. Only a full-cell wash keeps
the image.

## The bytes on the wire

`Sixel.Render` serialises one image:

- DCS `q`, the colour definitions, the sixel bands, ST.
- **Only the colours the image uses are defined.** A cell uses a handful of a palette shared by the
  whole picture, and defining all 256 made up most of the bytes. A one-cell sixel went from about
  4.1 KB to under 1 KB.
- Bands are built six rows at a time per colour. `BuildSixelRow` uses `Vector256` when available,
  then the rows are run-length encoded.
- The bytes are cached on the instance after the first render, unless the image is transient.
- The payload is strict ASCII (data bytes 0x3F..0x7E). `WriteSixel` therefore appends
  `Render()`'s span straight into the byte frame, with no conversion to text (see
  [bitmap.md](bitmap.md#the-byte-frame-ansiconsoleoutput)).

## Benchmarks

Machine-dependent. Read them as ratios. The commands and the shared setup are in
[bitmap.md](bitmap.md#benchmarks). A full screen is 240×67 cells of 8×16, a 1920×1072 picture.

**Renderer per frame** (`SixelFrameBenchmarks`, ShortRun). This is what a paint program does: no
change, a brush stroke, a new picture.

| Frame | Original | After cell reuse | After rendering reuse |
|---|---|---|---|
| Nothing changed | 17.2 ms / 1.2 MB | 4.8 ms / 1.2 MB | 3.8 ms / 536 B |
| One cell changed | 79.6 ms / 3.3 MB | 4.9 ms / 1.1 MB | 4.2 ms / 4 KB |
| One cell changed, including the copy out of the bitmap | — | ~9.5 MB | 5.0 ms / 568 B |
| Every cell new | 146 ms / 8.5 MB | 52.6 ms / 14.3 MB | ~60 ms / 13.9 MB (noisy) |

The remaining per-frame cost of an unchanged or one-cell frame is hashing every visible cell. The
switch from SHA-256 to XXH3 took the unchanged frame from 14.9 ms to 5.8 ms on its own.

Bytes to the terminal:
- One changed cell: from about 4.1 KB of sixel plus a resend of the whole region, to about 1 KB.
- The first stroke on a blank canvas: from about 2 MB (the whole canvas), to only the cells under
  the brush.

**Output path per frame** (`ConsoleOutputBenchmarks`, default job, in-process, null stream). Before
and after the byte frame:

| Scenario | Before | After | Allocated before | Allocated after |
|---|---|---|---|---|
| Full-screen sixel (552,336 bytes) | 293 µs | 17.5 µs (17×) | 1.1 KB | 80 B |
| One-cell sixel (3,036 bytes) | 714 ns | 298 ns (2.4×) | 80 B | 80 B |

Before, the rendered bytes were widened to a string, appended to a `StringBuilder` frame, and
encoded back to UTF-8 by the `StreamWriter`. Now they are one copy into the frame and one write.
The output bytes are identical (SHA-256 digests match).

## Block glyphs: removed

Block element glyphs used to be painted into the cell's pixels (`Sixel.DrawBlockGlyph`), so a
window edge or shadow could cross a picture without turning cells into text. This was removed for
two reasons:

- **Wrong colours.** The glyph's colour had to go into the cell's palette. That palette is the whole
  visible image quantised to 256 colours, so it is almost always full. A full palette gave the glyph
  the nearest colour the image had, and a blue window edge came out as whatever the picture held
  closest to blue.
- **Fragile geometry.** Painting depended on the cell pixel size, and on the terminal drawing block
  elements as exact cell fractions. Neither is guaranteed.

Block glyphs now take a text cell like any other glyph, on the cell's dominant colour (below).

## Text over an image: dominant colour as background

**Problem:** an ordinary glyph with a transparent or translucent background could land on a sixel
cell. The sixel cell's background is `PixelBackground.Transparent`, so the text cell came out with a
transparent background and looked like a cell-shaped hole in the picture.

**Implemented:** when a glyph wins over a sixel cell, `Pixel.Blend` uses the sixel's **dominant
colour** (`Sixel.DominantColor`) as the background below the glyph, and blends the glyph's own
background over it as usual:

- *Transparent glyph background*: the cell gets the dominant colour.
- *Translucent glyph background*: it is blended over the dominant colour, not over transparent.
- *Opaque glyph background*: it wins (fast path), as before.

`Sixel.DominantColor` is the palette colour covering the most pixels, computed lazily from a
256-bucket histogram of `Pixels`.
- **Washed variants.** Only the index is cached. A washed variant has the same pixels with a washed
  palette, so it gives the washed colour automatically, and a dimmed picture's text cells match it.
- **`BitBlt`** resets the cached index, because it changes the pixels.

The dominant colour was chosen over a mean because a cell split between two colours (an edge
between sky and roof) averages to a muddy colour that appears in neither.

**Applies to sixel only:** kitty tiles composite in the terminal, and quad-pixel cells already
carry a real background colour, so neither needs this.

**Tests:** `PixelTests.TextOverSixelTakesDominantColorAsBackground`.

**Open risk, not addressed:** readability. Theme foregrounds assume theme backgrounds, so white
text over a cell whose dominant colour is pale sky can be hard to read. A possible follow-up is to
flip the foreground to black or white when its contrast ratio with the dominant colour is too low,
only for transparent-background text over images.

**Rejected alternative:** drawing the glyph into the cell's sixel pixels. On top of the palette
problem that removed block-glyph painting, this needs the terminal's font, size, hinting and
weight, none of which Consolonia knows. The text would look different from every other glyph on
screen.

## Tests

| Tests | Cover |
|---|---|
| `SixelRenderCellsTests` | An edited cell is the only one made again. Identical cells share one sixel from the first frame. Rendering into the previous frame's buffer gives the same cells. Wrongly sized pixels are refused. |
| `SixelPaletteTests` | New cells share one palette. A new cell keeps its exact colour. Only used colours are defined. A transient image renders the same bytes without keeping them. Too-small arrays are refused. Exact versus Wu quantisation. |
| `WithLifetimeFixture/SixelBitmapRendererTests` | Drawing at target size without resizing. Drawing over a picture replaces its cells. Only visible cells are rendered. A partial redraw reuses the rendering. Edits keep untouched cells' sixels. A new version refills the rendering. |
| `WithLifetimeFixture/PixelTests` | Sixel symbols compare by image. Translucent overlays wash the palette. Block glyphs take a text cell. Text takes the dominant colour. |
| `AnsiConsoleOutputWriteTests` | The byte frame the sixels are written into. |
