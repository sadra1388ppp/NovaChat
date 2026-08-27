using System;
using System.Windows;
using System.Windows.Controls;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views
{
    public partial class LoginView : UserControl
    {
        private readonly ApiService _apiService;

        public event Action? CreateAccountRequested;
        public event Action? LoginSuccessful;
        public event Action? OwnerLoginSuccessful;

        public LoginView()
        {
            InitializeComponent();
            _apiService = new ApiService();
        }

        private async void LoginButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            string userId =
                UserIdTextBox.Text.Trim();

            string password =
                PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(userId) ||
                string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show(
                    "Please enter your User ID and Password.",
                    "Login",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            try
            {
                LoginButton.IsEnabled = false;

                var request = new LoginRequest
                {
                    Id = userId,
                    Password = password
                };

                var result =
                    await _apiService.PostAsync<
                        LoginRequest,
                        LoginResponse>(
                            "api/User/login",
                            request);

                if (result == null ||
                    string.IsNullOrWhiteSpace(
                        result.Token))
                {
                    MessageBox.Show(
                        "Invalid User ID or Password.",
                        "Login Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    return;
                }

                AuthState.Set(
                    result.Token,
                    result.User.Id,
                    result.User.DisplayName,
                    result.User.Email);

                const string ownerId = "BlackRoom";

                if (string.Equals(
                        result.User.Id,
                        ownerId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    OwnerLoginSuccessful?.Invoke();
                }
                else
                {
                    LoginSuccessful?.Invoke();
                }
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
                LoginButton.IsEnabled = true;
            }
        }

        private void CreateAccountButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            CreateAccountRequested?.Invoke();
        }
    }
}