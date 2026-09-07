using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private bool _conversationAvatarHooked;
    private void HookConversationAvatarRefresh() => _conversationAvatarHooked = true;

    private async Task RefreshConversationAvatarsAsync()
    {
        var changed = false;

        foreach (var item in _chats.ToArray())
        {
            if (item.Chat.IsGroup)
            {
                var avatarUri = string.IsNullOrWhiteSpace(item.Chat.AvatarUrl)
                    ? null
                    : _apiService.BuildAbsoluteUrl(item.Chat.AvatarUrl);

                if (!string.Equals(item.AvatarUri, avatarUri, StringComparison.Ordinal))
                {
                    item.AvatarUri = avatarUri;
                    changed = true;
                }

                continue;
            }

            var userId = item.Chat.OtherUserId(AuthState.UserId);
            if (string.IsNullOrWhiteSpace(userId)) continue;

            try
            {
                var profile = await _apiService.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(userId)}");
                if (profile == null) continue;

                var avatarUri = string.IsNullOrWhiteSpace(profile.AvatarUrl)
                    ? null
                    : _apiService.BuildAbsoluteUrl(profile.AvatarUrl);

                if (!string.Equals(item.DisplayName, profile.DisplayName, StringComparison.Ordinal) ||
                    item.IsOnline != profile.IsOnline ||
                    !string.Equals(item.AvatarUri, avatarUri, StringComparison.Ordinal))
                {
                    item.DisplayName = profile.DisplayName;
                    item.IsOnline = profile.IsOnline;
                    item.AvatarUri = avatarUri;
                    changed = true;
                }
            }
            catch { }
        }

        // Do not rebuild the entire conversations list every second when nothing changed.
        // Rebinding the ItemsControl was causing group avatars to visibly disappear/reappear.
        if (changed)
            RefreshChatsList();
    }

    private static async Task<BitmapImage?> LoadConversationAvatarAsync(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;

        try
        {
            var absolute = Uri.TryCreate(endpoint, UriKind.Absolute, out var parsed)
                ? parsed.ToString()
                : new ApiService().BuildAbsoluteUrl(endpoint);

            using var http = new HttpClient();
            using var response = await http.GetAsync(absolute, HttpCompletionOption.ResponseHeadersRead);
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
        catch { return null; }
    }

    private void InitializeConversationAvatarFix() => HookConversationAvatarRefresh();
}
