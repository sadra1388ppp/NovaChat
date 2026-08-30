using NovaChat.Client.Models;
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
            _ = view.RefreshProfileAvatarRuntimeAsync();
    }

    private async Task RefreshProfileAvatarRuntimeAsync()
    {
        if (!AuthState.IsAuthenticated || string.IsNullOrWhiteSpace(AuthState.UserId)) return;

        try
        {
            await Task.Delay(50);
            var profile = await _apiService.GetAsync<ProfileModel>("api/User/profile/me");
            if (profile == null || string.IsNullOrWhiteSpace(profile.AvatarUrl)) return;

            var url = _apiService.BuildAbsoluteUrl(profile.AvatarUrl);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(url, UriKind.Absolute);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
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
            // Keep initials fallback when the stored file is unavailable.
        }
    }
}
