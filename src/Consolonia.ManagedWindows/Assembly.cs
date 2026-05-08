using System;
using Avalonia.Metadata;

[assembly: CLSCompliant(false)] //todo: should we make it compliant?

[assembly: XmlnsDefinition("https://github.com/consolonia", "Consolonia.ManagedWindows")]
[assembly: XmlnsDefinition("https://github.com/consolonia", "Consolonia.ManagedWindows.Themes")]
[assembly: XmlnsDefinition("https://github.com/consolonia", "Consolonia.ManagedWindows.Storage")]
[assembly: XmlnsDefinition("https://github.com/consolonia", "Consolonia.ManagedWindows.Controls")]

// backward compatibility TODO remove when possible
[assembly: XmlnsDefinition("https://github.com/jinek/consolonia", "Consolonia.ManagedWindows")]
[assembly: XmlnsDefinition("https://github.com/jinek/consolonia", "Consolonia.ManagedWindows.Themes")]
[assembly: XmlnsDefinition("https://github.com/jinek/consolonia", "Consolonia.ManagedWindows.Storage")]
[assembly: XmlnsDefinition("https://github.com/jinek/consolonia", "Consolonia.ManagedWindows.Controls")]
