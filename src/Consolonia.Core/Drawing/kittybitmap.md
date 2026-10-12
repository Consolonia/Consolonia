# Kitty graphics: blocks, placements and washes

Architecture notes for `KittyConsoleImageEncoder` (`Consolonia.Core.Infrastructure`) and
`KittyGraphics`. The pipeline shared with the other protocols is in [bitmap.md](bitmap.md). The
protocol is specified at https://sw.kovidgoyal.net/kitty/graphics-protocol/.

## How it works

The kitty protocol transmits an image once, by id, and then shows it through **placements**: an
image put at a cell position, below the text. A placement is a terminal object, not the content of
a cell, so writing cells shows nothing by itself. The encoder therefore does its work once per
frame, from the whole screen:

1. **Cells are written on the default background.** `WriteCell` writes an image cell as an ordinary
   text cell, glyph or blank, with the terminal's default background (`WriteTextCell(…, true)`).
   Konsole draws below-text (z<0) placements under any explicit cell background, so painting the
   cell a colour, even black, hides the picture there; kitty draws them above either way.
2. **The screen is walked in blocks.** `EndFrame(screen)` goes over the buffer in blocks of 8×4
   cells (`BlockColumns`, `BlockRows`) aligned to the screen grid. A block with at least one image
   cell gets a content key: the XXH3 of its cells' image digests, a zero marker for a cell without
   one.
   - **Why blocks.** Kitty cannot portably change part of an image once transmitted (the animation
     extension can, and WezTerm, Konsole and Ghostty do not implement it reliably), so the image is
     the unit of retransmission. One image per picture meant a brush stroke re-sent the whole
     picture. A block is the compromise between how many images a screen is (about 500 at 240×67)
     and how much a change re-sends (one block). The size is the encoder's constant and nothing
     outside it knows it.
3. **Each distinct block is transmitted once.** `_blockImages`, a `ContentCache<int>`, maps content
   to the terminal's image id. A block already in the terminal is not sent again; blocks that look
   alike, such as a blank canvas, share one image. A new block is sent as RGBA with zlib
   (`TransmitBlock`): its cells' pixels side by side, a cell without an image transparent.
4. **One placement per block.** The block is placed at its cells, z=-2 (below text, above
   background colours), `C=1` so the cursor doesn't move. The image is the block, pre-scaled to the
   cell grid, so the placement is the whole image and needs no crop or stretch.
5. **Placements are diffed across frames.** Live placements are a `Dictionary<(ImageId, X, Y), int>`.
   An unchanged block costs nothing. A new one is placed. A vanished one is deleted by placement id
   (`a=d,d=i`), and the image stays for cheap re-placement. The pass runs every frame; a screen whose
   blocks did not change costs the scan and sends nothing.

## Translucent overlays: washes

The colour of an image cell is the **paint** over the picture: transparent to start with, and
`Pixel.Blend` merges every translucent overlay into it while keeping the image. A modal backdrop
dimming a picture is such a paint.

- **Wash placement.** Every maximal rectangle of image cells under one visible paint
  (`CellRectangles.GrowRectangle`) gets a second placement, at z=-1 (above the picture, below text),
  of a solid image in that colour.
- **Wash image.** One pixel per cell of the screen, zlib compressed (`BuildTransmitWashSequence`).
  It is a few hundred bytes whatever the screen size.
  - Why one pixel per cell: a single pixel stretched with `c=`/`r=` works on kitty, but terminals
    that map every cell to whole source pixels drop a placement whose cells get less than one pixel.
  - Wash images are kept by colour. One is freed once no placement uses it
    (`FreeUnusedWashImages`), and sent again sized up when the screen grows
    (`DropWashImagesSmallerThan`).
- **Mouse cursor.** The cursor glyph is written into the cell, which keeps its image; its contrast
  colour is computed against `PixelBackground.EffectiveColor`, the paint over the picture's dominant
  colour.

## Image ids and their lifetime

- **Ids.** Image and placement ids come from `KittyGraphics.AllocateImageId` and
  `AllocatePlacementId`. They count through 1..int.MaxValue and wrap within that range. A wide
  range matters because retransmitting an id the terminal still holds deletes that image and its
  placements.
