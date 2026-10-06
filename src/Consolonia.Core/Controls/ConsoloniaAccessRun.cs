using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using Avalonia.Reactive;

namespace Consolonia.Core.Controls
{
    [PseudoClasses(ShowAccessKeyPseudoClass)]
    public sealed class ConsoloniaAccessRun : Run
    {
        public const string ShowAccessKeyPseudoClass = ":showaccesskey";
        private IDisposable _disposable;

        public ConsoloniaAccessRun()
        {
        }

        public ConsoloniaAccessRun(string text) : base(text)
        {
        }

        private void OnShowAccessKeyChanged(bool newValue)
        {
            PseudoClassesExtensions.Set(Classes, ShowAccessKeyPseudoClass, newValue);
        }

        protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            _disposable = this.GetObservable(AccessText.ShowAccessKeyProperty)
                .Subscribe(new AnonymousObserver<bool>(OnShowAccessKeyChanged));
            OnShowAccessKeyChanged(GetValue(AccessText.ShowAccessKeyProperty));
            base.OnAttachedToLogicalTree(e);
        }

        protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
        {
            _disposable.Dispose();
            base.OnDetachedFromLogicalTree(e);
        }
    }
}