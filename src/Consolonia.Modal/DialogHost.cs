using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.VisualTree;
using Consolonia.Controls;

namespace Consolonia.Modal
{
    public class DialogHost
    {
        public static readonly AttachedProperty<bool> IsDialogHostProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>("IsDialogHost", typeof(DialogHost));
        
        public static bool GetIsDialogHost(Control control) => control.GetValue(IsDialogHostProperty);
        public static void SetIsDialogHost(Control control, bool value) => control.SetValue(IsDialogHostProperty, value);

        internal static readonly AttachedProperty<DialogHost> DialogHostProperty =
            AvaloniaProperty.RegisterAttached<Control, DialogHost>("DialogHost", typeof(DialogHost));

        private readonly Stack<OverlayPopupHost> _dialogs = new();

        private readonly Window _window;

        static DialogHost()
        {
            IsDialogHostProperty.Changed.SubscribeAction(args =>
            {
                args.Sender.SetValue(DialogHostProperty,
                    args.NewValue.Value ? new DialogHost((Window)args.Sender) : null);
            });
        }

        private DialogHost(Window window)
        {
            _window = window;
        }

        public void OpenInternal(DialogWindow dialogWindow)
        {
            IInputElement focusedElement = _window.FocusManager! /*todo: low: Why can be null?*/.GetFocusedElement();
            var overlayLayer = OverlayLayer.GetOverlayLayer(_window);
            var popupHost = new OverlayPopupHost(overlayLayer!);

            popupHost.ConfigurePosition(_window, PlacementMode.AnchorAndGravity,
                new Point(), PopupAnchor.TopLeft, PopupGravity.BottomRight);

            var dialogWrap = new DialogWrap();
            dialogWrap.SetContent(dialogWindow);
            popupHost.SetChild(dialogWrap);
            GetFirstContentPresenter().IsEnabled = false;

            if (_dialogs.TryPeek(out OverlayPopupHost previousDialog)) previousDialog.IsEnabled = false;

            dialogWrap.HadFocusOn = focusedElement;

            _dialogs.Push(popupHost);
            popupHost.Show();

            dialogWindow.AttachedToVisualTree += DialogAttachedToVisualTree;
            return;

            static void DialogAttachedToVisualTree(object sender, EventArgs e)
            {
                var dialogWindow = (DialogWindow)sender!;
                dialogWindow.AttachedToVisualTree -= DialogAttachedToVisualTree;
                dialogWindow.Focus();
            }
        }

        private ContentPresenter GetFirstContentPresenter()
        {
            ContentPresenter firstContentPresenter = _window.GetTemplateChildren()
                .Select(control => control.FindDescendantOfType<ContentPresenter>())
                .First(d => d!.Name == "PART_ContentPresenter");
            return firstContentPresenter;
        }

        public void PopInternal(DialogWindow dialogWindow)
        {
            OverlayPopupHost overlayPopupHost = _dialogs.Pop();
            var dialogWrap = (DialogWrap)overlayPopupHost.Content!;
            if (!Equals(dialogWrap!.FoundContentPresenter.Content, dialogWindow!))
                throw new InvalidOperationException("Dialog is not topmost. Close private dialogs first");
            overlayPopupHost.Hide();

            if (_dialogs.TryPeek(out OverlayPopupHost previousDialog))
            {
                previousDialog.IsEnabled = true;
                previousDialog.Focus();
            }

            if (_dialogs.Count == 0)
            {
                ContentPresenter firstContentPresenter = GetFirstContentPresenter();
                firstContentPresenter.IsEnabled = true;
                firstContentPresenter.Focus();
            }

            dialogWrap.HadFocusOn.Focus();
        }
    }
}