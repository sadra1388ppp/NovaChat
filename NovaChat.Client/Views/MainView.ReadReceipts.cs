using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private readonly Dictionary<int, HashSet<string>> _messageReaders = [];
    private readonly HashSet<string> _currentRecipientIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _readReceiptHandlersRegistered;
    private static readonly bool ReadReceiptClassHandlerRegistered = RegisterReadReceiptClassHandler();

    private static bool RegisterReadReceiptClassHandler()
    {
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnReadReceiptViewLoaded));
        return true;
    }

    private static async void OnReadReceiptViewLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainView view) return;
        for (var i = 0; i < 30 && view._hubConnection == null; i++) await Task.Delay(100);
        if (view._hubConnection == null) return;
        view.RegisterReadReceiptHandlers();
        await view.RefreshUnreadCountsAsync();
    }

    private void RegisterReadReceiptHandlers()
    {
        if (_readReceiptHandlersRegistered || _hubConnection == null) return;
        _readReceiptHandlersRegistered = true;
        _hubConnection.On<MessagesReadEvent>("MessagesRead", OnMessagesRead);
        _hubConnection.On<string>("UserOnline", OnReceiptPresenceOnline);
        _hubConnection.On<string>("UserOffline", OnReceiptPresenceOffline);
    }

    private async Task RefreshUnreadCountsAsync()
    {
        try
        {
            var counts = await _apiService.GetAsync<Dictionary<string, int>>("api/message-read/unread") ?? [];
            await Dispatcher.InvokeAsync(() =>
            {
                foreach (var item in _chats) item.UnreadCount = counts.TryGetValue(item.Chat.Id.ToString(), out var count) ? count : 0;
            });
        }
        catch { }
    }

    private async Task MarkCurrentChatAsReadAsync()
    {
        if (!_currentChatId.HasValue) return;
        var chatId = _currentChatId.Value;
        try
        {
            var response = await _apiService.PostAsync<object, ReadMessagesResponse>($"api/message-read/{chatId}/read", new { });
            var item = _chats.FirstOrDefault(x => x.Chat.Id == chatId);
            if (item != null) item.UnreadCount = 0;
            if (_hubConnection?.State == HubConnectionState.Connected) await _hubConnection.InvokeAsync("MarkChatAsRead", chatId);
        }
        catch { }
    }

    private void OnMessagesRead(MessagesReadEvent evt)
    {
        if (evt == null) return;
        Dispatcher.InvokeAsync(() =>
        {
            foreach (var id in evt.MessageIds ?? [])
            {
                if (!_messageReaders.TryGetValue(id, out var readers)) _messageReaders[id] = readers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (!string.IsNullOrWhiteSpace(evt.ReaderUserId)) readers.Add(evt.ReaderUserId);
            }
            UpdateMessageReceiptsUi();
        });
    }

    private void OnReceiptPresenceOnline(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        Dispatcher.InvokeAsync(UpdateMessageReceiptsUi);
    }

    private void OnReceiptPresenceOffline(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        Dispatcher.InvokeAsync(UpdateMessageReceiptsUi);
    }

    private void PrepareCurrentRecipients(ChatModel chat)
    {
        _currentRecipientIds.Clear();
        if (chat.IsGroup)
        {
            _ = LoadGroupRecipientsAsync(chat.Id);
            return;
        }
        var other = chat.OtherUserId(AuthState.UserId);
        if (!string.IsNullOrWhiteSpace(other)) _currentRecipientIds.Add(other);
    }

    private async Task LoadGroupRecipientsAsync(int chatId)
    {
        try
        {
            var members = await _apiService.GetAsync<List<GroupRecipientModel>>($"api/Chat/{chatId}/members") ?? [];
            await Dispatcher.InvokeAsync(() =>
            {
                _currentRecipientIds.Clear();
                foreach (var member in members)
                    if (!string.Equals(member.UserId, AuthState.UserId, StringComparison.OrdinalIgnoreCase)) _currentRecipientIds.Add(member.UserId);
                UpdateMessageReceiptsUi();
            });
        }
        catch { }
    }

    private void UpdateMessageReceiptsUi()
    {
        if (_currentChatId == null) return;
        var anyRecipientOnline = _currentRecipientIds.Any(IsUserOnline);
        foreach (var border in MessagesPanel.Children.OfType<Border>())
        {
            if (border.Tag is not int messageId) continue;
            var message = FindMessageById(messageId);
            if (message == null || !string.Equals(message.SenderId, AuthState.Username, StringComparison.OrdinalIgnoreCase)) continue;
            var readers = _messageReaders.TryGetValue(messageId, out var set) ? set : [];
            var seen = _currentRecipientIds.Count > 0 && _currentRecipientIds.All(readers.Contains);
            var state = seen ? "seen" : anyRecipientOnline ? "delivered" : "sent";
            message.DeliveryState = state;
            if (FindReceiptText(border) is TextBlock receipt)
            {
                receipt.Text = state switch { "seen" => "✓✓", "delivered" => "✓✓", _ => "✓" };
                receipt.Foreground = state == "seen" ? Brushes.DeepSkyBlue : Brushes.White;
            }
        }
    }

    private MessageModel? FindMessageById(int id)
    {
        if (_currentChatId == null) return null;
        var item = _chats.FirstOrDefault(x => x.Chat.Id == _currentChatId.Value);
        return item?.Chat.LastMessage?.Id == id ? item.Chat.LastMessage : null;
    }

    private static TextBlock? FindReceiptText(Border border)
    {
        if (border.Child is not StackPanel panel) return null;
        return panel.Children.OfType<TextBlock>().FirstOrDefault(x => Equals(x.Tag, "receipt"));
    }

    private sealed class MessagesReadEvent { public int ChatId { get; set; } public string ReaderUserId { get; set; } = string.Empty; public List<int> MessageIds { get; set; } = []; }
    private sealed class ReadMessagesResponse { public int ChatId { get; set; } public List<int> MessageIds { get; set; } = []; }
    private sealed class GroupRecipientModel { public string UserId { get; set; } = string.Empty; }
}
