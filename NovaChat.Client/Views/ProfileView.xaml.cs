using System;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views
{
    public partial class ProfileView : UserControl
    {
        public event Action? BackToChatRequested;
        public event Action? ContactsRequested;

        public ProfileView()
        {
            InitializeComponent();
        }

        private void BackToChatButton_Click(object sender, RoutedEventArgs e)
        {
            BackToChatRequested?.Invoke();
        }

        private void ContactsButton_Click(object sender, RoutedEventArgs e)
        {
            ContactsRequested?.Invoke();
        }
    }
}