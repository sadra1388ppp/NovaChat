using System;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views
{
    public partial class RegisterView : UserControl
    {
        public event Action? BackToLoginRequested;

        public RegisterView()
        {
            InitializeComponent();
        }

        private void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            string userId = UserIdTextBox.Text;
            string displayName = DisplayNameTextBox.Text;
            string email = EmailTextBox.Text;
            string password = PasswordBox.Password;

            MessageBox.Show(
                $"User ID: {userId}\n" +
                $"Display Name: {displayName}\n" +
                $"Email: {email}\n" +
                $"Password: {password}",
                "Register Test",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
        {
            BackToLoginRequested?.Invoke();
        }
    }
}