//DUPFINDER_ignore
//todo: this file is under refactoring. Restore the duplication finder

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Consolonia.Controls;
using Consolonia.Core.Drawing.PixelBufferImplementation;

namespace Consolonia.Core.Drawing
{
    /// <summary>
    ///     Renders a bitmap via the kitty graphics protocol. Pixels are transmitted to the terminal once
    ///     per bitmap version; every covered cell then references the image - as a background tile in
    ///     rect-placement mode, or as a unicode placeholder cell (U+10EEEE plus row/column diacritics,
    ///     image id in the foreground color) - so pixel buffer diffing and occlusion work unchanged
    ///     while redraws cost no pixel retransmission.
    /// </summary>
    internal sealed class KittyBitmapRenderer : BitmapRenderer
    {
        private static readonly
            ConditionalWeakTable<IBitmapImpl, Dictionary<BitmapQuantizedCacheKey, KittyRenderedBitmap>>
            RenderedBitmapCache = new();

        // placements larger than the placeholder diacritics can address fall back to this renderer
        private readonly BitmapRenderer _oversizeFallbackRenderer;

        // Classic rect-placement mode: every covered cell carries a KittyTile in its BACKGROUND,
        // which RenderTarget coalesces into placements at z=-1, drawn below text - so glyphs
        // composite over the picture, and painting an opaque background evicts it.
        private readonly bool _placementMode;

        public KittyBitmapRenderer(DrawingContextImpl context, bool placementMode = false)
            : base(context)
        {
            _placementMode = placementMode;
            _oversizeFallbackRenderer =
                context.ConsoleWindowImpl.Console.Capabilities.HasFlag(ConsoleCapabilities.SupportsSixel)
                    ? new SixelBitmapRenderer(context)
                    : new QuadPixelBitmapRenderer(context);
        }

        public override void Draw(IBitmapImpl source, IPlatformRenderInterface renderInterface,
            PixelRect targetRect, PixelRect intersectedRect)
        {
            // the diacritic table bounds placeholder addressing only; rect placements crop pixels
            if (!_placementMode &&
                (targetRect.Width > KittyGraphics.MaxPlacementSize ||
                 targetRect.Height > KittyGraphics.MaxPlacementSize))
            {
                _oversizeFallbackRenderer.Draw(source, renderInterface, targetRect, intersectedRect);
                return;
            }

            int cellPixelWidth = Context.ConsoleWindowImpl.Console.CellPixelWidth;
            int cellPixelHeight = Context.ConsoleWindowImpl.Console.CellPixelHeight;

            var targetSize = new PixelSize(targetRect.Width * cellPixelWidth,
                targetRect.Height * cellPixelHeight);
            var visibleRectInTarget = new PixelRect(
                intersectedRect.X - targetRect.X,
                intersectedRect.Y - targetRect.Y,
                intersectedRect.Width,
                intersectedRect.Height);

            PixelBuffer cellBuffer =
                GetOrCreateCellBuffer(source, renderInterface, targetRect, targetSize);

            CopyRenderedBitmapTrackingDirtyRegions(cellBuffer, intersectedRect, visibleRectInTarget);
        }

