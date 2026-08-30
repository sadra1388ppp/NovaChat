using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
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
            var version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var avatarUri = new ApiService().BuildAbsoluteUrl($"api/avatar/{Uri.EscapeDataString(userId)}?v={version}");

            await Dispatcher.InvokeAsync(() =>
            {
                foreach (var item in _chats.Where(x => string.Equals(x.Chat.OtherUserId(AuthState.UserId), userId, StringComparison.OrdinalIgnoreCase)))
                    item.AvatarUri = avatarUri;

                if (string.Equals(_currentOtherUserId, userId, StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var bitmap = new BitmapImage(new Uri(avatarUri, UriKind.Absolute));
                        bitmap.CacheOption = BitmapCacheOption.OnLoad;
                        bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
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

            var url = new ApiService().BuildAbsoluteUrl($"api/avatar/{Uri.EscapeDataString(userId)}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}");
            var bitmap = new BitmapImage(new Uri(url, UriKind.Absolute));
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;

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
        if (root is TextBlock text && !string.IsNullOrWhiteSpace(text.Text) && text.Text.TrimStart().StartsWith("@", StringComparison.Ordinal))
            return text.Text.Trim();
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
