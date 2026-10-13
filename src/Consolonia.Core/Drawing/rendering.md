# Rendering: how a frame gets from Avalonia to the terminal

Architecture notes for the rendering pipeline: what happens between an Avalonia control drawing
itself and the bytes a terminal receives. Pictures go through the same pipeline and have their own
notes in [bitmap.md](bitmap.md); this document is about the part every frame goes through, text or
not.

## The pipeline at a glance

```
Avalonia render loop                                   (SleepLoopRenderTimer, 30 frames a second at most)
  └─ Compositor renders the invalidated visuals
       └─ RenderTarget.CreateDrawingContext            (RenderTarget.cs: one DrawingContextImpl per pixel buffer)
            └─ DrawingContextImpl                      (DrawingContextImpl*.cs)
                 draw calls → cells blended into the PixelBuffer, each draw's rectangle marked dirty
                 │
                 ▼  the context is disposed: the render pass is over
            RenderTarget.RenderToDevice
                 every cell looked at (the caret may be anywhere), every dirty cell compared with
                 the cache of what the terminal shows, every changed cell → IConsoleOutput.WritePixel
                 │
                 ▼
            AnsiConsoleOutput                          (Infrastructure/AnsiConsoleOutput.cs)
                 a cursor move when the cell is not the next one, the SGR attributes that differ
                 from the last cell written, the glyph; image cells → an IConsoleImageEncoder
                 │
                 ▼
            byte frame (UTF-8) → one write to stdout per frame, in a DEC 2026 synchronized update
```

Three ideas carry the design:

- **The screen is a buffer of cells.** Avalonia draws into a `PixelBuffer`, a `Pixel[width, height]`
  in cell units, through the same `IDrawingContextImpl` it would draw into a bitmap with. Layering
  is a blend of one cell over another; clipping is a rectangle of cells.
- **The terminal is written by difference.** The render target keeps a copy of every cell the
  terminal shows and writes only the cells that differ, and the output writes only the attributes
  that differ from the previous cell. A frame that changes one character sends a cursor move and a
  character.
- **Nothing is allocated per cell.** A `Pixel` is a 40-byte `readonly struct`; the buffer, the
  cache and the dirty mask are arrays of them; blends happen in place and compares by reference. A
  frame of text allocates a few kilobytes in Avalonia's text layer and nothing in this pipeline.

## The cell

`PixelBufferImplementation/`

A **`Pixel`** is one cell: a `PixelForeground`, a `PixelBackground` and a `CaretStyle`.

- **`Symbol`** is what the cell shows: a `char`, or a *complex* string for a grapheme that is more
  than one UTF-16 unit (an emoji with modifiers, a combining sequence); a `Width` in cells (1, or 2
  for a wide glyph; the cells a wide glyph covers hold a symbol of width 0); and a box-drawing
  `Pattern`, the four arms of a border character as bits, which is how borders join (below).
- **`PixelForeground`** is the symbol, its colour, and three optional attributes: `FontWeight?`,
  `FontStyle?` and `TextDecorationLocation?`. Null means "not set": a foreground blended over
  another keeps the attributes below that it does not set. They are packed in 6 bytes.
