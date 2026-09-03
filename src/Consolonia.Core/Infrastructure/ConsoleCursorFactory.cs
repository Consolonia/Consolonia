using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Consolonia.Core.Infrastructure
{
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
    internal sealed class ConsoleCursorFactory : ICursorFactory
    {
        private readonly Dictionary<StandardCursorType, CursorImpl> _standardCursors = new();

        public ICursorImpl GetCursor(StandardCursorType cursorType)
        {
            lock (_standardCursors)
            {
                if (_standardCursors.TryGetValue(cursorType, out CursorImpl cursor))
                    return cursor;
                cursor = new CursorImpl(cursorType);
                _standardCursors.Add(cursorType, cursor);
                return cursor;
            }
        }

        public ICursorImpl CreateCursor(Bitmap cursor, PixelPoint hotSpot)
        {
            throw new NotSupportedException("Consolonia doesn't support bitmap based cursors");
        }
    }

    // ReSharper disable GCSuppressFinalizeForTypeWithoutDestructor
    public sealed class CursorImpl : ICursorImpl
    {
        public CursorImpl(StandardCursorType cursorType)
        {
            CursorType = cursorType;
        }

        public StandardCursorType CursorType { get; init; }

        public void Dispose()
        {
            // No unmanaged resources to dispose
            GC.SuppressFinalize(this);
        }
    }
}