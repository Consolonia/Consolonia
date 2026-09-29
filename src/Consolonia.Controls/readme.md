![logo](https://raw.githubusercontent.com/tomlm/ConsoloniaContent/main/Logo.png)

# Consolonia.Controls
This package provides Consolonia speicfic controls for building console apps using [Consolonia](https://github.com/jinek/consolonia) applications.
* LineBrush - A brush that draws a line.
* BrightenBrush -  A brush that brigthens the background color.
* ShadeBrush - A brush that shades the background color.
* MoveConsoleCaretToPositionBrush - A brush that moves the console caret to the position it is drawn into.
* ConsoleCaret - A control that represents the console caret.
* OnPlatform - Extends Console as a target platform for Avalonia.

## Installation
You can install the package via NuGet:
```bash
dotnet add package Consolonia.Controls
```
Or via the NuGet Package Manager in Visual Studio.
```powershell
Install-Package Consolonia.Controls
```
## Usage
To use the controls in your Consolonia application, you need to add a reference to the `Consolonia.Controls` namespace in your XAML file:
```xml
    xmlns:console="https://github.com/jinek/consolonia"
```
Then you can use the controls in your XAML code:
```xml
   <console:LineBrush Brush="Red" LineStyle="Edge"/>
```

## NativeAOT brush animations

`LineBrush` registers `LineBrushAnimator` through Avalonia 12.0.3's public
`Animation.RegisterCustomAnimator<IBrush, LineBrushAnimator>()` API when the
brush is first used. Previously, registration reflected into Avalonia's private
brush animator list, constructed an internal closed generic tuple and compiled
an expression tree. That path fails in NativeAOT. Inner gradient interpolation
also reflected into a private animator; it now uses public immutable brush
types instead.

Avalonia's public registration matches the *property type*, not the brush value
type, so this animator also receives ordinary `IBrush` animations. It retains
solid-color sRGB interpolation, linear/radial/conic gradient interpolation and
solid-to-gradient conversion for those properties; incompatible brush pairs
switch at halfway. This avoids registering or preserving Avalonia private
types for trimming.

To publish and run the focused Windows NativeAOT probe (requires the .NET 10
SDK and Windows C++ linker):

```powershell
dotnet publish src\Tests\Consolonia.NativeAot.Smoke\Consolonia.NativeAot.Smoke.csproj -c Release -r win-x64
.\src\Tests\Consolonia.NativeAot.Smoke\bin\Release\net10.0\win-x64\publish\Consolonia.NativeAot.Smoke.exe
```

The probe creates a `Border` with a `LineBrush`, checks brush interpolation, and
round-trips a `Consolonia.Core` pixel buffer through a source-generated JSON
context in the published native executable. It does not load a full TUI theme or
render a terminal frame.
This source fix requires a new `Consolonia.Controls` package release; existing
12.0.3.13 packages do not contain it. Until then, a project reference to this
source or a locally packed build is needed.
