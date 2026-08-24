using System;
using System.Windows;
using System.Windows.Controls;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views
{
    public partial class RegisterView : UserControl
    {
        private readonly ApiService _apiService;

        public event Action? BackToLoginRequested;

        public RegisterView()
        {
            InitializeComponent();

            _apiService = new ApiService();
        }

        private async void RegisterButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string userId = UserIdTextBox.Text.Trim();
            string displayName = DisplayNameTextBox.Text.Trim();
            string email = EmailTextBox.Text.Trim();
            string password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(userId) ||
                string.IsNullOrWhiteSpace(displayName) ||
                string.IsNullOrWhiteSpace(email) ||
                string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show(
                    "Please fill in all fields.",
                    "Register",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                RegisterButton.IsEnabled = false;

                var request = new RegisterRequest
                {
                    Id = userId,
                    DisplayName = displayName,
                    Email = email,
                    Password = password
                };

                var result =
                    await _apiService.PostAsync<RegisterRequest, RegisterResponse>(
                        "api/User/register",
                        request);

                if (result == null)
                {
                    MessageBox.Show(
                        "Registration failed.",
                        "Register Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    return;
                }

                MessageBox.Show(
                    result.Message,
                    "Registration Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                BackToLoginRequested?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not connect to the server.\n\n{ex.Message}",
                    "Connection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                RegisterButton.IsEnabled = true;
            }
        }

        private void BackToLoginButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            BackToLoginRequested?.Invoke();
        }
    }
}