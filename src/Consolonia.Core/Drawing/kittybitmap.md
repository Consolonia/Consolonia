# Kitty graphics: blocks sent when they change

Architecture notes for `KittyConsoleImageEncoder` and `KittyGraphics` (both in
`Consolonia.Core.Infrastructure`). The pipeline shared with the other protocols is in
[bitmap.md](bitmap.md). The protocol is specified at
https://sw.kovidgoyal.net/kitty/graphics-protocol/.

## How it works

The kitty protocol transmits an image under an id and shows it through **placements**: an image put
at a cell position, below the text. A placement is a terminal object, not the content of a cell, so
writing cells shows nothing by itself. The encoder uses the protocol the way the sixel encoder uses
sixel: pixels are sent when they change, and nothing is remembered about the terminal.

1. **Cells are written on the default background.** `WriteCell` writes an image cell as an ordinary
   text cell with the terminal's default background (`WriteTextCell(…, true)`), and a cell with no
   symbol as a space. Text sits above the placement, so the picture shows through spaces.
   Konsole draws below-text (z<0) placements under any explicit cell background, so painting the
   cell a colour, even black, hides the picture there; kitty draws them above either way. A glyph
   over a picture is drawn by the terminal over the picture, so no dominant-colour fallback is needed.
2. **The screen is walked in blocks.** `EndFrame(screen)` goes over the buffer in blocks of 8×4
   cells (`BlockColumns`, `BlockRows`) aligned to the screen grid. A block with at
   least one image cell gets a key: the XXH3 of each cell's image digest and paint, zero for a cell
   without an image. The key changes when a cell's picture changes, when its paint changes, and
   when a cell stops or starts showing a picture.
   - **Why blocks.** Kitty cannot portably change part of an image once transmitted (the animation
     extension can, and WezTerm, Konsole and Ghostty do not implement it reliably), so the image is
     the unit of retransmission. One image per picture meant a brush stroke re-sent the whole
     picture. A block is the compromise between how many images a screen is (about 500 at 240×67)
     and how much a change re-sends (one block). The size is the encoder's constant and nothing
     outside it knows it. Measured across sizes (`ConsoleOutputBenchmarks`, zlib fallback, quiet
     machine), the full-screen cost flattens out at 8×4 while the cost of a stroke keeps growing
     with the block, so 8×4 is the knee; 4×2 is the alternative if strokes matter more than loads:

     | Block | Images per screen | New full-screen picture | One brush stroke |
     |---|---|---|---|
     | 1×1 | 16,080 | 273 ms, 6.08 MB | 1.1 ms, 0.7 KB |
     | 4×2 | 2,010 | 125 ms, 3.52 MB | 381 µs, 2.3 KB |
     | 8×4 | 503 | 82 ms, 3.24 MB | 425 µs, 7.1 KB |
     | 16×8 | 128 | 79 ms, 3.22 MB | 734 µs, 25.6 KB |

     Per cell every block carries its own header, base64 padding and placement, and every frame
     walks 16,080 blocks instead of 503.
3. **One image id per block position, for the session.** `_blocks` holds, per position, the id
   allocated for it and the key last sent under it. A block whose key is unchanged costs nothing. A
   block whose key changed is **transmitted again under its id**: the protocol replaces the image
   the terminal holds under that id and every placement of it, so the old picture is gone with no
   delete. A block with no image cells left has its image deleted (`a=d,d=I`), and blocks beyond a
   screen that shrank are deleted too. The terminal therefore never holds more images than the
   screen has blocks.
4. **Placements cover image cells only, placed right after the transmit.** Within the block the
   image cells are grown into maximal rectangles (`GrowPlacement`), and each is placed
   at its cells as a crop of the block's image (`x=`, `y=`, `w=`, `h=` in pixels; placement ids 1, 2,
   … within the image), z=-2 (below text, above background colours), `C=1` so the cursor doesn't
   move. A block the picture fills is one placement; a block with a dialog's corner in it is a few.
   A text cell is never under a placement: a placement below the text is drawn above the cells'
   background colours, and a terminal need not show a cell's colour through the image's
   transparent pixels. Placing whole blocks put the picture over a dialog's body and left only its
   border glyphs.
5. **Paint is in the pixels.** The colour of an image cell is the paint over the picture (a modal
   backdrop dimming it, a shade); `TransmitBlock` composites it into the cell's pixels before
   encoding, exactly as the sixel encoder does. A dimmed block is a different block and is sent
   again; so is the block when the dialog closes.
6. **The pixels.** A block is sent as PNG through the platform's bitmaps when there are any (a
   bitmap loaded from the pixels and saved, which Avalonia does through Skia; every kitty terminal
   decodes PNG, and a photograph is half the bytes), as RGBA with zlib level 1 otherwise: the
   headless tests, the benchmarks, a platform without an image codec. Level 6 saved a quarter of
   the bytes on photographs and cost more than twice the time per block; this is the fallback.

## Why nothing is remembered about the terminal

