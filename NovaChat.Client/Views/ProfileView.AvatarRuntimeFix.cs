using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class ProfileView
{
    private static readonly bool AvatarRuntimeFixRegistered = RegisterAvatarRuntimeFix();

    private static bool RegisterAvatarRuntimeFix()
    {
        EventManager.RegisterClassHandler(
            typeof(ProfileView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnProfileAvatarRuntimeLoaded));
        return true;
    }

    private static void OnProfileAvatarRuntimeLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ProfileView view)
            _ = view.ForceLoadAvatarAsync();
    }

    private async Task ForceLoadAvatarAsync()
    {
        if (!AuthState.IsAuthenticated || string.IsNullOrWhiteSpace(AuthState.UserId)) return;

        try
        {
            await Task.Delay(100);
            var endpoint = $"api/avatar/{Uri.EscapeDataString(AuthState.UserId)}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            var bytes = await _apiService.GetBytesAsync(endpoint);
            if (bytes == null || bytes.Length == 0) return;

            var bitmap = new BitmapImage();
            using var stream = new MemoryStream(bytes);
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();

            await Dispatcher.InvokeAsync(() =>
            {
                AvatarImage.Source = bitmap;
                AvatarImage.Visibility = Visibility.Visible;
                AvatarInitialsText.Visibility = Visibility.Collapsed;
            });
        }
        catch
        {
            // Existing initials fallback remains visible when the server has no avatar.
        }
    }
}
