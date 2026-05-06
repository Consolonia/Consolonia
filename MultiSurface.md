# Plan: Child Window PixelBuffer Architecture

## Context

The current `ManagedWindowImpl` uses a content-adoption hack: it rips content from the Avalonia `Window`, moves it into a `ManagedWindow` control in the main window's visual tree, and makes the original Window a zombie (opacity=0, LayoutManager disposed). This breaks:
- Keyboard input routing (Escape, Enter, IsCancel/IsDefault)
- Focus management
- Window-level event handlers (OnOK, OnCancel)
- The Avalonia Window's PresentationSource becomes dead — `IWindowImpl.Input` is never called

The fix: give each child window its own `PixelBuffer`, let it render normally through Avalonia's pipeline, and composite all buffers into the final console output.

## Architecture Overview

```
Console Screen (final output)
    ↑ composite during RenderToDevice()
    ├── Main ConsoleWindowImpl PixelBuffer (background app + ManagedWindow chrome)
    ├── Child 1 PixelBuffer (dialog content, positioned at ManagedWindow content area)
    └── Child 2 PixelBuffer (nested dialog, higher z-order)
```

Each child window is a fully functional Avalonia TopLevel with its own PresentationSource, CompositingRenderer, and RenderTarget — all writing to its own PixelBuffer. ManagedWindow still provides chrome (title bar, borders, buttons) and z-order in the main window's visual tree.

---

## Phase 1: Surface Abstraction (refactor only, no behavior change)

Decouple `DrawingContextImpl` and `RenderTarget` from `ConsoleWindowImpl` so they can work with any pixel buffer owner.

### Step 1.1: Create `IPixelBufferSurface`
**New file:** `src/Consolonia.Core/Infrastructure/IPixelBufferSurface.cs`

```csharp
public interface IPixelBufferSurface
{
    PixelBuffer PixelBuffer { get; }
    Snapshot.Regions DirtyRegions { get; }
    IConsole Console { get; }  // for capabilities checks only
    event Action<Size, WindowResizeReason> Resized;
    event Action<ConsoleCursor> CursorChanged;
    event Action ClearScreenRequested;
}
```

### Step 1.2: `ConsoleWindowImpl` implements `IPixelBufferSurface`
**File:** `src/Consolonia.Core/Infrastructure/ConsoleWindow.cs`
- Add `: IPixelBufferSurface` to class declaration
- `DirtyRegions` (line 64), `Console` (line 30), `PixelBuffer` (line 66) already exist — adjust visibility for the interface
- Events `Resized`, `CursorChanged`, `ClearScreenRequested` already exist

### Step 1.3: `DrawingContextImpl` accepts `IPixelBufferSurface`
**Files:**
- `src/Consolonia.Core/Drawing/DrawingContextImpl.cs` — change `_consoleWindowImpl` field to `IPixelBufferSurface`
- `src/Consolonia.Core/Drawing/DrawingContextImpl.Boxes.cs` — update references
- `src/Consolonia.Core/Drawing/DrawingContextImpl.Bitmaps.cs` — update references

All usages are: `.DirtyRegions.AddRect()`, `.PixelBuffer`, `.Console.Capabilities`

### Step 1.4: `RenderTarget` accepts `IPixelBufferSurface`
**File:** `src/Consolonia.Core/Drawing/RenderTarget.cs`
- Change `_consoleTopLevelImpl` field (line 22) to `IPixelBufferSurface`
- Constructor (line 35): accept `IPixelBufferSurface`
- Surfaces constructor (line 58): `surfaces.OfType<IPixelBufferSurface>().Single()`
- `Buffer` property (line 64): `_surface.PixelBuffer`
- `CreateDrawingContext` (line 106): `new DrawingContextImpl(_surface)`

### Verification
Run existing tests — all 40 should pass. No behavior change.

---

## Phase 2: ChildWindowSurface + Refactored ManagedWindowImpl

### Step 2.1: Create `ChildWindowSurface`
**New file:** `src/Consolonia.Core/Infrastructure/ChildWindowSurface.cs`

Implements `IPixelBufferSurface`. Owns its own `PixelBuffer`. Key properties:
- `PixelBuffer` — sized to child window's content area
- `DirtyRegions` — independent tracking
- `Console` — shared reference from main window (for capabilities)
- `Position` (PixelPoint) — screen-space position of content area
- `ZIndex` (int) — for compositing order
- `MainWindow` (ConsoleWindowImpl) — reference for dirty region forwarding
- `Resize(width, height)` — recreates PixelBuffer

### Step 2.2: Child window registry on `ConsoleWindowImpl`
**File:** `src/Consolonia.Core/Infrastructure/ConsoleWindow.cs`

Add:
- `List<ChildWindowSurface> _childSurfaces`
- `RegisterChildSurface(ChildWindowSurface)` / `UnregisterChildSurface(ChildWindowSurface)`

### Step 2.3: Refactor `ManagedWindowImpl`
**File:** `src/Consolonia.ManagedWindows/ManagedWindowImpl.cs`