The earlier encoder kept every transmitted block by content, placed blocks again by id when they
came back into view, kept washes as separate placements, and tracked evictions, orphans and
deferred deletes to keep its picture of the terminal true. Two things broke it: dragging a dialog
across a picture made a new block image for every block along its edges on every frame, which
filled the terminal's image registry in seconds; and a terminal that then trimmed a block still on
screen (XTerm.NET did, oldest first) left a hole wherever that block had to be placed again,
because the placement named an id the terminal no longer had. The protocol gives a client no way to
learn that an image is gone except asking for error replies (`q=1`) and parsing them from the
input, which Consolonia does not do.

Sending under a fixed id per position removes the belief instead of defending it. Nothing is ever
placed that was not transmitted in the same frame, so no placement can name an image the terminal
dropped; the set of images is bounded by the screen; and the only state is what each position was
last sent, which is the encoder's own. What it costs against the earlier encoder:

| Case | Earlier encoder | This one |
|---|---|---|
| New picture, brush stroke | one block | the same |
| Dialog dragged across the picture | nothing (crops only) | the blocks along the dialog's edges again, each frame |
| Modal dims or undims the picture | a few KB of wash placements | every covered block again, about half a megabyte at full HD |
| Resize, resume | placed again from retained images | the screen again |
| A terminal that drops an image on screen | a hole until the block's content changes | the same, and nothing else can make one |

## Image ids and their lifetime

- **Ids.** Image ids come from `KittyGraphics.AllocateImageId`, once per block position, and
  placement ids count from 1 within each image. Ids count through 1..int.MaxValue and wrap within
  that range.
- **Registry.** `TransmittedImages` records every id transmitted and not deleted since.
  `BuildDeleteSequence` removes an id from it.
- **Resize, and resume after another program had the terminal.** `RenderTarget.ResetCache` calls
  the console's `ResetCache`, and the encoder's `Reset` forgets what every position was sent. The
  next frame sends every block again under its id, which replaces whatever the terminal still
  holds for it: where the terminal still showed the picture, the new one is there before the old
  one goes, since a transmit replaces rather than deletes first.
- **Exit.** `RestoreConsole` writes `BuildDeleteTransmittedImagesSequence()`, which deletes every
  image in the registry one by one, and then `DeleteAllImages` (`a=d,d=A`). `d=A` alone deletes only
  visible placements. It would leave the images in the terminal's memory.

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
- **PNG first, RGBA with zlib otherwise.** On three Gallery photos at full HD, cut into 64×64
  blocks, 0.29 to 0.59 MB as PNG against 0.61 to 1.32 MB as zlib level 1 (0.39 to 1.00 MB at
  level 6, which costs more than twice the time per block and is not used).
- **`q=2`** suppresses the terminal's replies on every command.

## Benchmarks

`ConsoleOutputBenchmarks`, default job, in-process, writing to a null stream (see
[bitmap.md](bitmap.md#benchmarks)), driven as the render target drives the output: the changed
cells written, then `EndFrame`. The benchmark has no platform, so its blocks go as zlib RGBA.

| Scenario | Time | Allocated (`--alloc`) | Bytes on the wire |
|---|---|---|---|
| New full-screen picture: every block transmitted and placed | 82 ms | 208 KB | 3,237,839 |
| After a reset: every block sent again (the earlier encoder placed them again: 2.0 ms, 62,718 bytes) | 79 ms | 208 KB | 3,232,625 |
| One cell: one block re-sent and placed | 290 µs | 1 KB | 7,100 |

What a block allocates is its `ZLibStream`: the block's pixels, the encoded bytes and the escape
sequence go through buffers the encoder and the output keep (see [bitmap.md](bitmap.md#memory)).
Before that, a full screen allocated 5.2 MB in a `MemoryStream` and a copy of its bytes per block.

A whole kitty frame on the unit test console (drawing into the pixel buffer and `RenderToDevice`,
nothing encoded) is `BitmapFrameBenchmarks`; the table is in [bitmap.md](bitmap.md#benchmarks).

## Tests

| Tests | Cover |
|---|---|
| `KittyGraphicsTests` | Sequence formats (single chunk, chunked reassembly, PNG without dimensions, placements, deletes), ids staying positive past overflow, and the registry: transmitted images are deleted together unless deleted one by one. |
| `WithLifetimeFixture/AnsiConsoleOutputImageTests` | A block of cells is one image and one placement, written on the default background; only the image cells of a block are placed, as crops; a stroke re-sends the block under the same id with no delete; paint is sent in the pixels and undimming sends the block again as it was; a reset sends everything again under the same ids; a block with no picture left has its image deleted; a dialog dragged across a picture leaves the live placements covering exactly the image cells after every frame, with only the blocks along its edges re-sent. |
| `WithLifetimeFixture/CellImageBitmapRendererTests` | With the kitty capability, the same renderer tests as with sixel. |
| `AnsiConsoleOutputWriteTests` | A transmit written as bytes is the same as the one built as a string. |
