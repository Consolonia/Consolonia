# Sixel: runs of cells, how text and images share the screen

Architecture notes for `SixelConsoleImageEncoder` and `Sixel` (both in the `Consolonia.Imaging`
library, enabled with `UseConsoloniaImaging()`), and how image cells reach a sixel terminal. The
pipeline shared with the other protocols, and the encoder contract, are in core's
[bitmap.md](../Consolonia.Core/Drawing/bitmap.md).

## How it works

A sixel image never has text drawn *on top of it* in the terminal: every cell is either a sixel or
text. The pixel buffer holds the picture as cells with a `PixelImage` background, like any image
protocol; the choice between sixel and text is made when the cell is written.

1. **A glyph wins the cell.** The encoder's `SupportsTextOverlay` is false, so the output writes an
   image cell with a glyph (the mouse cursor, a window edge crossing the picture) as text, on
   `Pixel.EffectiveBackgroundColor`: the paint over the picture's dominant colour, so the cell still
   looks like the picture rather than a hole in it. A picture cell with no glyph it leaves alone.
   A picture cell with any symbol, a space included, is written as text. The converter makes picture
   cells with no symbol, and gaps that layout never drew keep none, so they show the picture; a
   translucent overlay blended over a picture cell colours its foreground but draws no symbol, so the
   cell still shows the picture, dimmed.
