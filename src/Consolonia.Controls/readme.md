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