- **`PixelBackground`** is a colour, and for a cell of a picture a `CellImage` under a paint colour
  (see [bitmap.md](bitmap.md#the-pixel-model)).
- **`CaretStyle`** marks the one cell where the caret is shown and how (block, underline, bar,
  blinking or not). It is drawn with a brush like anything else.

`Pixel` is stored flat: the fields of the symbol, the foreground and the background side by side,
40 bytes, where the three nested structs padded to 56. `Foreground` and `Background` are built on
read, in registers. It is a `readonly struct`, so it can be passed and held by reference without
the compiler copying it defensively, and every member that takes a cell takes it by `in`:
`Blend(in Pixel)`, the equality operators, `IsCaret(this in Pixel)`, `IConsoleOutput.WritePixel`.
`PixelTests.PixelIsFortyBytes` keeps the size.

### Blending

`Pixel.Blend(in Pixel above)` is the one rule for drawing one thing over another, and every draw
call ends in it. In order:

1. An image cell above takes the cell whole ([bitmap.md](bitmap.md#the-pixel-model)).
2. An opaque background above replaces the cell.
3. A transparent background above with nothing to draw (no symbol, no caret) leaves the cell as it
   is. With a symbol, the foregrounds blend: the symbol above replaces the one below, unless both
   are box-drawing symbols, whose patterns merge (`BoxPattern.Merge`) so that two borders meeting
   at a cell become a junction; the colour is the colour above; each attribute is the one above
   when set, else the one below. The caret style blends the same way.
4. A translucent background above merges into both colours below through the console colour mode's
   `Blend` (`IConsoleColorMode`), so a shade over text dims the text and its background alike; a
   symbol above replaces the foreground as in 3.

`Shade`, `Brighten` and `Invert` make the derived cells a theme or a selection needs; they are
blends or colour operations on the same fields.

### The buffer

`PixelBuffer` is the `Pixel[,]`, initialised to `Pixel.Space` (a space on a transparent
background), with a new one made on every resize. Its indexers copy a cell out or in; the hot paths
do not use them. `CellAt(x, y)` returns a `ref readonly Pixel` for code that looks at a cell, and
`Blend(x, y, in above)` blends into a cell where it is: the drawing context calls it for every cell
it touches, and before it the same blend copied the cell out through the indexer, into the blend
and back.

## Drawing

`DrawingContextImpl` is Avalonia's `IDrawingContextImpl` over the buffer. Avalonia's coordinates are
cells: a `Point` is floored to a cell (`ToPixelPoint`), and the only part of the transform that
matters is its translation. Every draw adds the rectangle it touched to the window's
`DirtyRegions`. What Avalonia asks for becomes:

- **Text** (`DrawGlyphRun`). The text was shaped by `ConsoleTypeface`: one glyph per grapheme, its
  advance the glyph's width in cells. Each glyph is one `DrawPixel` of a foreground-only cell
  (symbol, colour, the typeface's weight and style) blended at its cell. A wide glyph also writes
  the cells it covers as width-0 cells carrying only the background, so that the terminal is given
  blanks there. Tabs are spaces; a line break is a space.
- **Rectangles** (`DrawRectangle`, `DrawGeometry` of a rectangle or a stream geometry's fills).
  The fill is one cell blended at every cell of the rectangle: a solid brush makes the cell once, a
  gradient asks the brush its colour at each cell (`ColorAt`). The border is a `LineBrush` pen with
  a `LineStyle`: single, double or bold lines are box-drawing symbols whose patterns merge with
  what is there, so adjacent borders join; `Edge` and `EdgeWide` draw half-block edges. The fill
  and the pen rectangles are adjusted by a cell because Avalonia's rectangle geometry assumes a
  pen half inside, half outside, which a cell grid cannot do.
- **Lines** (`DrawLine`). A box-drawing line, horizontal or vertical, merged into the cells it
  crosses. A pen whose thickness is `UnderlineThickness` or `StrikethroughThickness` is Avalonia
  drawing a text decoration: the cells on the line get that decoration and keep their symbol.
- **The caret.** `MoveConsoleCaretToPositionBrush` is a brush whose fill is a caret style: a
  rectangle or line drawn with it blends the caret style into its first cell, which is how a
  `TextBox` says where the caret is.
- **Pictures** (`DrawBitmap`): [bitmap.md](bitmap.md).
- **Clipping.** `PushClip` of a rectangle intersects with the current clip; cells outside are not
  touched. A region clip is approximated by its bounding rectangle.
- **Not supported.** Opacity (`PushOpacity`), opacity masks, geometry clips, layers, ellipses,
  rounded corners and geometries other than rectangles, lines and the stream geometries Avalonia's
  shapes produce. Each raises `ConsoloniaPlatform.NotSupported`; with the default
  `UnsafeRendering` setting the request is ignored (a rounded rectangle is drawn square), without
  it the frame throws.

## Writing the frame

`RenderTarget.RenderToDevice` runs when the drawing context is disposed, which is Avalonia's end of
a render pass, and, with its own dirty regions, 16 ms after the mouse cursor moved. It is
synchronized: one frame at a time.

1. **Dirty regions.** The regions are taken (and cleared), intersected with the buffer, and the
   frame ends here if there are none. The rectangles are marked into one `bool` per cell
   (`CellRectangles.Scratch`, kept between frames), so the loop asks a mask, not a list.
2. **A new buffer, a new cache.** If the buffer's size is not the cache's, `ResetCache`: the cache
   is reinitialised to `Pixel.Empty` (nothing was ever written), the console forgets what it knows
   about the terminal, and the whole screen is marked dirty. The same happens when the terminal's
   contents are lost (another program had it while console I/O was paused) and on a resize.
3. **`HideCaret`, `BeginFrame`.** The caret is hidden for the frame; the console is told a frame
   starts ([bitmap.md](bitmap.md#writing-a-frame)).
4. **Every cell.** The loop visits every cell of the screen, not only the dirty ones, because the
   caret must be found wherever it is. A cell is looked at in place (`CellAt`); only a dirty one is
   copied out, since what follows may rewrite it:
   - **The mouse cursor.** The cell under the mouse cursor gets the cursor: with the default `" "`
     cursor its own glyph on an inverted, contrast background; with a text cursor the cursor's
     character in the cell's colours.
   - **Wide glyphs.** A wide glyph whose covered cells were since overwritten (they are no longer
     width 0) becomes a space, since the terminal would overrun; a width-0 cell that is not under
     a wide glyph becomes a space, so that the terminal is given something.
   - **The compare.** The cell, and for a wide glyph the cells it covers, are compared with the
     cache by reference; if none differs the cell is skipped.
   - **`WritePixel`** to the console, and the cache takes the cell.
5. **`EndFrame`**, then the caret is placed, styled and shown if a cell asked for it, and **`Flush`**
   sends the frame.

## The output

`AnsiConsoleOutput.WritePixel` writes a text cell with `WriteTextCell` (an image cell goes to the
image encoder, [bitmap.md](bitmap.md#the-output-decides-what-an-image-cell-is-ansiconsoleoutput)):

- **Cursor.** A cursor move is written only when the cell is not where the terminal's cursor
  already is, which after the previous cell it usually is: a row of changed cells is one move and
  the glyphs. Wide and complex glyphs are written as spaces then the glyph, with explicit moves
  around them, because terminals disagree about how far such a glyph advances the cursor.
- **Attributes.** Text decoration, italic, weight (bold, dim, normal), foreground and background
  are each written only when they differ from the last cell written. The colours go through the
  console colour mode: `RgbConsoleColorMode` writes 24-bit colours; `EgaConsoleColorMode` maps
  them to the nearest of the 16 console colours (8 for the background on a terminal without bright
  backgrounds). `ResetCache` forgets the last attributes, since another program may have changed
  the terminal's.
- **The frame.** Everything goes into one pooled `byte[]`, UTF-8 encoded on the way in, and
  `Flush` writes it in one `Stream.Write` wrapped in a DEC 2026 synchronized update, so the
  terminal shows the frame at once ([bitmap.md](bitmap.md#the-byte-frame)).

`UnitTestConsole` (`Consolonia.NUnit`) is an `IConsole` that keeps the pixels written instead of a
terminal, for the tests and the frame benchmarks; `DefaultNetConsoleOutput` is the output that uses
the `System.Console` API rather than escape sequences.

## What a frame costs

For a full HD terminal, 240×67 cells, every frame does:

| Work | Per | How much |
|---|---|---|
| Blend | cell drawn | one blend into the buffer in place; a cell under several controls is blended once per control |
| Scan | cell of the screen | a look at the caret and dirty flags, in place |
| Compare | dirty cell | the fields of the cell and its cached copy, by reference |
| Write | changed cell | the differing SGR attributes and the glyph into the byte frame (about 50 bytes of strings allocated per cell on this path) |
| Flush | frame | one write of the frame |

The pixel copies were the hidden cost. A 40-byte struct passed by value is copied on every call,
and before the in-place blend and the reference-based scan a frame moved each cell of the screen
several times: the render loop copied all 16,080 cells out of the buffer before it had looked at
their dirty flags, then copied both sides again for the compare, and every blend copied the cell
out through the indexer, into `Blend` and back. On a desktop the copies pipeline well and hide
behind the terminal's own work; on a small device, where a frame's working set is what the memory
bus can carry, they are a large share of the frame.

### Benchmark

`TextFrameBenchmarks` is a whole frame of a text screen on the unit test console: a bordered
background and a shaped line of text on each of the 67 rows, drawn through the drawing context,
then `RenderToDevice`. The glyph runs are shaped once; what is measured is the drawing, the scan
and the compare, which is the whole pipeline but the terminal and the bytes.

```
dotnet run -c Release --project src/Consolonia.Benchmarks -- --filter "*TextFrame*"
```

| Frame | Pixel copied everywhere | In place, by reference |
|---|---|---|
| Redrawn unchanged (every cell blended and compared, nothing written) | 3.00 ms | 2.18 ms |
| One character typed (one cell written) | 3.26 ms | 3.12 ms |
| Every cell changed, as on a scroll (every text cell written) | 2.81 ms | 2.72 ms |

Measured on the developer machine, the two builds back to back; the first row's error bars are
under 0.05 ms, the other two rows are within their run-to-run noise. Allocation is 5.4 KB a frame
in both, Avalonia's glyph run drawing. The unchanged redraw is the common frame of a text UI: a
control repaints, everything under it is blended and compared again, nothing is sent. The same
change took the unchanged full-screen picture frame of `BitmapFrameBenchmarks` from 538 to 393 µs
(sixel) and 527 to 453 µs (kitty); the picture frames are in [bitmap.md](bitmap.md#benchmarks).

## Tests

In `src/Tests/Consolonia.Core.Tests` and `src/Tests/Consolonia.Gallery.Tests`:

| Tests | Cover |
|---|---|
| `WithLifetimeFixture/DrawingContextImplTests` | The drawing context: text, wide glyphs and what overwrites them, tabs and clipping, rectangles with every pen, lines crossing into junctions, boxes over edges, gradients sampled per cell, underline and strikethrough. |
| `WithLifetimeFixture/PixelTests`, `PixelForegroundTests`, `PixelBackgroundTests`, `SimpleSymbolTests`, `DrawingBoxSymbolTests`, `PixelBufferTests`, `LineStylesTests`, `GlyphTests` | The cell: size, blending rules, box pattern merging, equality, serialisation; line styles; glyph widths. |
| `WithLifetimeFixture/RenderTargetTests` | The frame: after the terminal's contents are lost every cell is written again. |
| `AnsiConsoleOutputWriteTests` | The byte frame: order, UTF-8, surrogates, redirected output, an empty frame. |
| Gallery tests | Whole screens: each gallery page rendered on the unit test console and its screen checked for the text expected, so a change anywhere in the pipeline shows on a page. |
