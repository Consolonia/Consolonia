using System;
using System.Runtime.CompilerServices;
using Avalonia.Metadata;

[assembly: InternalsVisibleTo("Consolonia.Core.Tests")]
[assembly: InternalsVisibleTo("Consolonia.Designer")]
[assembly: InternalsVisibleTo("Consolonia.ManagedWindows")]

[assembly: CLSCompliant(false)] //todo: should we make it compliant?
[assembly: XmlnsDefinition("https://github.com/consolonia", "Consolonia")]
[assembly: XmlnsDefinition("https://github.com/consolonia", "Consolonia.Controls")]

// backward compatibility TODO remove when possible
[assembly: XmlnsDefinition("https://github.com/jinek/consolonia", "Consolonia")]
[assembly: XmlnsDefinition("https://github.com/jinek/consolonia", "Consolonia.Controls")]