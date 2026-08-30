using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private async void ChatHeaderAvatarDirect_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (string.IsNullOrWhiteSpace(_currentOtherUserId)) return;
        await RenderHeaderAvatarDirectAsync(_currentOtherUserId);
        ChatUserNameText_Click(sender, new RoutedEventArgs());
    }

    private async Task RenderHeaderAvatarDirectAsync(string userId)
    {
        try
        {
            var profile = await _apiService.GetAsync<ProfileModel>(
                $"api/User/profile/{Uri.EscapeDataString(userId)}");
            if (profile == null) return;

            BitmapImage? bitmap = null;
            if (!string.IsNullOrWhiteSpace(profile.AvatarUrl))
            {
                var url = _apiService.BuildAbsoluteUrl(profile.AvatarUrl);
                var separator = url.Contains('?') ? '&' : '?';
                bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(
                    $"{url}{separator}v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}",
                    UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.EndInit();
                bitmap.Freeze();
            }

            await Dispatcher.InvokeAsync(() =>
            {
                ChatAvatarInitialsText.Text = GetPublicProfileInitials(profile.DisplayName, profile.Id);
                if (bitmap != null)
                {
                    ChatHeaderAvatarImage.Source = bitmap;
                    ChatHeaderAvatarImage.Visibility = Visibility.Visible;
                    ChatAvatarInitialsText.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ChatHeaderAvatarImage.Source = null;
                    ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
                    ChatAvatarInitialsText.Visibility = Visibility.Visible;
                }
            });
        }
        catch { }
    }
}
