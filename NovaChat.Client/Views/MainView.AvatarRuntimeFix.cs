using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static readonly bool AvatarRuntimeFixRegistered = RegisterAvatarRuntimeFix();
    private HubConnection? _avatarFixConnection;

    private static bool RegisterAvatarRuntimeFix()
    {
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnMainAvatarRuntimeLoaded));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnProfileWindowLoaded));
        return true;
    }

    private static void OnMainAvatarRuntimeLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view)
            _ = view.InitializeAvatarRuntimeFixAsync();
    }

    private async Task InitializeAvatarRuntimeFixAsync()
    {
        try
        {
            for (var i = 0; i < 30 && _hubConnection == null && IsLoaded; i++)
                await Task.Delay(100);

            if (_hubConnection != null && !ReferenceEquals(_avatarFixConnection, _hubConnection))
            {
                _hubConnection.On<AvatarProfileUpdatedPayload>("ProfileUpdated", OnAvatarProfileUpdated);
                _avatarFixConnection = _hubConnection;
            }

            await ForceRefreshAllAvatarsAsync();
        }
        catch { }
    }

    private async void OnAvatarProfileUpdated(AvatarProfileUpdatedPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.UserId)) return;
        await ForceRefreshUserAvatarAsync(payload.UserId);
    }

    private async Task ForceRefreshAllAvatarsAsync()
    {
        foreach (var item in _chats.ToArray())
        {
            var userId = item.Chat.OtherUserId(AuthState.UserId);
            if (!string.IsNullOrWhiteSpace(userId))
                await ForceRefreshUserAvatarAsync(userId);
        }

        if (!string.IsNullOrWhiteSpace(_currentOtherUserId))
            await ForceRefreshUserAvatarAsync(_currentOtherUserId);
    }

    private async Task ForceRefreshUserAvatarAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;

        try
        {
            var profile = await _apiService.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(userId)}");
            if (profile == null) return;

            var avatarUri = string.IsNullOrWhiteSpace(profile.AvatarUrl)
                ? null
                : _apiService.BuildAbsoluteUrl(profile.AvatarUrl);

            await Dispatcher.InvokeAsync(() =>
            {
                foreach (var item in _chats.Where(x => string.Equals(x.Chat.OtherUserId(AuthState.UserId), userId, StringComparison.OrdinalIgnoreCase)))
                {
                    item.AvatarUri = avatarUri;
                    item.DisplayName = profile.DisplayName;
                    item.IsOnline = IsUserOnline(userId);
                }

                if (string.Equals(_currentOtherUserId, userId, StringComparison.OrdinalIgnoreCase))
                {
                    var online = IsUserOnline(userId);
                    ChatAvatarInitialsText.Text = GetPublicProfileInitials(profile.DisplayName, profile.Id);
                    ChatStatusText.Text = online ? "Online" : "Offline";
                    ChatStatusIndicator.Fill = online ? Brushes.LimeGreen : Brushes.Gray;
                    ChatAvatarStatusDot.Fill = online ? Brushes.LimeGreen : Brushes.Gray;

                    if (string.IsNullOrWhiteSpace(avatarUri))
                    {
                        ChatHeaderAvatarImage.Source = null;
                        ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
                        ChatAvatarInitialsText.Visibility = Visibility.Visible;
                        return;
                    }

                    try
                    {
                        var bitmap = new BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(avatarUri, UriKind.Absolute);
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                        bitmap.EndInit();
                        bitmap.Freeze();
                        ChatHeaderAvatarImage.Source = bitmap;
                        ChatHeaderAvatarImage.Visibility = Visibility.Visible;
                        ChatAvatarInitialsText.Visibility = Visibility.Collapsed;
                    }
                    catch
                    {
                        ChatHeaderAvatarImage.Source = null;
                        ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
                        ChatAvatarInitialsText.Visibility = Visibility.Visible;
                    }
                }
            });
        }
        catch { }
    }

    private static void OnProfileWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Window window) return;
        if (string.IsNullOrWhiteSpace(window.Title) || !window.Title.EndsWith(" · Profile", StringComparison.Ordinal)) return;
        _ = ForceRefreshPublicProfileWindowAvatarAsync(window);
    }

    private static async Task ForceRefreshPublicProfileWindowAvatarAsync(Window window)
    {
        try
        {
            await Task.Delay(80);
            var idText = FindTextStartingWithAt(window.Content as DependencyObject);
            var userId = idText?.TrimStart('@');
            if (string.IsNullOrWhiteSpace(userId)) return;

            var api = new ApiService();
            var profile = await api.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(userId)}");
            if (profile == null || string.IsNullOrWhiteSpace(profile.AvatarUrl)) return;

            var url = api.BuildAbsoluteUrl(profile.AvatarUrl);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(url, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.EndInit();
            bitmap.Freeze();

            await window.Dispatcher.InvokeAsync(() =>
            {
                var firstGrid = FindFirstGrid(window.Content as DependencyObject);
                if (firstGrid == null) return;
                var image = firstGrid.Children.OfType<Image>().FirstOrDefault();
                if (image != null)
                {
                    image.Source = bitmap;
                    return;
                }

                var ellipse = firstGrid.Children.OfType<Ellipse>().FirstOrDefault();
                if (ellipse == null) return;
                firstGrid.Children.Add(new Image
                {
                    Width = 112,
                    Height = 112,
                    Stretch = Stretch.UniformToFill,
                    Clip = new EllipseGeometry(new Point(56, 56), 56, 56),
                    Source = bitmap
                });
                foreach (var text in firstGrid.Children.OfType<TextBlock>()) text.Visibility = Visibility.Collapsed;
            });
        }
        catch { }
    }

    private static string? FindTextStartingWithAt(DependencyObject? root)
    {
        if (root is TextBlock text && !string.IsNullOrWhiteSpace(text.Text) && text.Text.TrimStart().StartsWith("@", StringComparison.Ordinal)) return text.Text.Trim();
        if (root == null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = FindTextStartingWithAt(VisualTreeHelper.GetChild(root, i));
            if (!string.IsNullOrWhiteSpace(result)) return result;
        }
        return null;
    }

    private static Grid? FindFirstGrid(DependencyObject? root)
    {
        if (root is Grid grid && grid.Width >= 100 && grid.Height >= 100) return grid;
        if (root == null) return null;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = FindFirstGrid(VisualTreeHelper.GetChild(root, i));
            if (result != null) return result;
        }
        return null;
    }

    private sealed class AvatarProfileUpdatedPayload
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
    }
}
