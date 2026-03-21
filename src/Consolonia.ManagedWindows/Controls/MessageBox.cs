using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Iciclecreek.Avalonia.WindowManager;

// ReSharper disable CheckNamespace

namespace Consolonia.Controls
{
    public enum MessageBoxStyle
    {
        /// <summary>
        ///     Specifies that the message box contains an OK button.
        /// </summary>
        Ok,

        /// <summary>
        ///     Specifies that the message box contains OK and Cancel buttons.
        /// </summary>
        OkCancel,

        /// <summary>
        ///     Specifies that the message box contains Yes and No buttons.
        /// </summary>
        YesNo,

        /// <summary>
        ///     Specifies that the message box contains Yes, No, and Cancel buttons.
        /// </summary>
        YesNoCancel
    }

    public enum MessageBoxResult
    {
        Cancel,
        Ok,
        Yes,
        No
    }

    public class MessageBox : ManagedWindow
    {
        /// <summary>
        ///     Defines the <see cref="SizeToContent" /> property.
        /// </summary>
        public static readonly StyledProperty<object> YesProperty =
            AvaloniaProperty.Register<MessageBox, object>(nameof(Yes));

        public static readonly StyledProperty<object> NoProperty =
            AvaloniaProperty.Register<MessageBox, object>(nameof(No));

        public static readonly StyledProperty<object> OkProperty =
            AvaloniaProperty.Register<MessageBox, object>(nameof(Ok));

        public static readonly StyledProperty<object> CancelProperty =
            AvaloniaProperty.Register<MessageBox, object>(nameof(Cancel));

        public static readonly StyledProperty<object> MessageProperty =
            AvaloniaProperty.Register<MessageBox, object>(nameof(Message));

        public static readonly StyledProperty<MessageBoxStyle> MessageBoxStyleProperty =
            AvaloniaProperty.Register<MessageBox, MessageBoxStyle>(nameof(MessageBoxStyle));

        private Button _cancelButton;
        private Button _noButton;
        private Button _okButton;
        private Button _yesButton;

        public MessageBox()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SizeToContent = SizeToContent.WidthAndHeight;
            Padding = new Thickness(1.0);
        }


        public object Ok
        {
            get => GetValue(OkProperty);
            set => SetValue(OkProperty, value);
        }

        public object Yes
        {
            get => GetValue(YesProperty);
            set => SetValue(YesProperty, value);
        }

        public object No
        {
            get => GetValue(NoProperty);
            set => SetValue(NoProperty, value);
        }

        public object Cancel
        {
            get => GetValue(CancelProperty);
            set => SetValue(CancelProperty, value);
        }

        public object Message
        {
            get => GetValue(MessageProperty);
            set => SetValue(MessageProperty, value);
        }

        public MessageBoxStyle MessageBoxStyle
        {
            get => GetValue(MessageBoxStyleProperty);
            set => SetValue(MessageBoxStyleProperty, value);
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();

            _yesButton = new Button
            {
                Content = Yes ?? "_Yes",
                IsTabStop = true
            };
            _yesButton.Click += OnYes;

            _noButton = new Button
            {
                Content = No ?? "_No",
                IsTabStop = true
            };
            _noButton.Click += OnNo;

            _cancelButton = new Button
            {
                Content = Cancel ?? "_Cancel",
                IsCancel = true,
                IsTabStop = true
            };
            _cancelButton.Click += OnCancel;

            _okButton = new Button
            {
                Content = Ok ?? "O_k",
                IsDefault = true,
                IsTabStop = true
            };
            _okButton.Click += OnOk;

            var buttonsStackPanel = new StackPanel
            {
                Spacing = 1,
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            switch (MessageBoxStyle)
            {
                case MessageBoxStyle.Ok:
                    buttonsStackPanel.Children.Add(_okButton);
                    break;
                case MessageBoxStyle.OkCancel:
                    buttonsStackPanel.Children.Add(_okButton);
                    buttonsStackPanel.Children.Add(_cancelButton);
                    break;
                case MessageBoxStyle.YesNo:
                    buttonsStackPanel.Children.Add(_yesButton);
                    buttonsStackPanel.Children.Add(_noButton);
                    break;
                case MessageBoxStyle.YesNoCancel:
                    buttonsStackPanel.Children.Add(_yesButton);
                    buttonsStackPanel.Children.Add(_noButton);
                    buttonsStackPanel.Children.Add(_cancelButton);
                    break;
            }

            buttonsStackPanel.Children[0].Focus();

            Control message = Message is string str
                ? new TextBlock
                {
                    Text = str,
                    TextWrapping = TextWrapping.Wrap
                }
                : Message as Control;

            Content = new StackPanel
            {
                Spacing = 1,
                Children =
                {
                    message,
                    buttonsStackPanel
                }
            };
        }

        /// <summary>
        ///     Show this messagebox as a global modal dialog.
        /// </summary>
        /// <param name="title"></param>
        /// <param name="message"></param>
        /// <param name="style"></param>
        /// <returns></returns>
        public static Task<MessageBoxResult> ShowDialog(string title, string message,
            MessageBoxStyle style = MessageBoxStyle.Ok)
        {
            return ShowDialog(null, title, message, style);
        }

        /// <summary>
        ///     Show this messagebox as a global modal dialog.
        /// </summary>
        /// <returns></returns>
        public Task<MessageBoxResult> ShowDialog()
        {
            return ShowDialog(null);
        }


        /// <summary>
        ///     Show this messagebox scoped to the specified visual.
        /// </summary>
        /// <param name="visual"></param>
        /// <param name="title"></param>
        /// <param name="message"></param>
        /// <param name="style"></param>
        /// <returns></returns>
        public static Task<MessageBoxResult> ShowDialog(Visual visual, string title, string message,
            MessageBoxStyle style = MessageBoxStyle.Ok)
        {
            var mb = new MessageBox
            {
                MessageBoxStyle = style,
                Title = title,
                Message = message
            };
            return mb.ShowDialog<MessageBoxResult>(visual);
        }

        /// <summary>
        ///     Show this messagebox scoped to the specified visual.
        /// </summary>
        /// <param name="visual"></param>
        /// <returns></returns>
        public new async Task<MessageBoxResult> ShowDialog(Visual visual)
        {
            return await ShowDialog<MessageBoxResult>(visual);
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            Close(MessageBoxResult.Ok);
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Close(MessageBoxResult.Cancel);
        }

        private void OnYes(object sender, RoutedEventArgs e)
        {
            Close(MessageBoxResult.Yes);
        }

        private void OnNo(object sender, RoutedEventArgs e)
        {
            Close(MessageBoxResult.No);
        }
    }

    //public static class MessageBoxExtensions
    //{
    //    public static async Task<MessageBoxResult> ShowMessageBox(this Visual visual, string title, string message, MessageBoxStyle style = MessageBoxStyle.Ok)
    //    {
    //        var mb = new MessageBox
    //        {
    //            MessageBoxStyle = style,
    //            Title = title,
    //            Content = message
    //        };
    //        return await visual.ShowDialog<MessageBoxResult>(mb);
    //    }
    //}
}