Major changes:
- **Delete** `AdoptContentFromSource()` entirely (lines 113-181)
- **Delete** `_contentAdopted` field
- **Create** `ChildWindowSurface` in constructor, not sharing `_mainWindow.Surfaces`
- **`Surfaces`** → return `[_childSurface]` (own surface, not main window's)
- **`Compositor`** → keep sharing `_mainWindow.Compositor` (correct)
- **`Show()`** → register surface with ConsoleWindowImpl, bind position/size, call `base.Show()`/`base.ShowDialog()` for chrome only
- **`Dispose()`** → unregister surface
- **ManagedWindow Content** → set to a transparent `Panel` placeholder sized to match the child window. Chrome renders around it. The actual content renders to the child's PixelBuffer and is composited later.
- **Property bindings** (title, background, etc.) → keep flowing from Avalonia Window to ManagedWindow for chrome display

The Avalonia Window **keeps its own content** and renders normally through its PresentationSource.

---

## Phase 3: Compositing

### Step 3.1: Dual `Blit()` behavior in `RenderTarget`
**File:** `src/Consolonia.Core/Drawing/RenderTarget.cs`

When `Blit()` is called on a child surface's RenderTarget:
- Do NOT call `RenderToDevice()` (child doesn't write to console directly)
- Mark the child's screen-space rectangle as dirty in the main window's `DirtyRegions`
- The main window's next `RenderToDevice()` will pick up the changes

```csharp
void IDrawingContextLayerImpl.Blit(IDrawingContextImpl context)
{
    if (_surface is ChildWindowSurface child)
    {
        // Forward dirty region to main window
        child.MainWindow.DirtyRegions.AddRect(
            new PixelRect(child.Position.X, child.Position.Y,
                          child.PixelBuffer.Width, child.PixelBuffer.Height));
    }
    else
    {
        RenderToDevice();
    }
}
```

### Step 3.2: Composite child buffers in `RenderToDevice()`
**File:** `src/Consolonia.Core/Drawing/RenderTarget.cs`

In the pixel iteration loop (lines 155-250), after getting the main buffer pixel, check child surfaces:

```
for each (x, y):
    pixel = mainBuffer[x, y]
    
    // Check child surfaces in z-order (highest first)
    for each childSurface in reverse z-order:
        localX = x - child.Position.X
        localY = y - child.Position.Y
        if (localX, localY) is within child.PixelBuffer bounds:
            childPixel = child.PixelBuffer[localX, localY]
            if childPixel is not transparent:
                pixel = childPixel
            break  // topmost child wins
    
    // Continue with existing pixel processing (cursor, cache, WritePixel)
```

This requires `RenderTarget` to access the child surface list. Pass it through `IPixelBufferSurface` or have `ConsoleWindowImpl` (as the main surface) provide its child list.

### Step 3.3: Handle child dirty regions
When checking `dirtyRegions.Contains(x, y)`, also check if any child surface covering (x, y) has dirty regions. A pixel needs redrawing if it's dirty in the main buffer OR in any overlapping child buffer.

---

## Phase 4: Input Routing

### Step 4.1: Route keyboard input to active child
**File:** `src/Consolonia.Core/Infrastructure/ConsoleWindow.cs`

In `ConsoleOnKeyEvent()` (line 420) and `ConsoleOnTextInputEvent()` (line 408):
- Check if there's an active child window (topmost registered ChildWindowSurface)
- If yes, build `RawKeyEventArgs`/`RawTextInputEventArgs` and fire that child's `IWindowImpl.Input` callback instead of `this.Input`
- If no active child, fire `this.Input` as before

### Step 4.2: Route mouse input with hit-testing
**File:** `src/Consolonia.Core/Infrastructure/ConsoleWindow.cs`

In `ConsoleOnMouseEvent()` (line 360):
- Hit-test mouse position against child surfaces in reverse z-order
- If over a child: translate coordinates to child-local space, fire child's `Input`
- If not over any child: fire main window's `Input` as before
- Handle pointer capture (during drag, keep routing to capturing window)

### Step 4.3: Track active child window
`ChildWindowSurface` needs an `IsActive` flag, updated when:
- `ManagedWindow.Activated` fires → set active
- `ManagedWindow.Deactivated` fires → clear active

---

## Phase 5: Position Tracking

### Step 5.1: Sync ManagedWindow position to ChildWindowSurface
**File:** `src/Consolonia.ManagedWindows/ManagedWindowImpl.cs`

When ManagedWindow's position or size changes, update `ChildWindowSurface.Position` to reflect the screen-space position of the content area (not the chrome). This is:
- `ManagedWindow.Position` (Canvas.Left/Top) + content area offset (title bar height + border)

Subscribe to `PositionChanged` and `Resized` events on ManagedWindow.

### Step 5.2: Handle content area offset
The content area starts below the title bar and inside the border. Use `ManagedWindow._content` (PART_ContentPresenter) TranslatePoint or calculate from known chrome dimensions.

---

## Critical Files Summary

| File | Changes |
|------|---------|
| `src/Consolonia.Core/Infrastructure/IPixelBufferSurface.cs` | **New** — surface abstraction interface |
| `src/Consolonia.Core/Infrastructure/ChildWindowSurface.cs` | **New** — child window's pixel buffer |
| `src/Consolonia.Core/Infrastructure/ConsoleWindow.cs` | Implement IPixelBufferSurface, child registry, input routing |
| `src/Consolonia.Core/Drawing/RenderTarget.cs` | Accept IPixelBufferSurface, compositing, dual Blit() |
| `src/Consolonia.Core/Drawing/DrawingContextImpl.cs` | Accept IPixelBufferSurface |
| `src/Consolonia.Core/Drawing/DrawingContextImpl.Boxes.cs` | Update references |
| `src/Consolonia.Core/Drawing/DrawingContextImpl.Bitmaps.cs` | Update references |
| `src/Consolonia.ManagedWindows/ManagedWindowImpl.cs` | Delete content adoption, own surface, lifecycle |

## Verification

1. **Phase 1**: Run existing 42 tests — all 40 should pass (2 skipped). No behavior change.
2. **Phase 2-3**: Run gallery app, open dialog windows. They should render with chrome and content visible.
3. **Phase 4**: Test Escape closes dialogs, Enter activates default button, mouse clicks work in dialogs.
4. **Phase 5**: Drag/resize dialogs — content should follow correctly.
5. **End-to-end**: Run full test suite. Test nested dialogs (dialog opening dialog). Test window z-order.
