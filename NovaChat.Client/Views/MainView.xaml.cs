using System;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views
{
    public partial class MainView : UserControl
    {
        public event Action? ProfileRequested;
        public event Action? SettingsRequested;

        public MainView()
        {
            InitializeComponent();
        }

        private void ProfileButton_Click(object sender, RoutedEventArgs e)
        {
            ProfileRequested?.Invoke();
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            SettingsRequested?.Invoke();
        }
    }
}