        private PixelBuffer GetOrCreateCellBuffer(IBitmapImpl source,
            IPlatformRenderInterface renderInterface, PixelRect targetRect, PixelSize targetSize)
        {
            IBitmapImpl cacheSource = GetCacheBitmapImpl(source);
            Dictionary<BitmapQuantizedCacheKey, KittyRenderedBitmap> perBitmap =
                RenderedBitmapCache.GetOrCreateValue(cacheSource);
            var key = new BitmapQuantizedCacheKey(cacheSource.Version, targetSize);

            if (perBitmap.TryGetValue(key, out KittyRenderedBitmap renderedBitmap))
            {
                // the placement is deleted when the image leaves the screen (see RenderTarget);
                // re-creating it costs no pixel retransmission. Rect mode has no virtual placement.
                if (!_placementMode && KittyGraphics.TryReclaimPlacement(renderedBitmap.ImageId))
                    Context.ConsoleWindowImpl.Console.WriteText(
                        KittyGraphics.BuildVirtualPlacementSequence(renderedBitmap.ImageId,
                            targetRect.Width, targetRect.Height));

                return renderedBitmap.Cells;
            }

            // A new version (next animation frame) reuses the image id and placeholder buffer of the
            // same-size previous version: retransmitting under an existing id replaces the pixels
            // while placement and cells stay valid, so the diff re-emits no cells. Stale versions of
            // other sizes are deleted; GC'd bitmaps are cleaned up by KittyDeleteAllImages on restore.
            KittyRenderedBitmap reusableBitmap = null;
            List<BitmapQuantizedCacheKey> staleKeys = null;
            foreach (KeyValuePair<BitmapQuantizedCacheKey, KittyRenderedBitmap> pair in perBitmap)
                if (pair.Key.Version != cacheSource.Version)
                {
                    // Rect mode cannot reuse an id across versions: a classic placement binds to
                    // the image it was created against, so a fresh id is needed to re-key the tiles.
                    if (!_placementMode && reusableBitmap == null && pair.Key.TargetSize.Equals(targetSize))
                    {
                        reusableBitmap = pair.Value;
                    }
                    else
                    {
                        Context.ConsoleWindowImpl.Console.WriteText(
                            KittyGraphics.BuildDeleteSequence(pair.Value.ImageId));
                        KittyGraphics.TryReclaimPlacement(pair.Value.ImageId);
                    }

                    (staleKeys ??= new List<BitmapQuantizedCacheKey>()).Add(pair.Key);
                }

            if (staleKeys != null)
                foreach (BitmapQuantizedCacheKey staleKey in staleKeys)
                    perBitmap.Remove(staleKey);

            if (reusableBitmap != null)
            {
                byte[] imageData = ExtractImageData(source, renderInterface, targetSize,
                    out KittyImageFormat imageFormat);
                Context.ConsoleWindowImpl.Console.WriteText(KittyGraphics.BuildTransmitSequence(
                    reusableBitmap.ImageId, targetSize.Width, targetSize.Height, imageData, imageFormat));
                if (!_placementMode && KittyGraphics.TryReclaimPlacement(reusableBitmap.ImageId))
                    Context.ConsoleWindowImpl.Console.WriteText(
                        KittyGraphics.BuildVirtualPlacementSequence(reusableBitmap.ImageId,
                            targetRect.Width, targetRect.Height));

                perBitmap[key] = reusableBitmap;
                return reusableBitmap.Cells;
            }

            renderedBitmap = TransmitAndCreateCells(source, renderInterface, targetRect, targetSize);
            perBitmap[key] = renderedBitmap;
            return renderedBitmap.Cells;
        }

        private KittyRenderedBitmap TransmitAndCreateCells(IBitmapImpl source,
            IPlatformRenderInterface renderInterface, PixelRect targetRect, PixelSize targetSize)
        {
            byte[] imageData = ExtractImageData(source, renderInterface, targetSize,
                out KittyImageFormat imageFormat);

            int imageId = KittyGraphics.AllocateImageId();
            Context.ConsoleWindowImpl.Console.WriteText(
                KittyGraphics.BuildTransmitSequence(imageId, targetSize.Width, targetSize.Height, imageData,
                    imageFormat));

            var cellBuffer = new PixelBuffer((ushort)targetRect.Width, (ushort)targetRect.Height);

            if (_placementMode)
            {
                // image as cell BACKGROUND, foreground left free so glyphs drawn later composite
                // over the picture. The background color starts transparent: it is the wash that
                // translucent overlays accumulate, which RenderTarget lays over the image, and the
                // terminal cell itself is written black (occluding what the picture was drawn over)
                for (int cellY = 0; cellY < targetRect.Height; cellY++)
                for (int cellX = 0; cellX < targetRect.Width; cellX++)
                    cellBuffer[new PixelPoint(cellX, cellY)] = new Pixel(
                        new PixelForeground(Symbol.Space, Colors.Transparent),
                        new PixelBackground(Colors.Transparent,
                            new KittyTile(imageId, (ushort)cellX, (ushort)cellY)));

                return new KittyRenderedBitmap(imageId, cellBuffer);
            }

            Context.ConsoleWindowImpl.Console.WriteText(
                KittyGraphics.BuildVirtualPlacementSequence(imageId, targetRect.Width, targetRect.Height));

            Color imageIdColor = KittyGraphics.GetImageIdColor(imageId);
            for (int cellY = 0; cellY < targetRect.Height; cellY++)
            for (int cellX = 0; cellX < targetRect.Width; cellX++)
                cellBuffer[new PixelPoint(cellX, cellY)] = new Pixel(
                    new PixelForeground(
                        Symbol.FromVerbatim(KittyGraphics.GetPlaceholderCell(cellY, cellX), 1),
                        imageIdColor),
                    PixelBackground.Transparent);

            return new KittyRenderedBitmap(imageId, cellBuffer);
        }

