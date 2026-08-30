using NovaChat.Client.Services;
using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static readonly ConcurrentDictionary<string, BitmapImage> ConversationAvatarCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _conversationAvatarHooked;

    private void HookConversationAvatarRefresh()
    {
        if (_conversationAvatarHooked) return;
        _conversationAvatarHooked = true;
        ChatsList.ItemContainerGenerator.StatusChanged += ChatsList_ItemContainerGenerator_StatusChanged;
        Loaded += MainView_ConversationAvatarLoaded;
    }

    private async void MainView_ConversationAvatarLoaded(object sender, RoutedEventArgs e)
        => await RefreshConversationAvatarsAsync();

    private async void ChatsList_ItemContainerGenerator_StatusChanged(object? sender, EventArgs e)
    {
        if (ChatsList.ItemContainerGenerator.Status != GeneratorStatus.ContainersGenerated) return;
        await Dispatcher.InvokeAsync(RefreshConversationAvatarsAsync, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async Task RefreshConversationAvatarsAsync()
    {
        foreach (var item in _chats)
        {
            var userId = item.Chat.OtherUserId(AuthState.UserId);
            if (string.IsNullOrWhiteSpace(userId)) continue;
            var container = ChatsList.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
            if (container == null) continue;
            var image = FindAvatarImage(container);
            if (image == null) continue;

            var profile = await _apiService.GetAsync<NovaChat.Client.Models.ProfileModel>($"api/User/profile/{Uri.EscapeDataString(userId)}");
            var avatarUrl = profile?.AvatarUrl;
            if (string.IsNullOrWhiteSpace(avatarUrl)) continue;

            var bitmap = await LoadConversationAvatarAsync(userId, avatarUrl);
            if (bitmap != null)
            {
                item.AvatarSource = bitmap;
                image.Source = bitmap;
            }
        }
    }

    private async Task<BitmapImage?> LoadConversationAvatarAsync(string userId, string avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(avatarUrl)) return null;
        ConversationAvatarCache.TryRemoveWhereContains(userId);

        var endpoint = $"api/avatar/{Uri.EscapeDataString(userId)}?v={Uri.EscapeDataString(avatarUrl)}";
        try
        {
            var absolute = _apiService.BuildAbsoluteUrl(endpoint);
            var bytes = await _apiService.GetBytesAsync(endpoint);
            if (bytes == null || bytes.Length == 0) return null;

            using var memoryStream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.StreamSource = memoryStream;
            bitmap.EndInit();
            bitmap.Freeze();
            ConversationAvatarCache[absolute] = bitmap;
            return bitmap;
        }
        catch { return null; }
    }

    private async Task<BitmapImage?> LoadConversationAvatarAsync(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        try
        {
            var absolute = _apiService.BuildAbsoluteUrl(endpoint);
            var bytes = await _apiService.GetBytesAsync(endpoint);
            if (bytes == null || bytes.Length == 0) return null;
            if (ConversationAvatarCache.TryGetValue(absolute, out var cached)) return cached;

            using var memoryStream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.StreamSource = memoryStream;
            bitmap.EndInit();
            bitmap.Freeze();
            ConversationAvatarCache[absolute] = bitmap;
            return bitmap;
        }
        catch { return null; }
    }

    private void InitializeConversationAvatarFix()
        => HookConversationAvatarRefresh();
}

internal static class AvatarCacheExtensions
{
    public static void TryRemoveWhereContains(this ConcurrentDictionary<string, BitmapImage> cache, string userId)
    {
        foreach (var key in cache.Keys.Where(k => k.Contains($"api/avatar/{Uri.EscapeDataString(userId)}", StringComparison.OrdinalIgnoreCase)).ToArray())
            cache.TryRemove(key, out _);
    }
}