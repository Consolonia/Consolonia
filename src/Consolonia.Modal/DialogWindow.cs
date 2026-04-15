using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.VisualTree;

// ReSharper disable MemberCanBeProtected.Global

namespace Consolonia.Modal
{
    [TemplatePart("PART_ContentPresenter", typeof(ContentPresenter))]
    public class DialogWindow : ContentControl
    {
        protected override Type StyleKeyOverride => typeof(DialogWindow);

        public static readonly DirectProperty<DialogWindow, Size> ContentSizeProperty =
            AvaloniaProperty.RegisterDirect<DialogWindow, Size>(nameof(ContentSize), window => window.ContentSize);

        public static readonly StyledProperty<string> TitleProperty = Window.TitleProperty.AddOwner<DialogWindow>();
        public static readonly StyledProperty<object> IconProperty = AvaloniaProperty.Register<DialogWindow, object>(nameof(Icon));

        public static readonly StyledProperty<bool> IsCloseButtonVisibleProperty =
            AvaloniaProperty.Register<DialogWindow, bool>(nameof(IsCloseButtonVisible), true);

        private Size _contentSize;
        private ContentPresenter _partContentPresenter;

        private TaskCompletionSource _taskCompletionSource;

        static DialogWindow()
        {
            TitleProperty.OverrideDefaultValue<DialogWindow>(string.Empty);
        }

        public DialogWindow()
        {
            KeyDown += InputElement_OnKeyDown;
        }

        public Size ContentSize
        {
            get => _contentSize;
            private set => SetAndRaise(ContentSizeProperty, ref _contentSize, value);
        }

        public string Title
        {
            get => GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public bool IsCloseButtonVisible
        {
            get => GetValue(IsCloseButtonVisibleProperty);
            set => SetValue(IsCloseButtonVisibleProperty, value);
        }

        public object Icon
        {
            get => GetValue(IconProperty);
            set => SetValue(IconProperty, value);
        }

        // ReSharper disable once AutoPropertyCanBeMadeGetOnly.Global
        // ReSharper disable once MemberCanBePrivate.Global
        public bool CancelOnEscape { get; set; } = true;

        // ReSharper disable once UnusedMember.Global Used by template
        public void CloseClick()
        {
            if (CancelOnEscape)
                CloseDialog();
        }

        protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
        {
            base.OnApplyTemplate(e);
            _partContentPresenter = e.NameScope.Find<ContentPresenter>("PART_ContentPresenter");
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Size arrangeOverride = base.ArrangeOverride(finalSize);
            Visual firstVisualChild = _partContentPresenter?.GetVisualChildren().FirstOrDefault();
            if (firstVisualChild != null)
                ContentSize = firstVisualChild.Bounds.Size;
            return arrangeOverride;
        }

        private void ShowDialogInternal(Visual parent)
        {
            DialogHost dialogHost = GetDialogHost(parent);
            dialogHost.OpenInternal(this);
        }

        // ReSharper disable once VirtualMemberNeverOverridden.Global overriden in other packages, why resharper suggests this?
        public virtual void CloseDialog()
        {
            DialogHost dialogHost = GetDialogHost(this);
            dialogHost.PopInternal(this);
            _taskCompletionSource.SetResult();
        }

        public Task ShowDialogAsync(Control parent)
        {
            if (_taskCompletionSource != null)
                throw new NotImplementedException();

            _taskCompletionSource = new TaskCompletionSource();
            ShowDialogInternal(parent);
            return _taskCompletionSource.Task;
        }

        private static DialogHost GetDialogHost(Visual parent)
        {
            var window = parent.FindAncestorOfType<Window>(true);
            DialogHost dialogHost = window!.GetValue(DialogHost.DialogHostProperty);
            return dialogHost;
        }

        private void InputElement_OnKeyDown(object sender, KeyEventArgs e)
        {
            if (!CancelOnEscape) return;
            if (e.Key is not (Key.Cancel or Key.Escape)) return;
            CloseDialog();
            e.Handled = true;
        }
    }
}