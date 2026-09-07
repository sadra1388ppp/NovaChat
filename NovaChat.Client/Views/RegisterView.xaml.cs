using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views
{
    public partial class RegisterView : UserControl
    {
        private readonly ApiService _apiService;
        private int _registrationInProgress;
        private CancellationTokenSource? _availabilityCts;

        public event Action? BackToLoginRequested;

        public RegisterView()
        {
            InitializeComponent();
            _apiService = new ApiService();
        }

        private async void UsernameTextBox_TextChanged(object sender, TextChangedEventArgs e) => await CheckAvailabilityAsync();
        private async void EmailTextBox_TextChanged(object sender, TextChangedEventArgs e) => await CheckAvailabilityAsync();

        private async void PhoneNumberTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidatePhoneNumber();
            await CheckAvailabilityAsync();
        }

        private void PhoneNumberTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Text) || e.Text.Any(ch => ch < '0' || ch > '9'))
            {
                e.Handled = true;
                return;
            }

            string proposed = GetProposedText(PhoneNumberTextBox, e.Text);
            if (proposed.Length > 11 || (proposed.Length > 0 && proposed[0] != '0'))
                e.Handled = true;
        }

        private void PhoneNumberTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (!e.DataObject.GetDataPresent(DataFormats.Text))
            {
                e.CancelCommand();
                return;
            }

            string pasted = e.DataObject.GetData(DataFormats.Text) as string ?? string.Empty;
            string proposed = GetProposedText(PhoneNumberTextBox, pasted);
            if (proposed.Length > 11 || (proposed.Length > 0 && proposed[0] != '0') || proposed.Any(ch => ch < '0' || ch > '9'))
                e.CancelCommand();
        }

        private void ValidatePhoneNumber()
        {
            string phone = PhoneNumberTextBox.Text.Trim();
            bool valid = phone.Length == 11 && phone[0] == '0' && phone.All(char.IsDigit);
            PhoneValidationText.Visibility = string.IsNullOrWhiteSpace(phone) || valid ? Visibility.Collapsed : Visibility.Visible;
        }

        private static string GetProposedText(TextBox box, string insertedText)
        {
            int start = box.SelectionStart;
            int length = box.SelectionLength;
            return box.Text.Remove(start, length).Insert(start, insertedText);
        }

        private async System.Threading.Tasks.Task CheckAvailabilityAsync()
        {
            _availabilityCts?.Cancel();
            _availabilityCts?.Dispose();
            _availabilityCts = new CancellationTokenSource();
            var token = _availabilityCts.Token;

            string username = UsernameTextBox.Text.Trim();
            string email = EmailTextBox.Text.Trim();
            string phone = PhoneNumberTextBox.Text.Trim();

            UsernameDuplicateText.Visibility = Visibility.Collapsed;
            EmailDuplicateText.Visibility = Visibility.Collapsed;
            PhoneDuplicateText.Visibility = Visibility.Collapsed;

            if (string.IsNullOrWhiteSpace(username) && string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
            {
                RegisterButton.IsEnabled = true;
                return;
            }

            try
            {
                await System.Threading.Tasks.Task.Delay(350, token);
                if (token.IsCancellationRequested) return;

                string query = $"api/User/registration-availability?username={Uri.EscapeDataString(username)}&email={Uri.EscapeDataString(email)}&phoneNumber={Uri.EscapeDataString(phone)}";
                var result = await _apiService.GetAsync<RegistrationAvailabilityResponse>(query);
                if (token.IsCancellationRequested || result == null) return;

                UsernameDuplicateText.Visibility = result.UsernameTaken ? Visibility.Visible : Visibility.Collapsed;
                EmailDuplicateText.Visibility = result.EmailTaken ? Visibility.Visible : Visibility.Collapsed;
                PhoneDuplicateText.Visibility = result.PhoneTaken ? Visibility.Visible : Visibility.Collapsed;
                RegisterButton.IsEnabled = !result.UsernameTaken && !result.EmailTaken && !result.PhoneTaken;
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                // The final registration request still performs authoritative validation.
            }
        }

        private async void RegisterButton_Click(object sender, RoutedEventArgs e)
        {
            if (Interlocked.Exchange(ref _registrationInProgress, 1) == 1) return;

            string username = UsernameTextBox.Text.Trim();
            string displayName = DisplayNameTextBox.Text.Trim();
            string email = EmailTextBox.Text.Trim();
            string phoneNumber = PhoneNumberTextBox.Text.Trim();
            string password = PasswordBox.Password;
            HideRegistrationError();

            try
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(phoneNumber) || string.IsNullOrWhiteSpace(password))
                {
                    MessageBox.Show("Please fill in all fields.", "Register", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (username.Length < 3 || username.Length > 32)
                {
                    ShowRegistrationError("Username must be 3 to 32 characters.");
                    return;
                }

                if (phoneNumber.Length != 11 || phoneNumber[0] != '0' || !phoneNumber.All(char.IsDigit))
                {
                    ShowRegistrationError("Phone number must contain exactly 11 digits and start with 0.");
                    return;
                }

                RegisterButton.IsEnabled = false;
                var request = new RegisterRequest { Username = username, DisplayName = displayName, Email = email, PhoneNumber = phoneNumber, Password = password };
                var result = await _apiService.PostAsync<RegisterRequest, RegisterResponse>("api/User/register", request);

                if (result == null)
                {
                    ShowRegistrationError("Registration failed. Please check the entered information and try again.");
                    return;
                }

                bool success = result.Message.Contains("success", StringComparison.OrdinalIgnoreCase);
                if (!success) { ShowRegistrationError(result.Message); return; }

                MessageBox.Show(result.Message, "Registration Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                BackToLoginRequested?.Invoke();
            }
            catch (HttpRequestException ex)
            {
                if (ex.Message.Contains("(409 Conflict)", StringComparison.OrdinalIgnoreCase))
                {
                    ShowRegistrationError(ExtractApiMessage(ex.Message));
                    await CheckAvailabilityAsync();
                    return;
                }
                MessageBox.Show(ex.Message, "Registration Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not connect to the server.\n\n{ex.Message}", "Connection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                Interlocked.Exchange(ref _registrationInProgress, 0);
                if (UsernameDuplicateText.Visibility != Visibility.Visible && EmailDuplicateText.Visibility != Visibility.Visible && PhoneDuplicateText.Visibility != Visibility.Visible)
                    RegisterButton.IsEnabled = true;
            }
        }

        private void ShowRegistrationError(string message)
        {
            RegistrationErrorTextBlock.Text = message;
            RegistrationErrorTextBlock.Visibility = Visibility.Visible;
        }

        private void HideRegistrationError()
        {
            RegistrationErrorTextBlock.Text = string.Empty;
            RegistrationErrorTextBlock.Visibility = Visibility.Collapsed;
        }

        private static string ExtractApiMessage(string exceptionMessage)
        {
            const string marker = "): ";
            var index = exceptionMessage.IndexOf(marker, StringComparison.Ordinal);
            return index >= 0 && index + marker.Length < exceptionMessage.Length ? exceptionMessage[(index + marker.Length)..].Trim() : exceptionMessage;
        }

        private void BackToLoginButton_Click(object sender, RoutedEventArgs e)
        {
            if (Volatile.Read(ref _registrationInProgress) == 1) return;
            BackToLoginRequested?.Invoke();
        }

        private sealed class RegistrationAvailabilityResponse
        {
            public bool UsernameTaken { get; set; }
            public bool EmailTaken { get; set; }
            public bool PhoneTaken { get; set; }
        }
    }
}
