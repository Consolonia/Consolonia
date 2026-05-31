using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Consolonia.Core.Drawing.AnsiArt;
using Consolonia.Core.Infrastructure;
using Consolonia.Core.InternalHelpers;
using Consolonia.Core.Text;

namespace Consolonia.Core.Drawing
{
    internal class ConsoloniaRenderInterface : IPlatformRenderInterface
    {
        private readonly IPlatformRenderInterface _fallback;

        internal ConsoloniaRenderInterface(IPlatformRenderInterface fallback)
        {
            _fallback = fallback;
        }

        public IGeometryImpl CreateEllipseGeometry(Rect rect)
        {
            throw new NotImplementedException();
        }

        public IGeometryImpl CreateLineGeometry(Point p1, Point p2)
        {
            return Line.CreateMyLine(p1, p2);
        }

        public IGeometryImpl CreateRectangleGeometry(Rect rect)
        {
            return new Rectangle(rect);
        }

        public IStreamGeometryImpl CreateStreamGeometry()
        {
            return new StreamGeometryImpl();
        }

        public IGeometryImpl CreateGeometryGroup(FillRule fillRule, IReadOnlyList<IGeometryImpl> children)
        {
            throw new NotImplementedException();
        }

        public IGeometryImpl CreateCombinedGeometry(GeometryCombineMode combineMode, IGeometryImpl g1, IGeometryImpl g2)
        {
            // this is handcrafted to only combine single thickness line strokes for borders.
            // This needs to be much more robust to handle general cases.

            if (combineMode != GeometryCombineMode.Exclude)
                throw new NotImplementedException("Only GeometryCombineMode.Exclude is supported");

            if (g1 is not StreamGeometryImpl stream1 || g2 is not StreamGeometryImpl stream2)
                throw new ArgumentException("Only StreamGeometryImpl is supported");

            IStreamGeometryImpl newGeometry = CreateStreamGeometry();
            using IStreamGeometryContextImpl ctx = newGeometry.Open();
            // Resharper disable UnusedVariable
            bool hasLeftStroke = stream2.Bounds.X.IsNearlyEqual(1);
            bool hasTopStroke = stream2.Bounds.Y.IsNearlyEqual(1);
            bool hasRightStroke = (stream1.Bounds.Width - stream2.Bounds.Width).IsNearlyEqual(stream2.Bounds.X + 1);
            bool hasBottomStroke =
                (stream1.Bounds.Height - stream2.Bounds.Height).IsNearlyEqual(stream2.Bounds.Y + 1);
            Point topLeft = stream1.Bounds.TopLeft;
            Point topRight = stream1.Bounds.TopRight;
            Point bottomLeft = stream1.Bounds.BottomLeft;
            Point bottomRight = stream1.Bounds.BottomRight;
            Line topStroke = stream1.Strokes[0];
            Line rightStroke = stream1.Strokes[1];
            Line bottomStroke = stream1.Strokes[2];
            Line leftStroke = stream1.Strokes[3];
            // Resharper enable UnusedVariable

            // add "null" strokes to establish boundaries of box even when there is a single real stroke.
            AddStroke(ctx, topLeft, topLeft);
            AddStroke(ctx, bottomRight, bottomRight);

            if (hasTopStroke)
                AddStroke(ctx, topStroke.PStart, topStroke.PEnd + new Vector(-1, 0));
            if (hasRightStroke)
                AddStroke(ctx, rightStroke.PStart + new Vector(-1, 0), rightStroke.PEnd + new Vector(-1, -1));
            if (hasBottomStroke)
                AddStroke(ctx, bottomStroke.PStart + new Vector(0, -1), bottomStroke.PEnd + new Vector(-1, -1));
            if (hasLeftStroke)
                AddStroke(ctx, leftStroke.PStart, leftStroke.PEnd + new Vector(0, -1));

            return newGeometry;
        }

        private static void AddStroke(IStreamGeometryContextImpl ctx, Point start, Point end)
        {
            ctx.BeginFigure(start, false);
            ctx.LineTo(end);
            ctx.EndFigure(true);
        }

        public IGeometryImpl BuildGlyphRunGeometry(GlyphRun glyphRun)
        {
            throw new NotImplementedException();
        }

        public IRenderTargetBitmapImpl CreateRenderTargetBitmap(PixelSize size, Vector dpi)
        {
            throw new NotImplementedException();
            // return new ConsoleRenderTargetBitmapImpl(size, dpi);
        }

