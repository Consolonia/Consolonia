using System;
using Consolonia.Core.Styles;

namespace Consolonia.Modal
{
    public class ModalTheme : ResourceIncludeBase
    {
        public ModalTheme(Uri baseUri) : base(baseUri)
        {
        }

        public ModalTheme(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        protected override Uri Uri =>
            new("avares://Consolonia.Modal/DialogWindow.axaml");
    }
}
