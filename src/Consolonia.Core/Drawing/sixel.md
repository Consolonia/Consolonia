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
   - *Ordinary glyph on top* → `TryCompositeOverSixel` refuses and the glyph replaces the tile
     ("Any other glyph needs a text cell, so the image loses").
   - *Translucent backdrop, no glyph* (modal dimmer, shade) → `Sixel.Wash` alpha-blends the wash
     into the tile's **palette**; the pixels are shared and the variant is cached, so a dimmed
     tile is only sent once.
   - *Block element glyph* (`▀`–`▐`, `▔`–`▟`: window edges, shadows) → `Sixel.DrawBlockGlyph`
     paints the block into the tile's pixels, so a border can cross a picture without punching a
     full-cell hole in it.

3. **Output runs in two passes** (`RenderTarget`):
   - Pass 1, `RenderSixelRegions`, joins runs of dirty tiles that share a palette into one larger
     sixel and writes it with a single escape sequence.
   - Pass 2 writes the remaining dirty cells as normal text and skips tile cells.

4. **The terminal does the rest.** Writing a character into a cell replaces the sixel pixels in
   that cell. Neighbouring tiles are not re-sent because they did not change, so the rest of the
   image stays on screen.

**What it can't do:** real transparency. No anti-aliased text with the image showing through
behind the letters. Any cell holding a normal glyph shows no image; only a full-cell wash or a
block glyph preserves it.

## Block glyphs: no font involved

`Sixel.BlockCovers` works out each glyph's coverage from the cell's pixel size:

| Glyphs | Coverage |
|---|---|
| `▀` | top half: `y * 2 < height` |
| `▁`…`█` | bottom n/8: `(height - y) * 8 <= n * height` |
| `▉`…`▏` | left n/8 |
| `▐`, `▔`, `▕` | right half, top 1/8, right 1/8 |
| `▖`…`▟` | 4-bit quadrant mask |

Covered pixels are set to the glyph colour (added to the palette or matched to the nearest entry by
`GetPaletteWithColor`).

That's also why only this subset is supported: Unicode defines block elements as exact fractions
of the cell. Most modern terminals (kitty, WezTerm, foot, Ghostty, iTerm2, Windows Terminal,
recent VTE) also draw them themselves rather than from the font, so Consolonia's drawing should
match real text cells to within about a pixel.

### Known weaknesses

- **Rounding:** with odd cell sizes (e.g. 17px tall, where `▀` covers 9 rows here and a terminal
  might give it 8) there can be a 1px step where a shadow or edge goes from a text cell onto an
  image cell.
- **Terminals that use the font:** xterm with some fonts, and some Konsole setups, draw blocks from
  the font, which may not fill the cell (ascent/descent gaps).
- **Wrong cell size:** everything depends on `CellPixelWidth/Height` being right. If the terminal
  reports the wrong size, or the font changes and nobody queries it again, the tiles are wrong
  anyway.
- **Box-drawing lines are not covered:** `─ │ ┌ ╔` and the rest are not in `IsBlockGlyph`, because
  line thickness and position vary between terminals and fonts. A border drawn with them over an
  image turns those cells into text cells, leaving a cell-sized hole along the edge.
- **Colour:** the glyph colour has to fit into the tile's palette, so near a 256-entry limit it can
  come out as an approximation rather than the exact colour.

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

**Rejected alternative:** drawing the glyph into the tile's sixel pixels. Unlike block elements,
this needs the terminal's font, size, hinting and weight, none of which Consolonia knows. The text
would look different from every other glyph on screen.
