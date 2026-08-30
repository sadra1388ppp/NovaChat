using Microsoft.Win32;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class ProfileView : UserControl
{
    public event Action? BackToChatRequested;
    public event Action? ContactsRequested;
    public event Action? SessionExpired;

    private readonly ApiService _apiService = new();
    private ProfileModel? _profile;
    private bool _busy;

    public ProfileView()
    {
        InitializeComponent();
        Loaded += ProfileView_Loaded;
    }

    private async void ProfileView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ProfileView_Loaded;
        await LoadProfileAsync();
    }

    private async Task LoadProfileAsync()
    {
        if (!AuthState.IsAuthenticated) return;
        try
        {
            _profile = await _apiService.GetAsync<ProfileModel>("api/User/profile/me");
            if (_profile == null)
            {
                ShowFeedback("Could not load your profile.");
                return;
            }
            DisplayNameBox.Text = _profile.DisplayName;
            UserIdBox.Text = _profile.Id;
            EmailBox.Text = _profile.Email;
            BioBox.Text = _profile.Bio;
            UpdateProfileUi();
        }
        catch (Exception ex)
        {
            ShowFeedback($"Could not load profile: {ex.Message}");
        }
    }

    private void UpdateProfileUi()
    {
        if (_profile == null) return;
        AvatarInitialsText.Text = GetInitials(_profile.DisplayName, _profile.Id);
        AvatarInitialsText.Visibility = string.IsNullOrWhiteSpace(_profile.AvatarUrl) ? Visibility.Visible : Visibility.Collapsed;
        AvatarImage.Visibility = string.IsNullOrWhiteSpace(_profile.AvatarUrl) ? Visibility.Collapsed : Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(_profile.AvatarUrl))
        {
            try
            {
                AvatarImage.Source = new BitmapImage(new Uri(_apiService.BuildAbsoluteUrl(_profile.AvatarUrl) + $"?v={DateTime.UtcNow.Ticks}"));
            }
            catch { AvatarImage.Visibility = Visibility.Collapsed; AvatarInitialsText.Visibility = Visibility.Visible; }
        }

        StatusText.Text = _profile.IsOnline ? "● Online" : "● Offline";
        LastSeenText.Text = _profile.IsOnline ? "Active now" : (_profile.LastSeenAt.HasValue ? $"Last seen {FormatLastSeen(_profile.LastSeenAt.Value)}" : "Last seen not available");
        JoinedText.Text = $"Joined { _profile.CreatedAt.ToLocalTime():dd MMM yyyy}";
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _profile == null) return;
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text) || string.IsNullOrWhiteSpace(UserIdBox.Text) || string.IsNullOrWhiteSpace(EmailBox.Text))
        {
            ShowFeedback("Display name, User ID and Email are required.");
            return;
        }
        if (BioBox.Text.Length > 160) { ShowFeedback("Bio must be 160 characters or less."); return; }

        _busy = true;
        SaveButton.IsEnabled = false;
        try
        {
            var request = new UpdateProfileRequest
            {
                DisplayName = DisplayNameBox.Text.Trim(),
                Email = EmailBox.Text.Trim(),
                Bio = BioBox.Text.Trim(),
                NewUserId = UserIdBox.Text.Trim()
            };
            var result = await _apiService.PutAsync<UpdateProfileRequest, ProfileActionResponse>($"api/User/{Uri.EscapeDataString(_profile.Id)}", request);
            if (result?.User == null)
            {
                ShowFeedback("Profile could not be updated. Check your values and try again.");
                return;
            }

            _profile = result.User;
            UpdateProfileUi();
            ShowFeedback(result.Message);

            if (!string.Equals(AuthState.UserId, _profile.Id, StringComparison.Ordinal))
            {
                AuthState.Clear();
                SessionExpired?.Invoke();
            }
        }
        catch (Exception ex)
        {
            ShowFeedback($"Profile update failed: {ex.Message}");
        }
        finally
        {
            _busy = false;
            SaveButton.IsEnabled = true;
        }
    }

    private async void ChangePictureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !AuthState.IsAuthenticated) return;
        var dialog = new OpenFileDialog
        {
            Title = "Choose profile picture",
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.webp|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        _busy = true;
        SaveButton.IsEnabled = false;
        try
        {
            var result = await _apiService.UploadFileAsync<ProfileActionResponse>($"api/User/{Uri.EscapeDataString(AuthState.UserId)}/avatar", dialog.FileName);
            if (result?.User == null) ShowFeedback("Profile picture upload failed.");
            else { _profile = result.User; UpdateProfileUi(); ShowFeedback(result.Message); }
        }
        catch (Exception ex) { ShowFeedback($"Picture upload failed: {ex.Message}"); }
        finally { _busy = false; SaveButton.IsEnabled = true; }
    }

    private async void RemovePictureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _profile == null || string.IsNullOrWhiteSpace(_profile.AvatarUrl)) return;
        if (MessageBox.Show("Remove your profile picture?", "NovaChat", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _busy = true;
        try
        {
            if (await _apiService.DeleteAsync($"api/User/{Uri.EscapeDataString(_profile.Id)}/avatar"))
            {
                _profile.AvatarUrl = null;
                UpdateProfileUi();
                ShowFeedback("Profile picture removed.");
            }
            else ShowFeedback("Profile picture could not be removed.");
        }
        catch (Exception ex) { ShowFeedback($"Could not remove picture: {ex.Message}"); }
        finally { _busy = false; }
    }

    private static string GetInitials(string displayName, string id)
    {
        var value = string.IsNullOrWhiteSpace(displayName) ? id : displayName;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant() : value[..Math.Min(2, value.Length)].ToUpperInvariant();
    }

    private static string FormatLastSeen(DateTime value)
    {
        var local = value.ToLocalTime();
        return local.Date == DateTime.Now.Date ? $"today at {local:HH:mm}" : local.ToString("dd MMM yyyy HH:mm");
    }

    private void ShowFeedback(string message) => FeedbackText.Text = message;
    private void BackToChatButton_Click(object sender, RoutedEventArgs e) => BackToChatRequested?.Invoke();
    private void ContactsButton_Click(object sender, RoutedEventArgs e) => ContactsRequested?.Invoke();
}