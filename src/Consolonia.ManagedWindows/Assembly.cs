using System;
using Avalonia.Metadata;

[assembly: CLSCompliant(false)] //todo: should we make it compliant?

[assembly: XmlnsDefinition("https://github.com/consolonia", "Consolonia.ManagedWindows")]

// backward compatibility TODO remove when possible
[assembly: XmlnsDefinition("https://github.com/jinek/consolonia", "Consolonia.ManagedWindows")]
