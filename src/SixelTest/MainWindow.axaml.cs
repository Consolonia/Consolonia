using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SixelTest
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private void OnNewWindowClick(object? sender, RoutedEventArgs e)
        {
            var child = new ChildWindow();
            child.Show();
        }

        private void OnExitClick(object? sender, RoutedEventArgs e)
        {
            if (Application.Current?.ApplicationLifetime is
                Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime lifetime)
                lifetime.Shutdown();
            else
                Environment.Exit(0);
        }
    }
}
