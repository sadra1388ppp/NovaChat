using Microsoft.Win32;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class ProfileView : UserControl
{
    public event Action? BackToChatRequested;
    public event Action? ContactsRequested;
    public event Action? SessionExpired;

    private static readonly HttpClient AvatarHttpClient = new();
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
            await UpdateProfileUiAsync();
        }
        catch (Exception ex)
        {
            ShowFeedback($"Could not load profile: {ex.Message}");
        }
    }

    private async Task UpdateProfileUiAsync()
    {
        if (_profile == null) return;

        var initials = GetInitials(_profile.DisplayName, _profile.Id);
        AvatarInitialsText.Text = initials;
        ProfileDisplayNameText.Text = _profile.DisplayName;
        ProfileUserIdText.Text = $"@{_profile.Id}";
        ProfileBioText.Text = string.IsNullOrWhiteSpace(_profile.Bio)
            ? "Add a short bio to tell people about yourself."
            : _profile.Bio;
        AvatarImage.Source = null;
        AvatarInitialsText.Visibility = Visibility.Visible;
        AvatarImage.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(_profile.AvatarUrl))
        {
            var bitmap = await LoadAvatarAsync(_profile.Id, _profile.AvatarUrl);
            if (bitmap != null)
            {
                AvatarImage.Source = bitmap;
                AvatarImage.Visibility = Visibility.Visible;
                AvatarInitialsText.Visibility = Visibility.Collapsed;
            }
        }

        var online = _profile.IsOnline;
        StatusText.Text = online ? "● Online" : "● Offline";
        StatusText.Foreground = online
            ? Brushes.LimeGreen
            : (Brush)FindResource("SecondaryTextBrush");
        ProfileStatusDot.Fill = online
            ? Brushes.LimeGreen
            : (Brush)FindResource("SecondaryTextBrush");
        PresenceSummaryText.Text = online ? "Visible as online" : "Visible as offline";
        LastSeenText.Text = online
            ? "Active now"
            : (_profile.LastSeenAt.HasValue
                ? $"Last seen {FormatLastSeen(_profile.LastSeenAt.Value)}"
                : "Last seen not available");
        JoinedText.Text = $"Joined {_profile.CreatedAt.ToLocalTime():dd MMM yyyy}";
        CopyUserIdButton.ToolTip = $"Copy @{_profile.Id}";
    }

    private async Task<BitmapImage?> LoadAvatarAsync(string userId, string avatarUrl)
    {
        var cacheBust = Uri.EscapeDataString(
            $"{avatarUrl}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
        var endpoint = _apiService.BuildAbsoluteUrl(
            $"api/avatar/{Uri.EscapeDataString(userId)}?v={cacheBust}");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            if (!string.IsNullOrWhiteSpace(AuthState.Token))
            {
                request.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", AuthState.Token);
            }

            using var response = await AvatarHttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;

            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0) return null;

            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _profile == null) return;
        if (string.IsNullOrWhiteSpace(DisplayNameBox.Text) ||
            string.IsNullOrWhiteSpace(UserIdBox.Text) ||
            string.IsNullOrWhiteSpace(EmailBox.Text))
        {
            ShowFeedback("Display name, User ID and Email are required.");
            return;
        }

        if (BioBox.Text.Length > 160)
        {
            ShowFeedback("Bio must be 160 characters or less.");
            return;
        }

        _busy = true;
        SaveButton.IsEnabled = false;
        try
        {
            var oldId = _profile.Id;
            var request = new UpdateProfileRequest
            {
                DisplayName = DisplayNameBox.Text.Trim(),
                Email = EmailBox.Text.Trim(),
                Bio = BioBox.Text.Trim(),
                NewUserId = UserIdBox.Text.Trim()
            };

            var result = await _apiService.PutAsync<UpdateProfileRequest, ProfileActionResponse>(
                $"api/User/{Uri.EscapeDataString(oldId)}", request);

            if (result?.User == null)
            {
                ShowFeedback("Profile could not be updated. Check your values and try again.");
                return;
            }

            _profile = result.User;
            await UpdateProfileUiAsync();
            ShowFeedback(result.Message);

            if (!string.Equals(AuthState.UserId, _profile.Id, StringComparison.Ordinal))
            {
                AuthState.Clear();
                SessionExpired?.Invoke();
            }
            else
            {
                AuthState.UpdateProfile(_profile.DisplayName, _profile.Email);
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
            var result = await _apiService.UploadFileAsync<ProfileActionResponse>(
                $"api/User/{Uri.EscapeDataString(AuthState.UserId)}/avatar",
                dialog.FileName);

            if (result?.User == null)
            {
                ShowFeedback("Profile picture upload failed.");
            }
            else
            {
                _profile = result.User;
                await UpdateProfileUiAsync();
                ShowFeedback(result.Message);
            }
        }
        catch (Exception ex)
        {
            ShowFeedback($"Picture upload failed: {ex.Message}");
        }
        finally
        {
            _busy = false;
            SaveButton.IsEnabled = true;
        }
    }

    private async void RemovePictureButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _profile == null || string.IsNullOrWhiteSpace(_profile.AvatarUrl)) return;
        if (MessageBox.Show(
                "Remove your profile picture?",
                "NovaChat",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            return;
        }

        _busy = true;
        try
        {
            var removed = await _apiService.DeleteAsync(
                $"api/User/{Uri.EscapeDataString(_profile.Id)}/avatar");

            if (removed)
            {
                _profile.AvatarUrl = null;
                await UpdateProfileUiAsync();
                ShowFeedback("Profile picture removed.");
            }
            else
            {
                ShowFeedback("Profile picture could not be removed.");
            }
        }
        catch (Exception ex)
        {
            ShowFeedback($"Could not remove picture: {ex.Message}");
        }
        finally
        {
            _busy = false;
        }
    }

    private void CopyUserIdButton_Click(object sender, RoutedEventArgs e)
    {
        if (_profile == null || string.IsNullOrWhiteSpace(_profile.Id)) return;
        Clipboard.SetText(_profile.Id);
        ShowFeedback($"Copied @{_profile.Id} to clipboard.");
    }

    private async void ChangePasswordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !AuthState.IsAuthenticated) return;

        var dialog = new Window
        {
            Title = "Change password",
            Width = 420,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("PanelBackgroundBrush")
        };

        var root = new StackPanel { Margin = new Thickness(24) };
        root.Children.Add(new TextBlock
        {
            Text = "Change your password",
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = (Brush)FindResource("TextBrush")
        });
        root.Children.Add(new TextBlock
        {
            Text = "Choose a new password you do not reuse elsewhere.",
            FontSize = 12,
            Foreground = (Brush)FindResource("SecondaryTextBrush"),
            Margin = new Thickness(0, 4, 0, 18)
        });

        var currentBox = CreatePasswordBox(root, "Current password");
        var newBox = CreatePasswordBox(root, "New password");
        var confirmBox = CreatePasswordBox(root, "Confirm new password");

        var feedback = new TextBlock
        {
            Foreground = (Brush)FindResource("PrimaryBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 10)
        };
        root.Children.Add(feedback);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right
        };

        var cancel = new Button
        {
            Content = "Cancel",
            Width = 82,
            Height = 36,
            Margin = new Thickness(0, 0, 8, 0),
            Background = Brushes.Transparent,
            Foreground = (Brush)FindResource("PrimaryBrush"),
            BorderBrush = (Brush)FindResource("PrimaryBrush")
        };

        var save = new Button
        {
            Content = "Update",
            Width = 82,
            Height = 36,
            Background = (Brush)FindResource("PrimaryBrush"),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0)
        };

        actions.Children.Add(cancel);
        actions.Children.Add(save);
        root.Children.Add(actions);

        cancel.Click += (_, _) => dialog.Close();
        save.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(currentBox.Password) ||
                string.IsNullOrWhiteSpace(newBox.Password) ||
                string.IsNullOrWhiteSpace(confirmBox.Password))
            {
                feedback.Text = "Fill in all password fields.";
                return;
            }

            if (!string.Equals(newBox.Password, confirmBox.Password, StringComparison.Ordinal))
            {
                feedback.Text = "New passwords do not match.";
                return;
            }

            if (newBox.Password.Length < 6)
            {
                feedback.Text = "New password must be at least 6 characters.";
                return;
            }

            if (string.Equals(currentBox.Password, newBox.Password, StringComparison.Ordinal))
            {
                feedback.Text = "New password must be different from the current password.";
                return;
            }

            save.IsEnabled = false;
            cancel.IsEnabled = false;

            try
            {
                var request = new ChangePasswordRequest
                {
                    CurrentPassword = currentBox.Password,
                    NewPassword = newBox.Password
                };

                var response = await _apiService.PutAsync<ChangePasswordRequest, SimpleMessageResponse>(
                    $"api/User/{Uri.EscapeDataString(AuthState.UserId)}/password",
                    request);

                if (response == null)
                {
                    feedback.Text = "Password could not be changed. Check your current password and try again.";
                    return;
                }

                feedback.Text = response.Message;
                ShowFeedback(response.Message);

                if (response.Message.Contains("success", StringComparison.OrdinalIgnoreCase))
                    dialog.Close();
            }
            catch (Exception ex)
            {
                feedback.Text = $"Password update failed: {ex.Message}";
            }
            finally
            {
                save.IsEnabled = true;
                cancel.IsEnabled = true;
            }
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) => currentBox.Focus();
        dialog.ShowDialog();
    }

    private static PasswordBox CreatePasswordBox(Panel parent, string label)
    {
        parent.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)Application.Current.FindResource("TextBrush"),
            Margin = new Thickness(0, 6, 0, 5)
        });

        var box = new PasswordBox
        {
            Height = 40,
            Padding = new Thickness(10),
            Background = (Brush)Application.Current.FindResource("InputBackgroundBrush"),
            Foreground = (Brush)Application.Current.FindResource("TextBrush"),
            BorderBrush = (Brush)Application.Current.FindResource("BorderBrush")
        };

        parent.Children.Add(box);
        return box;
    }

    private static string GetInitials(string displayName, string id)
    {
        var value = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "?";
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant()
            : value[..Math.Min(2, value.Length)].ToUpperInvariant();
    }

    private static string FormatLastSeen(DateTime value)
    {
        var local = value.ToLocalTime();
        return local.Date == DateTime.Now.Date
            ? $"today at {local:HH:mm}"
            : local.ToString("dd MMM yyyy HH:mm");
    }

    private void ShowFeedback(string message) => FeedbackText.Text = message;
    private void BackToChatButton_Click(object sender, RoutedEventArgs e) => BackToChatRequested?.Invoke();
    private void ContactsButton_Click(object sender, RoutedEventArgs e) => ContactsRequested?.Invoke();

    private sealed class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }

    private sealed class SimpleMessageResponse
    {
        public string Message { get; set; } = string.Empty;
    }
}
