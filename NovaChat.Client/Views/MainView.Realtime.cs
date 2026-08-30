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
    private HubConnection? _realtimeAttachedConnection;
    private int _realtimeAttachAttempts;

    static MainView()
    {
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnRealtimeViewLoaded));
    }

    private static void OnRealtimeViewLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainView view) return;
        view._realtimeAttachAttempts = 0;
        view.QueueRealtimeHandlerAttach();
    }

    private void QueueRealtimeHandlerAttach()
    {
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(AttachRealtimeHandlersWhenReady));
    }

    private async void AttachRealtimeHandlersWhenReady()
    {
        AttachRealtimeHandlers();
        if (_realtimeAttachedConnection != null || _hubConnection == null) return;
        if (_realtimeAttachAttempts++ >= 30) return;
        await Task.Delay(100);
        if (!IsLoaded) return;
        QueueRealtimeHandlerAttach();
    }

    private void AttachRealtimeHandlers()
    {
        if (_hubConnection == null || ReferenceEquals(_realtimeAttachedConnection, _hubConnection)) return;
        _hubConnection.On<MessageDeletedPayload>("MessageDeleted", OnRealtimeMessageDeleted);
        _hubConnection.On<ChatDeletedPayload>("ChatDeleted", OnRealtimeChatDeleted);
        _hubConnection.On<ChatCreatedPayload>("ChatCreated", OnRealtimeChatCreated);
        _hubConnection.On<ProfileUpdatedPayload>("ProfileUpdated", OnRealtimeProfileUpdated);
        _realtimeAttachedConnection = _hubConnection;
    }

    private async void OnRealtimeMessageDeleted(MessageDeletedPayload payload)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            _loadedMessageIds.Remove(payload.Id);
            if (_currentChatId == payload.ChatId)
            {
                var candidates = MessagesPanel.Children.OfType<Border>()
                    .Where(IsRealtimeMessageBubble)
                    .Where(b => RealtimeMessageBubbleMatches(b, payload.Content, payload.SentAt))
                    .ToList();
                if (candidates.Count > 0) MessagesPanel.Children.Remove(candidates[^1]);
            }
            if (_chats.FirstOrDefault(x => x.Chat.Id == payload.ChatId)?.Chat.LastMessage?.Id == payload.Id)
                _ = RefreshChatAfterRealtimeChangeAsync(payload.ChatId);
        });
    }

    private async void OnRealtimeChatDeleted(ChatDeletedPayload payload)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            _chats.RemoveAll(x => x.Chat.Id == payload.ChatId);
            if (_currentChatId == payload.ChatId)
            {
                _currentChatId = null;
                _currentOtherUserId = string.Empty;
                _oldestLoadedMessageId = null;
                _hasMoreMessages = false;
                _loadedMessageIds.Clear();
                ChatUserNameText.Text = "Select a chat";
                ChatStatusText.Text = "Offline";
                ChatStatusIndicator.Fill = Brushes.Gray;
                ChatHeaderAvatarImage.Source = null;
                ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
                ChatAvatarInitialsText.Text = "N";
                ChatAvatarInitialsText.Visibility = Visibility.Visible;
                MessagesPanel.Children.Clear();
                MessageTextBox.Clear();
                UpdateLoadOlderButton();
            }
            RefreshChatsList();
        });
    }

    private async void OnRealtimeChatCreated(ChatCreatedPayload payload)
    {
        if (payload.Id <= 0) return;
        if (string.Equals(payload.CreatedBy, AuthState.UserId, StringComparison.OrdinalIgnoreCase)) return;
        try { await LoadChatsAsync(); } catch { }
    }

    private async void OnRealtimeProfileUpdated(ProfileUpdatedPayload payload)
    {
        if (string.IsNullOrWhiteSpace(payload.UserId)) return;

        await Dispatcher.InvokeAsync(async () =>
        {
            ConversationAvatarCache.Clear();

            foreach (var item in _chats.Where(x => string.Equals(x.Chat.OtherUserId(AuthState.UserId), payload.UserId, StringComparison.OrdinalIgnoreCase)))
            {
                item.Chat.User1AvatarUrl = string.Equals(item.Chat.User1Id, payload.UserId, StringComparison.OrdinalIgnoreCase) ? payload.AvatarUrl : item.Chat.User1AvatarUrl;
                item.Chat.User2AvatarUrl = string.Equals(item.Chat.User2Id, payload.UserId, StringComparison.OrdinalIgnoreCase) ? payload.AvatarUrl : item.Chat.User2AvatarUrl;
                if (!string.IsNullOrWhiteSpace(payload.DisplayName)) item.DisplayName = payload.DisplayName;
                item.IsOnline = IsUserOnline(payload.UserId);
                item.AvatarSource = null;
            }

            RefreshChatsList();
            await RefreshConversationAvatarsAsync();

            if (string.Equals(_currentOtherUserId, payload.UserId, StringComparison.OrdinalIgnoreCase))
                await RefreshCurrentChatAvatarAsync();
        });
    }

    private async Task RefreshChatAfterRealtimeChangeAsync(int chatId)
    {
        try
        {
            var chats = await _apiService.GetAsync<List<ChatModel>>("api/Chat");
            var updated = chats?.FirstOrDefault(x => x.Id == chatId);
            var item = _chats.FirstOrDefault(x => x.Chat.Id == chatId);
            if (updated == null || item == null) return;
            item.Chat.LastMessage = updated.LastMessage;
            item.LastMessage = updated.LastMessage == null ? "No messages yet." : FormatLastMessage(updated.LastMessage);
            RefreshChatsList();
        }
        catch { }
    }

    private static bool IsRealtimeMessageBubble(Border border) => border.Child is StackPanel panel && panel.Children.OfType<TextBlock>().Any();

    private static bool RealtimeMessageBubbleMatches(Border border, string content, DateTime sentAt)
    {
        if (border.Child is not StackPanel panel) return false;
        var text = panel.Children.OfType<TextBlock>().FirstOrDefault();
        var time = panel.Children.OfType<TextBlock>().Skip(1).FirstOrDefault();
        return text != null && time != null && string.Equals(text.Text, content, StringComparison.Ordinal) && string.Equals(time.Text, sentAt.ToLocalTime().ToString("HH:mm"), StringComparison.Ordinal);
    }

    private async Task<BitmapImage?> LoadConversationAvatarAsync(string endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint)) return null;
        try
        {
            var api = new ApiService();
            var absoluteUrl = api.BuildAbsoluteUrl(endpoint);
            if (ConversationAvatarCache.TryGetValue(absoluteUrl, out var cached)) return cached;
            var bytes = await api.GetBytesAsync(endpoint);
            if (bytes == null || bytes.Length == 0) return null;
            using var stream = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            ConversationAvatarCache[absoluteUrl] = bitmap;
            return bitmap;
        }
        catch { return null; }
    }

    private sealed class MessageDeletedPayload
    {
        public int Id { get; set; }
        public int ChatId { get; set; }
        public string SenderId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
    }

    private sealed class ChatDeletedPayload
    {
        public int ChatId { get; set; }
        public string DeletedBy { get; set; } = string.Empty;
    }

    private sealed class ChatCreatedPayload
    {
        public int Id { get; set; }
        public string User1Id { get; set; } = string.Empty;
        public string User2Id { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
    }

    private sealed class ProfileUpdatedPayload
    {
        public string UserId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public string? AvatarUrl { get; set; }
    }
}
