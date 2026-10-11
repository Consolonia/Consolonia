# Kitty graphics: tiles, placements and washes

Architecture notes for `KittyBitmapRenderer` and `KittyGraphics`. The pipeline shared with the
other renderers is in [bitmap.md](bitmap.md). The protocol is specified at
https://sw.kovidgoyal.net/kitty/graphics-protocol/.

## How it works

The kitty protocol transmits an image once, by id, and then shows it through **placements**: rects
of the image put at a cell position. Consolonia uses it as follows:

1. **Cut the picture into tiles.** `KittyBitmapRenderer.Render` cuts the visible pixels into tiles
   of 8×4 cells (`TileColumns`, `TileRows`). Tiles are small enough that a brush stroke touches few
   of them, and large enough that a screen holds only a few hundred (510 at 240×67).
2. **Transmit each distinct tile once.** A tile is keyed by its content (`ContentKey`, XXH3-128).
   `TileImages`, a `ContentCache<int>`, maps content to the terminal's image id. A tile already in
   the terminal is not sent again. A changed picture retransmits only the tiles it touched. Tiles
   that look alike, such as a blank canvas, share one image.
3. **Put the image in the cell background.** Every covered cell gets
   `PixelBackground(Transparent, KittyTile(imageId, x, y))`, with `PixelForeground.Space`. The
   foreground stays free, so glyphs drawn later composite over the picture. An opaque background
   drawn over the cell evicts the tile, through `Pixel.Blend`'s opaque fast path. Diffing and
   occlusion therefore work unchanged.
4. **Coalesce tile cells into placements.** `RenderTarget.EmitKittyRectPlacements` runs at the end
   of a frame. It joins tile cells into maximal rectangles and creates one classic placement per
   rectangle, at z=-2 (below text, above background colours), with `C=1` so the cursor doesn't
   move. Each rectangle continues one image's tile grid under one wash. The image is pre-scaled to
   the cell grid, so a placement is a 1:1 crop and needs no `c=`/`r=` stretching.
5. **Diff placements across frames.** Live placements are a `Dictionary<KittyRect, int>`. An
   unchanged rectangle costs nothing. A new one is placed. A vanished one is deleted by placement
   id (`a=d,d=i`), and the image stays for cheap re-placement. The pass runs only when a tile cell
   changed, or when an evicted image is waiting to be deleted.
6. **Write text cells blank.** In pass 2, a tile cell is written as a blank cell with the terminal's
   default background. That clears what the picture was drawn over, and the placement shows on top.

## Translucent overlays: washes

The background colour of a tile cell starts transparent. `Pixel.Blend` keeps the tile under any
overlay that is not opaque, and blends the overlay's colour into that background. What accumulates
is exactly the **wash** to lay over the image, for example a modal backdrop dimming a picture.

- **Wash placement.** A rectangle with a visible wash gets a second placement, at z=-1 (above the
  image, below text), of a solid image in that colour.
- **Wash image.** One pixel per cell of the screen, zlib compressed (`BuildTransmitWashSequence`).
  It is a few hundred bytes whatever the screen size.
  - Why one pixel per cell: a single pixel stretched with `c=`/`r=` works on kitty, but terminals
    that map every cell to whole source pixels drop a placement whose cells get less than one pixel.
  - Wash images are kept by colour. One is freed once no placement uses it
    (`FreeUnusedWashImages`), and sent again sized up when the screen grows
    (`DropWashImagesSmallerThan`).
- **Mouse cursor.** Only for the mouse cursor cell is the wash composited over black in the text
  cell.

## Image ids and their lifetime

- **Ids.** Image and placement ids come from `KittyGraphics.AllocateImageId` and
  `AllocatePlacementId`. They count through 1..int.MaxValue and wrap within that range. A wide
  range matters because retransmitting an id the terminal still holds deletes that image and its
  placements.
- **Registry.** `TransmittedImages` records every id transmitted and not deleted since.
  `BuildDeleteSequence` removes an id from it.
- **Tile budget.** At least 24 MB of tile pixels (`MinTileImageBudgetBytes`), or three screens' worth
  (`ScreensOfTileImages`) on a larger screen. One full-screen picture therefore never evicts its own
  tiles while transmitting them, and a picture shown again within a few screens is placed, not
  resent. The terminal has to keep at least as much: XTerm.NET keeps the same three screens
  (`MaxImageRegistryBytes`). If the terminal dropped tiles that Consolonia still counts on, placing
  them again would show nothing.
- **Eviction is deferred.** A tile evicted from `TileImages` is not deleted immediately. A picture
  that isn't being redrawn may still show it, and deleting the image (`d=I`) takes its placements
  with it. Evicted ids go to `EvictedTileImages`. `RenderTarget.DeleteOrphanedTileImages` deletes
  each one once no live placement shows it (`SelectOrphans`).