        private static byte[] ExtractImageData(IBitmapImpl source, IPlatformRenderInterface renderInterface,
            PixelSize targetSize, out KittyImageFormat format)
        {
            using IBitmapImpl resizedBitmap = !source.PixelSize.Equals(targetSize)
                ? renderInterface.ResizeBitmap(source, targetSize, BitmapInterpolationMode.MediumQuality)
                : null;

            IBitmapImpl bitmapToRead = resizedBitmap ?? source;

            // PNG is far smaller on the wire than raw RGBA (a full screen image is ~7MB raw, over
            // 9MB base64), which matters for the first paint and for every animation frame
            byte[] png = TryEncodePng(bitmapToRead);
            if (png != null)
            {
                format = KittyImageFormat.Png;
                return png;
            }

            // fallback for bitmap implementations which cannot encode themselves
            format = KittyImageFormat.Rgba;
            var readableBitmap = (IReadableBitmapImpl)bitmapToRead;

            using ILockedFramebuffer frameBuffer = readableBitmap.Lock();
            unsafe
            {
                ReadOnlySpan<byte> pixelBytes = MemoryMarshal.CreateReadOnlySpan(
                    ref Unsafe.AsRef<byte>((void*)frameBuffer.Address),
                    frameBuffer.RowBytes * frameBuffer.Size.Height);

                return ConvertBgraToRgba(pixelBytes, frameBuffer.RowBytes, targetSize);
            }
        }

        private static byte[] TryEncodePng(IBitmapImpl bitmap)
        {
            try
            {
                // Avalonia saves as PNG via Skia; the bitmap is already scaled to the target size
                using var stream = new MemoryStream();
                bitmap.Save(stream);
                return stream.ToArray();
            }
            catch (Exception exception) when (exception is NotSupportedException or NotImplementedException)
            {
                return null;
            }
        }

        private static byte[] ConvertBgraToRgba(ReadOnlySpan<byte> bgra, int rowBytes, PixelSize size)
        {
            byte[] rgba = GC.AllocateUninitializedArray<byte>(size.Width * size.Height * 4);
            for (int row = 0; row < size.Height; row++)
            {
                int sourceOffset = row * rowBytes;
                int targetOffset = row * size.Width * 4;
                for (int x = 0; x < size.Width; x++)
                {
                    rgba[targetOffset] = bgra[sourceOffset + 2];
                    rgba[targetOffset + 1] = bgra[sourceOffset + 1];
                    rgba[targetOffset + 2] = bgra[sourceOffset];
                    rgba[targetOffset + 3] = bgra[sourceOffset + 3];
                    sourceOffset += 4;
                    targetOffset += 4;
                }
            }

            return rgba;
        }

        private sealed class KittyRenderedBitmap
        {
            public KittyRenderedBitmap(int imageId, PixelBuffer cells)
            {
                ImageId = imageId;
                Cells = cells;
            }

            public int ImageId { get; }

            public PixelBuffer Cells { get; }
        }
    }
}