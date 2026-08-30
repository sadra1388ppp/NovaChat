using System.Collections.Concurrent;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows.Controls.Primitives;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static readonly HttpClient ConversationAvatarHttpClient = new();
    private static readonly ConcurrentDictionary<string, BitmapImage?> ConversationAvatarCache = new(StringComparer.OrdinalIgnoreCase);
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
            var url = item.Chat.OtherUserAvatarUrl;
            if (string.IsNullOrWhiteSpace(url)) continue;

            var container = ChatsList.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
            if (container == null) continue;

            var image = FindAvatarImage(container);
            if (image == null) continue;

            var absoluteUrl = _apiService.BuildAbsoluteUrl(url);
            var bitmap = await LoadConversationAvatarAsync(absoluteUrl);
            if (bitmap != null)
                image.Source = bitmap;
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

    private static async Task<BitmapImage?> LoadConversationAvatarAsync(string url)
    {
        if (ConversationAvatarCache.TryGetValue(url, out var cached)) return cached;

        try
        {
            using var response = await ConversationAvatarHttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode) return null;

            await using var networkStream = await response.Content.ReadAsStreamAsync();
            using var memoryStream = new MemoryStream();
            await networkStream.CopyToAsync(memoryStream);
            memoryStream.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.StreamSource = memoryStream;
            bitmap.EndInit();
            bitmap.Freeze();

            ConversationAvatarCache[url] = bitmap;
            return bitmap;
        }
        catch
        {
            ConversationAvatarCache[url] = null;
            return null;
        }
    }

    private void InitializeConversationAvatarFix()
        => HookConversationAvatarRefresh();
}
