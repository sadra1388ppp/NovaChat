using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private HubConnection? _realtimeAttachedConnection;

    static MainView()
    {
        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnRealtimeViewLoaded));
    }

    private static void OnRealtimeViewLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainView view) return;

        view.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(view.AttachRealtimeHandlers));
    }

    private void AttachRealtimeHandlers()
    {
        if (_hubConnection == null || ReferenceEquals(_realtimeAttachedConnection, _hubConnection))
            return;

        _hubConnection.On<MessageDeletedPayload>("MessageDeleted", OnRealtimeMessageDeleted);
        _hubConnection.On<ChatDeletedPayload>("ChatDeleted", OnRealtimeChatDeleted);
        _hubConnection.On<ChatCreatedPayload>("ChatCreated", OnRealtimeChatCreated);

        _realtimeAttachedConnection = _hubConnection;
    }

    private async void OnRealtimeMessageDeleted(MessageDeletedPayload payload)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            _loadedMessageIds.Remove(payload.Id);

            if (_currentChatId == payload.ChatId)
            {
                var candidates = MessagesPanel.Children
                    .OfType<Border>()
                    .Where(IsRealtimeMessageBubble)
                    .Where(b => RealtimeMessageBubbleMatches(b, payload.Content, payload.SentAt))
                    .ToList();

                if (candidates.Count > 0)
                    MessagesPanel.Children.Remove(candidates[^1]);
            }

            if (_chats.FirstOrDefault(x => x.Chat.Id == payload.ChatId)?.Chat.LastMessage?.Id == payload.Id)
                _ = RefreshChatAfterRealtimeChangeAsync(payload.ChatId);
        });
    }

    private async void OnRealtimeChatDeleted(ChatDeletedPayload payload)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            var item = _chats.FirstOrDefault(x => x.Chat.Id == payload.ChatId);
            if (item != null)
                _chats.Remove(item);

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

        try
        {
            await LoadChatsAsync();
        }
        catch
        {
            // The normal chat list remains intact if a background refresh fails.
        }
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
            item.LastMessage = updated.LastMessage == null
                ? "No messages yet."
                : FormatLastMessage(updated.LastMessage);

            RefreshChatsList();
        }
        catch
        {
            // Ignore background refresh failures; the next real-time event can retry.
        }
    }

    private static bool IsRealtimeMessageBubble(Border border)
    {
        return border.Child is StackPanel panel &&
               panel.Children.OfType<TextBlock>().Any();
    }

    private static bool RealtimeMessageBubbleMatches(Border border, string content, DateTime sentAt)
    {
        if (border.Child is not StackPanel panel) return false;

        var text = panel.Children.OfType<TextBlock>().FirstOrDefault();
        var time = panel.Children.OfType<TextBlock>().Skip(1).FirstOrDefault();

        return text != null &&
               time != null &&
               string.Equals(text.Text, content, StringComparison.Ordinal) &&
               string.Equals(time.Text, sentAt.ToLocalTime().ToString("HH:mm"), StringComparison.Ordinal);
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
    }
}