        public IWriteableBitmapImpl CreateWriteableBitmap(PixelSize size, Vector dpi, PixelFormat format,
            AlphaFormat alphaFormat)
        {
            if (_fallback != null)
            {
                IWriteableBitmapImpl bitmap = _fallback.CreateWriteableBitmap(size, dpi, format, alphaFormat);
                return new AspectRatioAdjustedBitmap(bitmap);
            }

            return ConsoloniaPlatform.RaiseNotSupported<IWriteableBitmapImpl>(
                NotSupportedRequestCode.BitmapsNotSupported, this,
                nameof(CreateWriteableBitmap));
        }

        public IBitmapImpl LoadBitmap(string fileName)
        {
            if (fileName.EndsWith(".ans", StringComparison.OrdinalIgnoreCase))
            {
                using FileStream stream = File.OpenRead(fileName);
                return new PixelBufferBitmapImpl(AnsiParser.Parse(stream));
            }

            if (_fallback != null)
            {
                IBitmapImpl bitmap = _fallback.LoadBitmap(fileName);
                return new AspectRatioAdjustedBitmap(bitmap);
            }

            return ConsoloniaPlatform.RaiseNotSupported<IBitmapImpl>(NotSupportedRequestCode.BitmapsNotSupported, this,
                nameof(LoadBitmap));
        }

        public IBitmapImpl LoadBitmap(Stream stream)
        {
            if (IsAnsi(stream))
                return new PixelBufferBitmapImpl(AnsiParser.Parse(stream));

            if (_fallback != null)
            {
                IBitmapImpl bitmap = _fallback.LoadBitmap(stream);
                return new AspectRatioAdjustedBitmap(bitmap);
            }

            return ConsoloniaPlatform.RaiseNotSupported<IBitmapImpl>(NotSupportedRequestCode.BitmapsNotSupported, this,
                nameof(LoadBitmap));
        }

        private static bool IsAnsi(Stream stream)
        {
            if (!stream.CanSeek) 
                return false;
            
            try
            {
                byte[] buffer = new byte[256];
                int read = stream.Read(buffer, 0, buffer.Length);
                for (int i = 0; i < read - 1; i++)
                {
                    if (buffer[i] == 0x1B && buffer[i + 1] == '[')
                        return true;
                }

                return false;
            }
            finally
            {
                stream.Position = 0;
            }
        }

        public IWriteableBitmapImpl LoadWriteableBitmapToHeight(Stream stream, int height,
            BitmapInterpolationMode interpolationMode)
        {
            if (_fallback != null)
            {
                IWriteableBitmapImpl bitmap = _fallback.LoadWriteableBitmapToHeight(stream, height, interpolationMode);
                return new AspectRatioAdjustedBitmap(bitmap);
            }

            return ConsoloniaPlatform.RaiseNotSupported<IWriteableBitmapImpl>(
                NotSupportedRequestCode.BitmapsNotSupported, this,
                nameof(LoadWriteableBitmapToHeight));
        }

        public IWriteableBitmapImpl LoadWriteableBitmapToWidth(Stream stream, int width,
            BitmapInterpolationMode interpolationMode = BitmapInterpolationMode.HighQuality)
        {
            if (_fallback != null)
            {
                IWriteableBitmapImpl bitmap = _fallback.LoadWriteableBitmapToWidth(stream, width, interpolationMode);
                return new AspectRatioAdjustedBitmap(bitmap);
            }

            return ConsoloniaPlatform.RaiseNotSupported<IWriteableBitmapImpl>(
                NotSupportedRequestCode.BitmapsNotSupported, this,
                nameof(LoadWriteableBitmapToWidth));
        }

        public IWriteableBitmapImpl LoadWriteableBitmap(string fileName)
        {
            if (_fallback != null)
            {
                IWriteableBitmapImpl bitmap = _fallback.LoadWriteableBitmap(fileName);
                return new AspectRatioAdjustedBitmap(bitmap);
            }

            return ConsoloniaPlatform.RaiseNotSupported<IWriteableBitmapImpl>(
                NotSupportedRequestCode.BitmapsNotSupported, this,
                nameof(LoadWriteableBitmap));
        }

