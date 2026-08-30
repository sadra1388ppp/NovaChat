using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Net.Http;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private bool _conversationAvatarHooked;

    private void HookConversationAvatarRefresh()
    {
        // Refresh is driven explicitly by LoadChatsAsync and ProfileUpdated.
        // No global Loaded/container handlers are used, because those caused
        // repeated UI rebuilds and avatar flicker.
        _conversationAvatarHooked = true;
    }

    private async Task RefreshConversationAvatarsAsync()
    {
        foreach (var item in _chats.ToArray())
        {
            var userId = item.Chat.OtherUserId(AuthState.UserId);
            if (string.IsNullOrWhiteSpace(userId)) continue;

            try
            {
                var profile = await _apiService.GetAsync<ProfileModel>(
                    $"api/User/profile/{Uri.EscapeDataString(userId)}");
                if (profile == null) continue;

                item.DisplayName = profile.DisplayName;
                item.IsOnline = profile.IsOnline;
                item.AvatarUri = string.IsNullOrWhiteSpace(profile.AvatarUrl)
                    ? null
                    : _apiService.BuildAbsoluteUrl(profile.AvatarUrl);
            }
            catch
            {
                // Keep the last known state on transient failures.
            }
        }

        RefreshChatsList();
    }

    // Compatibility helper for existing MainView code paths.
    // It performs one direct HTTP read and never mutates the chat list.
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
        catch
        {
            return null;
        }
    }

    private void InitializeConversationAvatarFix()
        => HookConversationAvatarRefresh();
}
