using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private async void ChatUserNameText_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentOtherUserId)) return;
        try
        {
            var userId = _currentOtherUserId.Trim();
            var profile = await _apiService.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(userId)}");
            if (profile == null)
            {
                MessageBox.Show("This user's profile could not be loaded.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                try { profile.IsOnline = await _hubConnection.InvokeAsync<bool>("IsUserOnline", userId); }
                catch { profile.IsOnline = IsUserOnline(userId); }
            }
            else
            {
                profile.IsOnline = IsUserOnline(userId);
            }

            ShowPublicProfile(profile);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load this profile.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task RefreshCurrentChatAvatarAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentOtherUserId))
        {
            ChatHeaderAvatarImage.Source = null;
            ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
            ChatAvatarInitialsText.Visibility = Visibility.Visible;
            ChatAvatarInitialsText.Text = "N";
            return;
        }

        try
        {
            var userId = _currentOtherUserId.Trim();
            var profile = await _apiService.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(userId)}");
            if (profile == null) return;

            var online = IsUserOnline(userId);
            if (_hubConnection?.State == HubConnectionState.Connected)
            {
                try { online = await _hubConnection.InvokeAsync<bool>("IsUserOnline", userId); }
                catch { }
            }

            await Dispatcher.InvokeAsync(() =>
            {
                ChatAvatarInitialsText.Text = GetPublicProfileInitials(profile.DisplayName, profile.Id);
                ChatStatusText.Text = online ? "Online" : "Offline";
                ChatStatusIndicator.Fill = online ? Brushes.LimeGreen : Brushes.Gray;
                ChatAvatarStatusDot.Fill = online ? Brushes.LimeGreen : Brushes.Gray;
                ChatAvatarInitialsText.Visibility = Visibility.Visible;
                ChatHeaderAvatarImage.Source = null;
                ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
            });

            if (string.IsNullOrWhiteSpace(profile.AvatarUrl)) return;

            var avatarUrl = _apiService.BuildAbsoluteUrl(profile.AvatarUrl);
            var separator = avatarUrl.Contains('?') ? '&' : '?';
            var versionedUrl = $"{avatarUrl}{separator}v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";

            try
            {
                var bitmap = await LoadStaticAvatarBitmapAsync(versionedUrl);
                if (bitmap == null || !string.Equals(_currentOtherUserId, profile.Id, StringComparison.OrdinalIgnoreCase)) return;
                await Dispatcher.InvokeAsync(() =>
                {
                    ChatHeaderAvatarImage.Source = bitmap;
                    ChatHeaderAvatarImage.Visibility = Visibility.Visible;
                    ChatAvatarInitialsText.Visibility = Visibility.Collapsed;
                });
            }
            catch { }
        }
        catch { }
    }

    private void ShowPublicProfile(ProfileModel profile)
    {
        var window = new Window
        {
            Title = $"{profile.DisplayName} · Profile",
            Width = 390,
            Height = 470,
            MinWidth = 350,
            MinHeight = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("PanelBackgroundBrush")
        };

        var root = new StackPanel { Margin = new Thickness(28) };
        var avatarGrid = new Grid { Width = 112, Height = 112, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 18) };
        avatarGrid.Children.Add(new Ellipse { Fill = (Brush)FindResource("SelectedChatBrush") });
        var initials = new TextBlock { Text = GetPublicProfileInitials(profile.DisplayName, profile.Id), Foreground = (Brush)FindResource("PrimaryBrush"), FontSize = 38, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        avatarGrid.Children.Add(initials);
        root.Children.Add(avatarGrid);
        root.Children.Add(new TextBlock { Text = profile.DisplayName, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center });
        root.Children.Add(new TextBlock { Text = $"@{profile.Id}", FontSize = 13, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 14) });
        root.Children.Add(new TextBlock { Text = profile.IsOnline ? "● Online" : "● Offline", FontSize = 12, Foreground = profile.IsOnline ? Brushes.LimeGreen : (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) });
        if (!string.IsNullOrWhiteSpace(profile.Bio)) root.Children.Add(new TextBlock { Text = profile.Bio, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = (Brush)FindResource("TextBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(10, 0, 10, 18) });
        root.Children.Add(new TextBlock { Text = profile.IsOnline ? "Active now" : (profile.LastSeenAt.HasValue ? $"Last seen {FormatPublicLastSeen(profile.LastSeenAt.Value)}" : "Last seen not available"), FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        root.Children.Add(new TextBlock { Text = $"Joined {profile.CreatedAt.ToLocalTime():dd MMM yyyy}", FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 20) });
        var closeButton = new Button { Content = "Close", Width = 100, Height = 36, HorizontalAlignment = HorizontalAlignment.Center, Background = (Brush)FindResource("PrimaryBrush"), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        closeButton.Click += (_, _) => window.Close();
        root.Children.Add(closeButton);
        window.Content = root;

        _ = LoadPublicProfileAvatarAsync(profile, avatarGrid, initials);
        window.ShowDialog();
    }

    private async Task LoadPublicProfileAvatarAsync(ProfileModel profile, Grid avatarGrid, TextBlock initials)
    {
        if (string.IsNullOrWhiteSpace(profile.AvatarUrl)) return;

        try
        {
            var url = _apiService.BuildAbsoluteUrl(profile.AvatarUrl);
            var separator = url.Contains('?') ? '&' : '?';
            var versionedUrl = $"{url}{separator}v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var bitmap = await LoadStaticAvatarBitmapAsync(versionedUrl);
            if (bitmap == null) return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (avatarGrid.Children.OfType<Image>().Any()) return;
                var image = new Image
                {
                    Width = 112,
                    Height = 112,
                    Stretch = Stretch.UniformToFill,
                    Clip = new EllipseGeometry(new Point(56, 56), 56, 56),
                    Source = bitmap
                };
                avatarGrid.Children.Add(image);
                initials.Visibility = Visibility.Collapsed;
            });
        }
        catch { }
    }

    private static async Task<BitmapImage?> LoadStaticAvatarBitmapAsync(string absoluteUrl)
    {
        if (string.IsNullOrWhiteSpace(absoluteUrl)) return null;

        try
        {
            using var http = new HttpClient();
            using var response = await http.GetAsync(absoluteUrl, HttpCompletionOption.ResponseHeadersRead);
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

    private static string GetPublicProfileInitials(string displayName, string id)
    {
        var value = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "?";
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant() : value[..Math.Min(2, value.Length)].ToUpperInvariant();
    }

    private static string FormatPublicLastSeen(DateTime value)
    {
        var local = value.ToLocalTime();
        return local.Date == DateTime.Now.Date ? $"today at {local:HH:mm}" : local.ToString("dd MMM yyyy HH:mm");
    }
}