2. **Changed cells are found.** `WriteImages(screen)` walks the screen against the encoder's own
   record of what it sent to each cell: the picture's digest and the paint over it, or nothing where
   the cell is text. A picture cell whose picture or paint differs from the record is gathered, and
   the record updated; a text cell (a glyph over a picture, a wide glyph's continuation) clears it,
   since the text replaced whatever sixel was there. A frame that changes no picture sends nothing,
   and a picture is sent again where a glyph over it went away. `Reset` clears the record.
3. **Runs.** Consecutive gathered cells on a row are joined into runs, and each is sent as
   **one sixel**: one escape sequence, one palette, one cursor move. A brush
   stroke is a one-cell run; a new picture is one run per row.
   - **The run's pixels.** `RenderRun` copies the cells' pixels side by side into one buffer. A
     cell's paint, when it has one, is composited over its pixels in the same pass. That is how a
     modal dims a picture on a sixel terminal: the paint is baked into the run.
   - **One palette per run** (`Sixel.CreateFromBitmap`): at most 256 colours per run rather than per
     picture, so a run of few colours is indexed exactly (`TryIndexExactly`) and a photo row goes to
     Wu's quantiser (`JeremyAnsel.ColorQuant`). The earlier converter quantised the whole picture to
     one palette at draw time; this is per row and at write time.
   - **Rendered runs are cached by content** (`ContentCache<byte[]>`, 16 MB): the key is the XXH3 of
     the cells' image digests and paints. A dialog opening dims the runs under it, which are
     quantised once; closing it re-sends the un-dimmed runs from the cache, which were made when the
     picture first appeared. The sixel itself is transient (`IsTransient`): the cache holds the
     bytes, not the `Sixel`.
4. **Writing a run.** `SetCaretPosition` at the run's first cell, then `WriteBytes`. The output
   writes the move whenever the cursor might not be where it believes: after a glyph in the last
   column the terminal's cursor still sits on the old row with a pending wrap, and a sixel would
   otherwise land a row up. Where a sixel leaves the cursor depends on the terminal, so after
   `WriteBytes` the output treats the cursor as unknown and moves it before anything else.
   **The last row.** A terminal counts a sixel's height in bands of 6 pixels, so a cell's sixel
   reaches up to 5 pixels into the row below (Windows Terminal answers the cell size query with
   10x20: a 20-pixel sixel is 24). Unset pixels are transparent (DCS `0;1q`), so the row below
   keeps what it shows; by default a terminal fills them with the background, out to that row's
   bottom. On the last row the overhang would scroll the whole screen up a line, under everything
   the output and the render target remember (stale pictures through a dialog, holes where pictures
   should be). So the last row's sixels are drawn with scroll margins of the rows above
   (DECSTBM `ESC[1;{rows-1}r`, put back with `ESC[r`): with the cursor below them there is nothing
   to scroll, and the overhang is cut off at the bottom of the screen. Rounding the sixel down to
   whole bands instead left a strip in every cell that a space could only fill with a flat colour.
5. **The terminal does the rest.** Writing a character into a cell replaces the sixel pixels in
   that cell. Neighbouring cells are not sent again because they did not change, so the rest of the
   picture stays on screen.

**What it can't do:** real transparency. Anti-aliased text with the image showing through behind
the letters is not possible. Any cell holding a glyph shows no image. Only a full-cell paint keeps
the image.

## The bytes on the wire

`Sixel.Render` serialises one image:

- DCS `0;1q` (unset pixels transparent), the colour definitions, the sixel bands, ST.
- **Only the colours the image uses are defined.** A run uses what its cells use, and defining all
  256 used to make up most of the bytes of a one-cell sixel.
- Bands are built six rows at a time per colour. `BuildSixelRow` uses `Vector256` when available,
  then the rows are run-length encoded.
- The bytes are cached on the instance after the first render, unless the image is transient.
- The payload is strict ASCII (data bytes 0x3F..0x7E), so it goes to `WriteBytes` without
  building a string of it (see [bitmap.md](../Consolonia.Core/Drawing/bitmap.md#writing-the-frame)).

## Benchmarks

Machine-dependent. Read them as ratios. The commands and the shared setup are in
[bitmap.md](../Consolonia.Core/Drawing/bitmap.md#benchmarks). A full screen is 240×67 cells of 8×16, a 1920×1072
picture.

**Output path per frame** (`ImageOutputBenchmarks`, `Sixel*`, default job, in-process, null stream),
driven as the render target drives the output: the changed cells written, then `EndUpdate`, which
calls `WriteImages`. The dialog-closed scenario shows the dimmed picture before each run.

| Scenario | Time | Allocated (`--alloc`) | Bytes on the wire |
|---|---|---|---|
| New full-screen picture: a run per row, quantised and encoded | 50 ms | 2.7 MB | 628,728 |
| Dialog closed: every run from the cache | 1.2 ms | 2.7 KB | 624,738 |
| One cell: a one-cell run | 3.1 µs | 10 KB | 3,032 |

Times were measured before the encoder moved to the imaging library; allocations and bytes are
measured with it (`--alloc`, `--digest`).

A new picture's allocation is Wu's quantiser (its histogram and the indexes of the whole frame
image) and the rendered runs, which the cache keeps; the runs' slices of the indexes are rented.

For comparison, before this model a full-screen picture was one sixel per cell with the bytes made
at draw time: quantising the picture took about 110 ms in the converter, and appending the 16,080
cached cell sixels about 4.5 ms in the output, for 552,336 bytes on the wire as one joined sixel or
about 1 KB per cell unjoined.

**Renderer per frame** (`PixelImageFrameBenchmarks`) and the whole frame (`BitmapFrameBenchmarks`)
are in [bitmap.md](../Consolonia.Core/Drawing/bitmap.md#benchmarks); nothing sixel-specific happens there any more.

## Block glyphs: removed

Block element glyphs used to be painted into the cell's pixels (`Sixel.DrawBlockGlyph`), so a
window edge or shadow could cross a picture without turning cells into text. This was removed for
two reasons:

- **Wrong colours.** The glyph's colour had to go into the cell's palette. That palette was the
  whole visible image quantised to 256 colours, so it was almost always full. A full palette gave
  the glyph the nearest colour the image had, and a blue window edge came out as whatever the
  picture held closest to blue.
- **Fragile geometry.** Painting depended on the cell pixel size, and on the terminal drawing block
  elements as exact cell fractions. Neither is guaranteed.

Block glyphs take a text cell like any other glyph, on the cell's effective colour.

## Text over an image: the dominant colour

**Problem:** an ordinary glyph with a transparent or translucent background lands on an image cell.
The cell's own colour is the paint, transparent when there is none, so the text cell would come out
with a transparent background and look like a cell-shaped hole in the picture.

**Implemented:** `PixelImage.DominantColor` is the colour covering most of the cell's pixels,
computed lazily. `Pixel.EffectiveBackgroundColor` blends the paint over it, so a dimmed
picture's text cells match the dimmed picture. The output writes a glyph cell on that colour when
the encoder cannot overlay text. The pixel buffer keeps the image under the glyph; it is the output,
for this protocol, not `Pixel.Blend`, that turns the cell into text, because it is the protocol that
cannot show both.

The dominant colour was chosen over a mean because a cell split between two colours (an edge
between sky and roof) averages to a muddy colour that appears in neither.

**Applies to sixel and to the no-protocol fallback.** Kitty composites the glyph over the picture in
the terminal, and quad-pixel cells already carry a real background colour.

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
| `Consolonia.Imaging.Tests/ImageEncoderTests` (`Sixel*`) | Neighbours on a row are one sixel at the run's first cell, with the cursor put back after; cells apart are separate sixels; a glyph over a picture is text on the picture's colour and no sixel, a space too; a backdrop leaves the picture; a cell is sent again only when what it shows changed, and everything after a reset; a dimmed run is different bytes and the un-dimmed run comes back from the cache byte for byte. |
| `Consolonia.Imaging.Tests/SixelPaletteTests` | Only used colours are defined. A transient image renders the same bytes without keeping them. Too-small arrays are refused. Exact versus Wu quantisation. Rounded percentages. |
| `PixelImageConvertCellsTests`, `WithLifetimeFixture/PixelImageBitmapConverterTests` (core) | The converter, which is the same for every image protocol (see [bitmap.md](../Consolonia.Core/Drawing/bitmap.md#tests)). |
| `WithLifetimeFixture/PixelTests` (core) | Image cells compare by image; the effective colour of a painted image cell. |
| `AnsiConsoleOutputWriteTests`, `WithLifetimeFixture/AnsiConsoleOutputImageTests` (core) | The output the sixels are written to, and the text it writes over pictures. |
