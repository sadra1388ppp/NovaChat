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

            var profile = await new ApiService().GetAsync<NovaChat.Client.Models.ProfileModel>($"api/User/profile/{Uri.EscapeDataString(userId)}");
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

    private static Image? FindAvatarImage(DependencyObject root)
    {
        if (root is Image image) return image;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var result = FindAvatarImage(VisualTreeHelper.GetChild(root, i));
            if (result != null) return result;
        }
        return null;
    }

    private async Task<BitmapImage?> LoadConversationAvatarAsync(string userId, string avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(avatarUrl)) return null;

        var apiEndpoint = $"api/User/profile/{Uri.EscapeDataString(userId)}/avatar?v={Uri.EscapeDataString(avatarUrl)}";
        var staticEndpoint = avatarUrl.StartsWith('/') ? avatarUrl : "/" + avatarUrl;

        foreach (var endpoint in new[] { apiEndpoint, staticEndpoint })
        {
            try
            {
                var cacheKey = new ApiService().BuildAbsoluteUrl(endpoint);
                if (ConversationAvatarCache.TryGetValue(cacheKey, out var cached)) return cached;

                var bytes = await new ApiService().GetBytesAsync(endpoint);
                if (bytes == null || bytes.Length == 0) continue;

                using var memoryStream = new MemoryStream(bytes);
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bitmap.StreamSource = memoryStream;
                bitmap.EndInit();
                bitmap.Freeze();

                ConversationAvatarCache[cacheKey] = bitmap;
                return bitmap;
            }
            catch
            {
                // Try the next supported avatar source.
            }
        }

        return null;
    }

    private void InitializeConversationAvatarFix()
        => HookConversationAvatarRefresh();
}
