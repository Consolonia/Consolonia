using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;

namespace Consolonia.Gallery.View
{
    public partial class XamlDialogWindow : Window
    {
        public XamlDialogWindow()
        {
            InitializeComponent();

            AttachedToVisualTree += DialogWindowAttachedToVisualTree;
        }

        private void DialogWindowAttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            AttachedToVisualTree -= DialogWindowAttachedToVisualTree;

            ILogical child = LogicalChildren.FirstOrDefault();
            if (child is InputElement input)
                input.AttachedToVisualTree += OnChildAttachedToVisualTree;
        }

        private void OnChildAttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            if (sender is InputElement input)
            {
                input.AttachedToVisualTree -= OnChildAttachedToVisualTree;
                input.Focus();
            }
        }
    }
}