using System;
using Avalonia.Metadata;

[assembly: CLSCompliant(false)] // Same as other projects

// Map Consolonia XML namespace to this assembly's CLR namespaces
[assembly: XmlnsDefinition("https://github.com/consolonia", "Consolonia.Controls.DataGrid")]
[assembly: XmlnsDefinition("https://github.com/consolonia", "Consolonia.Controls.DataGrid.Helpers")]

// backward compatibility with older Consolonia URI
[assembly: XmlnsDefinition("https://github.com/jinek/consolonia", "Consolonia.Controls.DataGrid")]
[assembly: XmlnsDefinition("https://github.com/jinek/consolonia", "Consolonia.Controls.DataGrid.Helpers")]