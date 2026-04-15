# Consolonia.Modal

`Consolonia.Modal` provides a mechanism for displaying modal dialogs in Consolonia applications.

## Features

- **Modal Overlay**: Shades the background to prevent interaction with the main window.
- **Integration**: Use `ModalTheme` to enable modal support for all windows.
- **Customization**: Control the title, icon, close button visibility, and keyboard behavior (Escape key).
- **Layout**: Dialog size and position are determined by the `DialogWindow` properties and its first child.

## Installation

Add the `Consolonia.Modal` package to your project and include the `ModalTheme` in your `App.axaml`:

```xml
<Application ...
             xmlns:console="https://github.com/consolonia"
             xmlns:modal="clr-namespace:Consolonia.Modal;assembly=Consolonia.Modal">
    <Application.Styles>
        <console:ModernTheme /> <!-- or TurboVisionTheme -->
        <modal:ModalTheme />
    </Application.Styles>
</Application>
```

The `ModalTheme` attaches a `DialogHost` to every `Window`, which is required for displaying modal dialogs.

## Usage

### 1. Create a Dialog Window

Create a new XAML file for your dialog, inheriting from `modal:DialogWindow`. Set alignment on the `DialogWindow` itself to control its position.

```xml
<modal:DialogWindow xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                    xmlns:modal="clr-namespace:Consolonia.Modal;assembly=Consolonia.Modal"
                    x:Class="MyApp.MyDialog"
                    Title="Confirmation"
                    Icon="❓"
                    HorizontalAlignment="Center"
                    VerticalAlignment="Center">
    <!-- The first child determines the size of the dialog box -->
    <StackPanel Margin="2" 
                Spacing="1">
        <TextBlock Text="Are you sure you want to proceed?" />
        <StackPanel Orientation="Horizontal" Spacing="2" HorizontalAlignment="Center">
            <Button Content="OK" Click="OnOkClick" />
            <Button Content="Cancel" Click="OnCancelClick" />
        </StackPanel>
    </StackPanel>
</modal:DialogWindow>
```

### 2. Show the Dialog

Call `ShowDialogAsync` from your code-behind:

```csharp
var dialog = new MyDialog();
await dialog.ShowDialogAsync(this); // 'this' is the parent window or control
```

## Layout and Alignment

A `DialogWindow` spans the entire parent window by default to provide the modal overlay. The actual "dialog box" (the frame with the title and borders) follows the size of the first child and is centered within the `DialogWindow`.

### The Role of the First Child

The size of the dialog frame **depends on the first child** of the `DialogWindow`.

- **Sizing**: The dialog frame automatically calculates its size based on the dimensions of the first child.
- **Alignment**: The first child **must have its `HorizontalAlignment` and `VerticalAlignment` set to `Stretch`** (the default). Setting them to `Center` or other values will cause layout issues.
- **Margins**: Avoid setting a `Margin` on the first child, as it will behave like padding inside the dialog frame.

### Positioning the Dialog

To control the position, size, or margins of the dialog box relative to the screen, set these properties on the `DialogWindow` itself:

- **Centering**: Set `HorizontalAlignment="Center"` and `VerticalAlignment="Center"` on the `DialogWindow`.
- **Margins**: Set `Margin` on the `DialogWindow` to add space between the overlay edges and the dialog.
- **Docking**: Use `Left`, `Right`, `Top`, or `Bottom` alignments on the `DialogWindow`.

### Properties

| Property | Type | Description |
| :--- | :--- | :--- |
| `Title` | `string` | The text displayed in the dialog header. |
| `Icon` | `object` | An icon displayed next to the title. |
| `IsCloseButtonVisible`| `bool` | Whether to show the [x] close button (Default: `true`). |
| `CancelOnEscape` | `bool` | Whether the dialog can be closed by pressing the `Escape` key (Default: `true`). |
| `Margin` | `Thickness`| Should be used on the `DialogWindow` itself to limit the dialog area. |

## Customizing Themes

`Consolonia.Modal` supports both `Modern` and `TurboVision` themes. The `ModalTheme` automatically picks the appropriate styles based on the active Consolonia theme.

If you need to customize the look of all dialogs, you can override the `ControlTheme` for `dialog:DialogWindow` or `dialog:DialogWindowHeader`.
