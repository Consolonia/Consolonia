using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Consolonia.Controls;

namespace Consolonia.Themes.Templates.Controls.Helpers
{
    public static class MenuExtensions
    {
        public static readonly AttachedProperty<bool> IsMenuProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(ControlUtils.GetStyledPropertyName(),
                typeof(MenuExtensions),
                inherits: true);

        public static readonly AttachedProperty<bool> FocusOnLoadProperty =
            AvaloniaProperty.RegisterAttached<Control, bool>(ControlUtils.GetStyledPropertyName(),
                typeof(MenuExtensions));

        private static readonly AttachedProperty<IDisposable[]> DisposablesProperty =
            AvaloniaProperty.RegisterAttached<ItemsPresenter, IDisposable[]>(ControlUtils.GetStyledPropertyName(),
                typeof(MenuExtensions));

        static MenuExtensions()
        {
            FocusOnLoadProperty.Changed.SubscribeAction(args =>
            {
                var visual = (Visual)args.Sender;
                if (args.NewValue.Value)
                {
                    visual.AttachedToVisualTree += OnAttachedToVisualTree;
                    IDisposable disposable = visual
                        .GetPropertyChangedObservable(InputElement.IsKeyboardFocusWithinProperty)
                        .Subscribe(new AnonymousObserver<AvaloniaPropertyChangedEventArgs>(eventArgs =>
                        {
                            if (!(bool)eventArgs.NewValue!)
                                Dispatcher.UIThread.Post(() =>
                                {
                                    var focusedControl =
                                        TopLevel.GetTopLevel(visual)?.FocusManager?.GetFocusedElement() as Control;

                                    if (focusedControl != null)
                                    {
                                        IEnumerable<ILogical> focusedTree = focusedControl.GetLogicalAncestors();
                                        IEnumerable<MenuItem> menuItems =
                                            visual.GetLogicalAncestors().OfType<MenuItem>();

                                        foreach (MenuItem menuItem in menuItems
                                                     .Where(item => !focusedTree.Contains(item))
                                                     .ToArray())
                                            menuItem.Close();
                                    }
                                });
                        }));
                    visual.SetValue(DisposablesProperty, new[] { disposable });
                }
                else
                {
                    visual.AttachedToVisualTree -= OnAttachedToVisualTree;
                    foreach (IDisposable disposable in visual.GetValue(DisposablesProperty)) disposable.Dispose();
                }
            });
        }

        private static void OnAttachedToVisualTree(object sender, VisualTreeAttachmentEventArgs e)
        {
            Dispatcher.UIThread.Post(() =>
            {
                var control = (Control)sender;
                control.FindDescendantOfType<MenuItem>()?.Focus();
            });
        }
    }
}