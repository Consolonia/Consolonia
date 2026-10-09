# Sixel overlay: how text and images share the screen

Notes from a design discussion (2026-10-09) on how Consolonia composites text over sixel images,
the limits of the current approach, and a proposed improvement.

## How it works

A sixel image never has text drawn *on top of it* in the terminal. Instead, the image is cut into
one-cell tiles and every cell is either a tile or text. The choice is made in the pixel buffer
before anything reaches the terminal.

1. **The image is tiled per cell** — `SixelBitmapRenderer.Draw` slices the scaled bitmap into
   `CellPixelWidth × CellPixelHeight` blocks. It turns each block into its own `Sixel` (one palette
   shared across the image) and stores it in the pixel buffer like a character:
   `new Pixel(new PixelForeground(new Symbol(cellSixel, 1), ...))`. Tiles are cached by content
   (`CellSixels`). `Symbol` compares sixels by reference, so an unchanged cell is not dirty and is
   not re-sent.

2. **Layering happens in `Pixel.Blend`** (`PixelBufferImplementation/Pixel.cs`):
   - *Opaque layer on top* → the fast path returns the upper pixel; the tile is gone from that cell.
   - *Glyph on top* → the glyph replaces the tile: a glyph needs a text cell, so the image loses.
   - *Translucent backdrop, no glyph* (modal dimmer, shade) → `Sixel.Wash` alpha-blends the wash
     into the tile's **palette**; the pixels are shared and the variant is cached, so a dimmed
     tile is only sent once.
   - Block element glyphs (`▀`–`▐`, `▔`–`▟`: window edges, shadows) are ordinary glyphs here: they
     take a text cell too. See "Block glyphs: removed" below.

3. **Output runs in two passes** (`RenderTarget`):
   - Pass 1, `RenderSixelRegions`, joins runs of dirty tiles that share a palette into one larger
     sixel and writes it with a single escape sequence.
   - Pass 2 writes the remaining dirty cells as normal text and skips tile cells.

4. **The terminal does the rest.** Writing a character into a cell replaces the sixel pixels in
   that cell. Neighbouring tiles are not re-sent because they did not change, so the rest of the
   image stays on screen.

**What it can't do:** real transparency. No anti-aliased text with the image showing through
behind the letters. Any cell holding a glyph shows no image; only a full-cell wash preserves it.

## Block glyphs: removed

Block element glyphs used to be painted into the tile's pixels (`Sixel.DrawBlockGlyph`), so a
window edge or shadow could cross a picture without turning cells into text. It was removed
because the glyph's colour had to go into the tile's palette, and the palette is the whole visible
image quantized to 256 colours, so it is almost always full. A full palette gave the glyph the
nearest colour the image had, so a blue window edge came out as whatever the picture held closest
to blue. Painting also depended on the cell pixel size and on the terminal drawing block elements
as exact cell fractions, neither of which is guaranteed.

Block glyphs now take a text cell like any other glyph, on the tile's dominant colour (below).

## Text over an image: dominant colour as background

**Problem:** when an ordinary glyph with a transparent or translucent background landed on a tile,
the tile's background is `PixelBackground.Transparent`, so the text cell came out with a
transparent background and looked like a cell-shaped hole in the picture.

**Implemented:** when a glyph wins over a sixel tile, `Pixel.Blend` uses the tile's
**dominant colour** (`Sixel.DominantColor`) as the background below the glyph, and blends the glyph's
own background over it as usual:

- *Transparent glyph background* → the cell gets the dominant colour.
- *Translucent glyph background* → it is blended over the dominant colour, not over transparent.
- *Opaque glyph background* → it wins (fast path), as before.

`Sixel.DominantColor` is the palette colour covering the most pixels, computed lazily from a
256-bucket histogram of `Pixels`. Only the index is cached, so a washed variant (same pixels,
washed palette) gives the washed colour automatically, and a dimmed picture's text cells match it.
`BitBlt` resets the cached index because it changes the pixels.

The dominant colour was chosen over a mean because a tile split between two colours (an edge
between sky and roof) averages to a muddy colour that appears in neither.

**Applies to sixel only:** kitty tiles composite in the terminal, and quad-pixel cells already
carry a real background colour, so neither needs this.

**Tests:** `PixelTests.TextOverSixelTakesDominantColorAsBackground`.

**Open risk, not addressed:** readability. Theme foregrounds assume theme backgrounds, so white
text over a cell whose dominant colour is pale sky can be hard to read. A possible follow-up is
flipping the foreground to black or white when the contrast ratio with the dominant colour is too
low, only for transparent-background text over images.

**Rejected alternative:** drawing the glyph into the tile's sixel pixels. On top of the palette
problem that removed block-glyph painting, this needs the terminal's font, size, hinting and weight, none of which Consolonia knows. The text
would look different from every other glyph on screen.