- **Registry.** `TransmittedImages` records every id transmitted and not deleted since.
  `BuildDeleteSequence` removes an id from it.
- **Block budget.** At least 24 MB of block pixels (`MinBlockImageBudgetBytes`), or three screens'
  worth (`ScreensOfBlockImages`) on a larger screen. One full-screen picture therefore never evicts
  its own blocks while transmitting them, and a picture shown again within a few screens is placed,
  not resent. The terminal has to keep at least as much: XTerm.NET keeps the same three screens
  (`MaxImageRegistryBytes`). If the terminal dropped blocks that Consolonia still counts on, placing
  them again would show nothing.
- **Eviction is deferred.** A block evicted from the cache is not deleted immediately. A picture
  that isn't being redrawn may still show it, and deleting the image (`d=I`) takes its placements
  with it. Evicted ids go to `_evictedBlockImages`, and `DeleteOrphanedBlockImages` deletes each one
  once no live placement shows it (`SelectOrphans`).

### Resize, resume and exit

- **Resize, and resume after another program had the terminal.** `RenderTarget.ResetCache` calls
  the console's `ResetCache`, and the encoder's `Reset` moves every live placement to
  `_placementsToDeleteAfterFrame`. The frame places everything again from the cells, and
  `DeleteWhatTheFrameReplaced` (the end of `EndFrame`) deletes the old placements at its end. The
  images stay in the terminal, because the alternate screen was never left and nothing but this app
  deletes by id. A resumed picture is placed again from blocks already there. This was the fix for a
  black flash on resume: deleting and retransmitting left the cells empty until the new images
  decoded.
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
- **RGBA with zlib.** Blocks are sent as raw RGBA, zlib compressed at the fastest level. The earlier
  renderer sent PNG through Avalonia/Skia, which needs a render interface the output does not have;
  the bytes per picture are in the benchmarks below. The PNG form of the sequence remains for a
  caller that has one.
- **`q=2`** suppresses the terminal's replies on every command.

## Benchmarks

`ConsoleOutputBenchmarks`, default job, in-process, writing to a null stream (see
[bitmap.md](bitmap.md#benchmarks)), driven as the render target drives the output: the changed
cells written, then `EndFrame`.

| Scenario | Time | Allocated | Bytes on the wire |
|---|---|---|---|
| New full-screen picture: every block transmitted and placed | 97 to 148 ms (noisy) | 13.6 MB | 3,254,735 |
| Every block placed again (after a reset; images in the terminal) | 2.0 ms | 135 KB | 62,718 |
| One cell: one block re-sent, one placement replaced | 83 µs | 40 B | 16,282 |

For comparison, before this model a full-screen transmit of 510 PNG tiles of 8 KB each cost 1.38 ms
and 32 KB allocated for 5,595,102 bytes on the wire, the PNG encoding not included.

A whole kitty frame on the unit test console (drawing into the pixel buffer and `RenderToDevice`,
nothing encoded) is `BitmapFrameBenchmarks`; the table is in [bitmap.md](bitmap.md#benchmarks).

## Tests

| Tests | Cover |
|---|---|
| `KittyGraphicsTests` | Sequence formats (single chunk, chunked reassembly, PNG without dimensions, placements, washes, deletes), ids staying positive past overflow, and the registry: transmitted images are deleted together unless deleted one by one. |
| `KittyImageLifetimeTests` | An evicted image nothing shows is an orphan; one still shown is deleted once its placement goes; the cache's budget. |
| `WithLifetimeFixture/AnsiConsoleOutputImageTests` | A block of cells is one image (RGBA, zlib) and one placement, written on the default background; a stroke re-sends and re-places the block it touched and deletes the old placement; paint over a picture is a wash image and placement, gone with the paint; after a reset everything is placed again before the old placements go. |
| `WithLifetimeFixture/CellImageBitmapRendererTests` | With the kitty capability, the same renderer tests as with sixel. |
| `AnsiConsoleOutputWriteTests` | A transmit written as bytes is the same as the one built as a string. |
