![logo](https://raw.githubusercontent.com/tomlm/ConsoloniaContent/main/Logo.png)

# Consolonia.Controls.DataGrid
DataGrid templates, styles, and helpers for Consolonia with multi‑theme support (Avalonia 11). Extracted from `Consolonia.Themes` into a standalone, reusable package.

## What’s inside
- Base templates for `DataGrid`, `DataGridRow`, `DataGridCell`, `DataGridColumnHeader`, headers presenter, scrollbars, grid lines, and separators.
- Theme resources tuned for text‑mode rendering (1×1 glyph cells, single‑char rules, minimal borders).
- Modern theme override that disables caret rendering inside rows to avoid visual noise during navigation.
- Attached property `DataGridExtensions.IsSelected` used by the row template to project selection state into the caret/control visuals.
- XAML namespace mapping for `https://github.com/consolonia` so all helpers are usable with the standard Consolonia XMLNS.

## Installation
Install from NuGet:
```bash
dotnet add package Consolonia.Controls.DataGrid
```
or via Package Manager:
```powershell
Install-Package Consolonia.Controls.DataGrid
```

Target framework: `net8.0`
Requires: `Avalonia` and `Avalonia.Controls.DataGrid` version `$(AvaloniaVersion)` (kept in repo props), `Consolonia.Core`, `Consolonia.Controls`.

## How to use
There are two common ways to consume the templates.

1) Recommended: via Consolonia themes (no extra setup)
- If your app already uses `Consolonia.Themes` (e.g., `ModernTheme`, `TurboVision*`), the DataGrid templates are merged automatically. No additional XAML is required.

2) Direct include (standalone)
- Add the resource dictionary directly in your application or theme resources:
```xaml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Styles.Resources>
    <ResourceInclude Source="avares://Consolonia.Controls.DataGrid/Themes/Templates/Controls/DataGrid.axaml" />
  </Styles.Resources>
</Styles>
```
- Optional Modern override (turns off the in‑row caret):
```xaml
<StyleInclude Source="avares://Consolonia.Controls.DataGrid/Themes/Modern/Controls/DataGridRow.axaml" />
```

XAML namespace (for helpers/attached props):
```xaml
xmlns:console="https://github.com/consolonia"
```

## Example
```xaml
<DataGrid AutoGenerateColumns="False"
          ItemsSource="{Binding People}"
          HeadersVisibility="Column"
          CanUserSortColumns="True">
  <DataGrid.Columns>
    <DataGridTextColumn Header="Name"  Binding="{Binding Name}" />
    <DataGridTextColumn Header="Age"   Binding="{Binding Age}" />
    <DataGridTextColumn Header="City"  Binding="{Binding City}" />
  </DataGrid.Columns>
</DataGrid>
```

## Theme notes
- Base templates ship in `Themes/Templates/Controls/DataGrid.axaml` and work across Consolonia themes.
- Modern theme customization lives in `Themes/Modern/Controls/DataGridRow.axaml` and can be included on top of base templates.
- If you use `Consolonia.Themes`, these are already wired:
  - `AllControls.axaml` merges the base DataGrid templates from this assembly.
  - `ModernBase.axaml` includes the Modern `DataGridRow` override from this assembly.

## API surface
- `Consolonia.Controls.DataGrid.Helpers.DataGridExtensions`:
  - `IsSelected` (attached, `bool`) — set by styles to signal row selection for caret/visual logic.

## Compatibility
- Avalonia: 11.3.x (pinned in repo to `$(AvaloniaVersion)`).
- Consolonia: same minor as this package (see `Directory.Build.props` `VersionPrefix`).
- Runtime: `net8.0`.

## Building from source
```bash
git clone https://github.com/consolonia/consolonia
cd Consolonia/src
dotnet build
```

## Contributing
Issues and PRs are welcome. Please see the root repository `contributing.md` for guidelines.

## License
MIT © Consolonia contributors
