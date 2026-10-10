# Quad pixels: pictures from block glyphs

Architecture notes for `QuadPixelBitmapRenderer`, the fallback used when the terminal supports
neither kitty graphics nor sixel, or when `CONSOLONIA_GRAPHICS=quad` is set. The pipeline shared
with the other renderers is in [bitmap.md](bitmap.md).

## How it works

Each cell shows **2×2 pixels**: one of the 16 quadrant block glyphs (`▘ ▝ ▖ ▗ ▚ ▞ ▌ ▐ ▄ ▀ ▛ ▜ ▙ ▟ █`
or space), with a foreground colour for the lit quadrants and a background colour for the rest.
The result is ordinary text, so it works in every terminal that has colour, and it composites with
the rest of the UI like any other glyph.

1. **Scale only what is drawn.** `Draw` asks `GetVisiblePixels` for the intersected cells at two
   pixels per cell each way. A picture zoomed far past the screen is not scaled whole every frame.
2. **Split the four pixels into two groups**, in `GetColorsPattern`:
   - **Quadrants available** (`SupportsComplexEmoji`): a small k-means with two clusters, at most 10
     iterations. The brighter cluster becomes the lit quadrants.
   - **Quadrants not available**: only `▀`, `▄`, `█` and space are used. The renderer compares the
     average brightness of the top row with that of the bottom row.
   - All four pixels transparent gives a space with a transparent background, and the picture's
     transparent areas show what is behind them.
3. **Colour the two groups.** The foreground is the alpha-composite of the lit pixels, and the
   background is the composite of the others (`CombineColors`).
   - A full block `█` also gets an **opaque** background. Terminals can leave hairlines around a
     full block glyph, and a transparent background let the colour under the picture show through
     them.
4. **Blend into the buffer.** Each cell is `Blend`ed over what the pixel buffer already holds, and
   the whole intersected rectangle is marked dirty.

## How it differs from the image renderers

- **No caching.** Nothing is kept between frames: no `CellRendering`, no content cache. Every draw
  scales and classifies the intersected cells again. Each cell is a glyph and two colours, and the
  text pass already skips cells that match what the terminal shows (`RenderTarget._cache`), so a
  redraw that changes nothing writes nothing.
- **One allocation per draw.** Each draw allocates its pixel destination: 16 bytes per intersected
  cell (four pixels of BGRA), about 257 KB for a full 240×67 screen. That is far smaller than the
  image renderers' 8 MB per full HD screen, so it is not pooled.
- **The whole intersected rectangle is marked dirty**, not just the cells that changed. The text
  pass's cache comparison then drops the unchanged ones.
- **Real colours in the cell.** Text drawn over a quad picture blends with the cell's colours
  through the normal `Pixel.Blend` rules. It doesn't need the sixel dominant-colour treatment
  (see [sixelbitmap.md](sixelbitmap.md)).
- **Colour depth follows the console's colour mode.** The two colours are mapped to the terminal's
  colours like any other text colour, so the renderer also works in 16-colour modes, where kitty is
  never chosen.

## Limits

- The resolution is 2×2 per cell, about a quarter of what sixel or kitty give for an 8×16 cell, and
  the pixels are not square: a cell is usually twice as tall as wide.
  `AspectRatioAdjustedBitmap` reports half the height to layout for that reason.
- Each cell can show only two colours, so fine detail and gradients within a cell are lost.
- Some quadrant glyphs are missing from IBM code pages (the `TODO`s in the renderer). Without
  `SupportsComplexEmoji`, the renderer sticks to half blocks.

## Benchmarks

None. The renderer has no benchmark of its own. Its output is text and goes through the text path,
which `ConsoleOutputBenchmarks.TextFullScreen` measures: about 2.05 ms and 332 KB for a full screen
of 240×67 cells in runs of eight cells per colour. The byte-frame change left that path unchanged.

## Tests

None of its own. The pixel copy it shares with the other renderers is covered by
`BitmapRendererTests`. The glyph choice, the colour clustering and the opaque full-block background
have no tests. They are a gap worth closing if this renderer changes.
