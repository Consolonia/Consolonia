# Consolonia.ManagedWindows

Managed windows and storage integration for Consolonia/Avalonia apps running in the console.

This module provides:

- File and folder pickers implemented as managed windows (open, save, pick folder).
- An `IStorageProvider` implementation that plugs into Avalonia (`ConsoloniaStorageProvider`).
- Simple wrappers for system files/folders (`SystemStorageFile`, `SystemStorageFolder`).
- Theme resources for managed windows (Modern and TurboVision) with auto‑inclusion via `AutoManagedWindowStyles`.

## Getting started

1) Enable the Consolonia storage provider in your app startup:

```csharp
using Avalonia;
using Consolonia.ManagedWindows.Storage;

public static class Program
{
    public static void Main(string[] args)
    {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
                  .UsePlatformDetect()
                  .UseConsoloniaStorage(); // <= enables Consolonia storage dialogs
}
```

2) Include Managed Windows styles (Modern/TurboVision). The easiest way is to auto‑include styles based on your Consolonia theme family:

```xml
<!-- App.axaml -->
<Application xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:themes="clr-namespace:Consolonia.Themes;assembly=Consolonia.ManagedWindows">
  <Application.Styles>
    <themes:AutoManagedWindowStyles/>
  </Application.Styles>
  <!-- ... -->
  
</Application>
```

Alternatively, include styles explicitly:

```xml
<!-- Modern base styles -->
<StyleInclude Source="avares://Consolonia.ManagedWindows/Themes/Base.axaml" />

<!-- TurboVision theme -->
<StyleInclude Source="avares://Consolonia.ManagedWindows/Themes/TurboVision/TurboVision.axaml" />
```

## Using the pickers (Avalonia `IStorageProvider`)

All standard Avalonia storage APIs are supported through `TopLevel.StorageProvider`.

Open files:

```csharp
var topLevel = TopLevel.GetTopLevel(this)!;
var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
{
    Title = "Open files",
    AllowMultiple = true,
    FileTypeFilter = new[]
    {
        new FilePickerFileType("Text") { Patterns = new[] { "*.txt", "*.md" } }
    }
});
```

Save a file:

```csharp
var topLevel = TopLevel.GetTopLevel(this)!;
var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
{
    Title = "Save as",
    SuggestedFileName = "document.txt"
});

if (file is not null)
{
    await using var stream = await file.OpenWriteAsync();
    // write your content
}
```

Pick a folder:

```csharp
var topLevel = TopLevel.GetTopLevel(this)!;
var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
{
    Title = "Choose a folder",
    AllowMultiple = false
});
```

You can also resolve paths directly through the provider:

```csharp
var file = await topLevel.StorageProvider.TryGetFileFromPathAsync(new Uri("file:///path/to/file.txt"));
var folder = await topLevel.StorageProvider.TryGetFolderFromPathAsync(new Uri("file:///path/to/folder"));
```

## What’s inside

- `Storage/StorageStartupExtensions.cs` – `AppBuilder.UseConsoloniaStorage()` extension.
- `Storage/ConsoloniaStorageProviderFactory.cs` – wires `IStorageProvider` for a `TopLevel`.
- `Storage/ConsoloniaStorageProvider.cs` – implements `IStorageProvider` using managed dialogs.
- `Controls/FileOpenPicker.*`, `FileSavePicker.*`, `FolderPicker.*` – managed window dialogs.
- `Controls/SystemStorageFile.cs`, `SystemStorageFolder.cs` – system I/O wrappers.
- `AutoManagedWindowStyles` and resources in `Themes/` – styles for managed windows.

## Samples

See `src/Consolonia.Gallery` for examples of using pickers in a running Consolonia app.

## Dependencies

- Avalonia 11+
- Iciclecreek.Avalonia.WindowManager (for managed windows)
- Consolonia.Core / Consolonia.Themes

## License

This project is part of the Consolonia repository and is licensed under the repository’s root license.