        public IWriteableBitmapImpl LoadWriteableBitmap(Stream stream)
        {
            if (_fallback != null)
            {
                IWriteableBitmapImpl bitmap = _fallback.LoadWriteableBitmap(stream);
                return new AspectRatioAdjustedBitmap(bitmap);
            }

            return ConsoloniaPlatform.RaiseNotSupported<IWriteableBitmapImpl>(
                NotSupportedRequestCode.BitmapsNotSupported, this,
                nameof(LoadWriteableBitmap));
        }

        public IBitmapImpl LoadBitmapToWidth(Stream stream, int width, BitmapInterpolationMode interpolationMode)
        {
            if (_fallback != null)
            {
                IWriteableBitmapImpl bitmap = _fallback.LoadWriteableBitmapToWidth(stream, width, interpolationMode);
                return new AspectRatioAdjustedBitmap(bitmap);
            }

            return ConsoloniaPlatform.RaiseNotSupported<IBitmapImpl>(NotSupportedRequestCode.BitmapsNotSupported, this,
                nameof(LoadBitmapToWidth));
        }

        public IBitmapImpl LoadBitmapToHeight(Stream stream, int height, BitmapInterpolationMode interpolationMode)
        {
            if (_fallback != null)
            {
                IWriteableBitmapImpl bitmap = _fallback.LoadWriteableBitmapToHeight(stream, height, interpolationMode);
                return new AspectRatioAdjustedBitmap(bitmap);
            }

            return ConsoloniaPlatform.RaiseNotSupported<IBitmapImpl>(NotSupportedRequestCode.BitmapsNotSupported, this,
                nameof(LoadBitmapToHeight));
        }

        public IBitmapImpl ResizeBitmap(IBitmapImpl bitmapImpl, PixelSize destinationSize,
            BitmapInterpolationMode interpolationMode)
        {
            if (_fallback != null)
            {
                // Unwrap if it's already aspect-adjusted to get the actual bitmap
                IBitmapImpl sourceBitmap = bitmapImpl is AspectRatioAdjustedBitmap adjusted
                    ? adjusted.InnerBitmap
                    : bitmapImpl;

                IBitmapImpl bitmap = _fallback.ResizeBitmap(sourceBitmap, destinationSize, interpolationMode);
                return new AspectRatioAdjustedBitmap(bitmap);
            }

            return ConsoloniaPlatform.RaiseNotSupported<IBitmapImpl>(NotSupportedRequestCode.BitmapsNotSupported, this,
                nameof(ResizeBitmap));
        }

        public IBitmapImpl LoadBitmap(PixelFormat format, AlphaFormat alphaFormat, IntPtr data, PixelSize size,
            Vector dpi, int stride)
        {
            if (_fallback != null)
            {
                IBitmapImpl bitmap = _fallback.LoadBitmap(format, alphaFormat, data, size, dpi, stride);
                return new AspectRatioAdjustedBitmap(bitmap);
            }

            return ConsoloniaPlatform.RaiseNotSupported<IBitmapImpl>(NotSupportedRequestCode.BitmapsNotSupported, this,
                nameof(LoadBitmap));
        }

        public IGlyphRunImpl CreateGlyphRun(IGlyphTypeface glyphTypeface,
            double fontRenderingEmSize,
            IReadOnlyList<GlyphInfo> glyphInfos,
            Point baselineOrigin)
        {
            return new GlyphRunImpl(glyphTypeface, fontRenderingEmSize, glyphInfos, baselineOrigin);
        }

        public IPlatformRenderInterfaceContext CreateBackendContext(IPlatformGraphicsContext graphicsApiContext)
        {
            if (graphicsApiContext != null)
                throw new NotImplementedException("Investigate this cases");
            return new ConsoloniaPlatformRenderInterfaceContext();
        }

        public bool IsSupportedBitmapPixelFormat(PixelFormat format)
        {
            if (_fallback != null)
                return _fallback.IsSupportedBitmapPixelFormat(format);
            return false;
        }

        public IPlatformRenderInterfaceRegion CreateRegion()
        {
            throw new NotImplementedException();
        }

        public bool SupportsIndividualRoundRects => false;

        public AlphaFormat DefaultAlphaFormat
        {
            get
            {
                if (_fallback != null)
                    return _fallback.DefaultAlphaFormat;
                return AlphaFormat.Opaque;
            }
        }

        public PixelFormat DefaultPixelFormat
        {
            get
            {
                if (_fallback != null)
                    return _fallback.DefaultPixelFormat;
                return PixelFormat.Bgra8888;
            }
        }

        public bool SupportsRegions => false;
    }
}