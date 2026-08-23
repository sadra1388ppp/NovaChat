using System;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views
{
    public partial class LoginView : UserControl
    {
        public event Action? CreateAccountRequested;

        public LoginView()
        {
            InitializeComponent();
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string userId = UserIdTextBox.Text;
            string password = PasswordBox.Password;

            MessageBox.Show(
                $"User ID: {userId}\nPassword: {password}",
                "Login Test",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void CreateAccountButton_Click(object sender, RoutedEventArgs e)
        {
            CreateAccountRequested?.Invoke();
        }
    }
}