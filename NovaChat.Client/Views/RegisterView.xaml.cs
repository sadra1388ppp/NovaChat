using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views
{
    public partial class RegisterView : UserControl
    {
        private readonly ApiService _apiService;
        private int _registrationInProgress;

        public event Action? BackToLoginRequested;

        public RegisterView()
        {
            InitializeComponent();
            _apiService = new ApiService();
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (Interlocked.Exchange(ref _registrationInProgress, 1) == 1)
                return;

            string userId = UserIdTextBox.Text.Trim();
            string displayName = DisplayNameTextBox.Text.Trim();
            string email = EmailTextBox.Text.Trim();
            string phoneNumber = PhoneNumberTextBox.Text.Trim();
            string password = PasswordBox.Password;

            try
            {
                if (string.IsNullOrWhiteSpace(userId) ||
                    string.IsNullOrWhiteSpace(displayName) ||
                    string.IsNullOrWhiteSpace(email) ||
                    string.IsNullOrWhiteSpace(phoneNumber) ||
                    string.IsNullOrWhiteSpace(password))
                {
                    MessageBox.Show(
                        "Please fill in all fields.",
                        "Register",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                RegisterButton.IsEnabled = false;

                var request = new RegisterRequest
                {
                    Id = userId,
                    DisplayName = displayName,
                    Email = email,
                    PhoneNumber = phoneNumber,
                    Password = password
                };

                var result = await _apiService.PostAsync<RegisterRequest, RegisterResponse>(
                    "api/User/register",
                    request);

                if (result == null)
                {
                    MessageBox.Show(
                        "Registration failed. Please check the entered information and try again.",
                        "Register Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show(
                    result.Message,
                    result.Message.Contains("success", StringComparison.OrdinalIgnoreCase)
                        ? "Registration Successful"
                        : "Register",
                    MessageBoxButton.OK,
                    result.Message.Contains("success", StringComparison.OrdinalIgnoreCase)
                        ? MessageBoxImage.Information
                        : MessageBoxImage.Warning);

                if (result.Message.Contains("success", StringComparison.OrdinalIgnoreCase))
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
                Interlocked.Exchange(ref _registrationInProgress, 0);
            }
        }

        private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (Volatile.Read(ref _registrationInProgress) == 1)
                return;

            BackToLoginRequested?.Invoke();
        }
    }
}