- **Reuse.** A rendering is reused only while every tile it shows is still in the terminal
  (`IsReusable`). Using a rendering refreshes all its tiles in the least-recently-used order, so a
  picture on screen does not lose tiles to the budget.

### Resize, resume and exit

- **Resize, and resume after another program had the terminal.** `ForgetPlacements` moves every
  live placement to `_placementsToDeleteAfterFrame`. The frame places everything again from the
  cells, and `DeleteWhatTheFrameReplaced` deletes the old placements at its end. The images stay in
  the terminal, because the alternate screen was never left and nothing but this app deletes by id.
  A resumed picture is placed again from tiles already there. This was the fix for a black flash on
  resume: deleting and retransmitting left the cells empty until the new images decoded.
- **Exit.** `RestoreConsole` writes `BuildDeleteTransmittedImagesSequence()`, which deletes every
  image in the registry one by one, and then `DeleteAllImages` (`a=d,d=A`). `d=A` alone deletes only
  visible placements. It would leave the images kept for showing again in the terminal's memory.

## Writing a transmit

`KittyGraphics.WriteTransmitSequence` composes the chunked APC sequence straight into a pooled byte
buffer and hands it to `IConsoleOutput.WriteBytes`. A payload of megabytes never becomes a string.

- **Chunks.** Each chunk carries 3072 raw bytes (`RawChunkSize`), which encode to exactly 4096
  base64 characters, the protocol's maximum. Each chunk's base64 therefore stands alone.
- **Headers.** The first chunk carries the header, written with `Utf8.TryWrite`:
  - PNG: `a=t,f=100,q=2,i=…`
  - raw RGBA: `a=t,f=32[,o=z],q=2,i=…,s=…,v=…`

  Each chunk ends `m=1` except the last, which ends `m=0`.
- **Encoding.** Base64 is written with `Base64.EncodeToUtf8`. The buffer is sized up front by
  `MaxTransmitLength`.
- **Same bytes as a string.** `BuildTransmitSequence` composes the same bytes and decodes them to a
  string. It serves the wash images and the tests (`AKittyTransmitIsWrittenAsTheSameBytesItIsBuiltAs`).
- **PNG first.** Tiles are sent as PNG through Avalonia/Skia (`TryEncodePng`). A full-screen picture
  is about 7 MB as raw RGBA, over 9 MB in base64, and far smaller as PNG. When the render interface
  cannot encode, the renderer falls back to raw RGBA.
- **`q=2`** suppresses the terminal's replies on every command.

## Benchmarks

`ConsoleOutputBenchmarks`, default job, in-process, writing to a null stream (see
[bitmap.md](bitmap.md#benchmarks)). The full-screen case is 510 tiles of 8 KB each, 5,595,102 bytes
on the wire.

| Scenario | Before byte frame | After | Allocated before | Allocated after |
|---|---|---|---|---|
| Full-screen transmit, 510 tiles | 5.22 ms | 1.38 ms (3.8×) | 22.6 MB, gen 0 and gen 1 GCs | 32 KB, no GC |
| One tile | 7.08 µs | 0.81 µs (8.8×) | 43 KB | 0 B |

Before, every transmit went through `string.Concat` of base64 chunks, a `StringBuilder` frame and a
`StreamWriter` encoding back to UTF-8. That came to about 44 KB of garbage per 8 KB tile. The output
bytes are identical (SHA-256 digests match). PNG encoding and the terminal's decode are not part of
this measurement. Both scale with the number of tiles sent, which is what the tile cache keeps
down.

Not benchmarked: a whole kitty frame through the renderer (tiling, hashing, PNG encoding). The
hashing and rendering reuse it shares with sixel are measured by `SixelFrameBenchmarks`.

## Tests

| Tests | Cover |
|---|---|
| `KittyGraphicsTests` | Sequence formats (single chunk, chunked reassembly, PNG without dimensions, placements, washes, deletes), ids staying positive past overflow, and the registry: transmitted images are deleted together unless deleted one by one. |
| `KittyImageLifetimeTests` | An evicted image nothing shows is an orphan; one still shown is deleted once its placement goes; the cache's budget. |
| `WithLifetimeFixture/KittyBitmapRendererTests` | Each tile is its own image, tiles that look alike share one, an edit retransmits only the tile it touched, a partial redraw keeps the images already shown. |
| `WithLifetimeFixture/PixelTests` | A tile background composes with a foreground and is evicted by an opaque background; washes accumulate over image cells. |
| `AnsiConsoleOutputWriteTests` | A transmit written as bytes is the same as the one built as a string. |
