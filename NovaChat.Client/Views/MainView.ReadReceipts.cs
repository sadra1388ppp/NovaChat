using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private readonly Dictionary<int, HashSet<string>> _messageReaders = [];
    private readonly HashSet<string> _currentRecipientIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _readReceiptHandlersRegistered;
    private DispatcherTimer? _unreadRefreshTimer;
    private static readonly bool ReadReceiptClassHandlerRegistered = RegisterReadReceiptClassHandler();

    private static bool RegisterReadReceiptClassHandler()
    {
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnReadReceiptViewLoaded));
        EventManager.RegisterClassHandler(typeof(Button), Button.ClickEvent, new RoutedEventHandler(OnConversationButtonClicked));
        return true;
    }

    private static async void OnReadReceiptViewLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainView view) return;
        for (var i = 0; i < 30 && view._hubConnection == null; i++) await Task.Delay(100);
        if (view._hubConnection != null) view.RegisterReadReceiptHandlers();
        await view.RefreshUnreadCountsAsync();
        view.StartUnreadRefreshTimer();
    }

    private static async void OnConversationButtonClicked(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ChatListItem item }) return;
        if (FindAncestor<MainView>((DependencyObject)sender) is not MainView view) return;
        await Task.Delay(350);
        if (view._currentChatId != item.Chat.Id) return;
        view.PrepareCurrentRecipients(item.Chat);
        await view.MarkCurrentChatAsReadAsync();
        view.UpdateMessageReceiptsUi();
    }

    private void RegisterReadReceiptHandlers()
    {
        if (_readReceiptHandlersRegistered || _hubConnection == null) return;
        _readReceiptHandlersRegistered = true;
        _hubConnection.On<MessagesReadEvent>("MessagesRead", OnMessagesRead);
        _hubConnection.On<string>("UserOnline", OnReceiptPresenceOnline);
        _hubConnection.On<string>("UserOffline", OnReceiptPresenceOffline);
    }

    private void StartUnreadRefreshTimer()
    {
        if (_unreadRefreshTimer != null) return;
        _unreadRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _unreadRefreshTimer.Tick += async (_, _) => await RefreshUnreadCountsAsync();
        _unreadRefreshTimer.Start();
    }

    private async Task RefreshUnreadCountsAsync()
    {
        try
        {
            var counts = await _apiService.GetAsync<Dictionary<string, int>>("api/message-read/unread") ?? [];
            await Dispatcher.InvokeAsync(() => { foreach (var item in _chats) item.UnreadCount = counts.TryGetValue(item.Chat.Id.ToString(), out var count) ? count : 0; });
        }
        catch { }
    }

    private async Task MarkCurrentChatAsReadAsync()
    {
        if (!_currentChatId.HasValue) return;
        var chatId = _currentChatId.Value;
        try
        {
            var item = _chats.FirstOrDefault(x => x.Chat.Id == chatId);
            if (item != null) item.UnreadCount = 0;
            if (_hubConnection?.State == HubConnectionState.Connected)
                await _hubConnection.InvokeAsync("MarkChatAsRead", chatId);
            else
                await _apiService.PostAsync<object, ReadMessagesResponse>($"api/message-read/{chatId}/read", new { });
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

    private void OnReceiptPresenceOnline(string userId) { if (!string.IsNullOrWhiteSpace(userId)) Dispatcher.InvokeAsync(UpdateMessageReceiptsUi); }
    private void OnReceiptPresenceOffline(string userId) { if (!string.IsNullOrWhiteSpace(userId)) Dispatcher.InvokeAsync(UpdateMessageReceiptsUi); }
    private void PrepareCurrentRecipients(ChatModel chat) { _currentRecipientIds.Clear(); if (chat.IsGroup) { _ = LoadGroupRecipientsAsync(chat.Id); return; } var other = chat.OtherUserId(AuthState.UserId); if (!string.IsNullOrWhiteSpace(other)) _currentRecipientIds.Add(other); }

    private async Task LoadGroupRecipientsAsync(int chatId)
    {
        try
        {
            var members = await _apiService.GetAsync<List<GroupRecipientModel>>($"api/Chat/{chatId}/members") ?? [];
            await Dispatcher.InvokeAsync(() => { _currentRecipientIds.Clear(); foreach (var member in members) if (!string.Equals(member.UserId, AuthState.UserId, StringComparison.OrdinalIgnoreCase)) _currentRecipientIds.Add(member.UserId); UpdateMessageReceiptsUi(); });
        }
        catch { }
    }

    private void UpdateMessageReceiptsUi()
    {
        if (_currentChatId == null) return;
        var anyRecipientOnline = _currentRecipientIds.Any(IsUserOnline);
        foreach (var border in MessagesPanel.Children.OfType<Border>())
        {
            if (border.Tag is not int messageId || border.HorizontalAlignment != HorizontalAlignment.Right) continue;
            var readers = _messageReaders.TryGetValue(messageId, out var set) ? set : [];
            var seen = _currentRecipientIds.Count > 0 && _currentRecipientIds.All(readers.Contains);
            var state = seen ? "seen" : anyRecipientOnline ? "delivered" : "sent";
            if (FindReceiptText(border) is TextBlock receipt) { receipt.Text = state is "seen" or "delivered" ? "✓✓" : "✓"; receipt.Foreground = state == "seen" ? Brushes.DeepSkyBlue : Brushes.White; }
        }
    }

    private static TextBlock? FindReceiptText(Border border) => border.Child is StackPanel panel ? panel.Children.OfType<TextBlock>().FirstOrDefault(x => Equals(x.Tag, "receipt")) : null;
    private sealed class MessagesReadEvent { public int ChatId { get; set; } public string ReaderUserId { get; set; } = string.Empty; public List<int> MessageIds { get; set; } = []; }
    private sealed class ReadMessagesResponse { public int ChatId { get; set; } public List<int> MessageIds { get; set; } = []; }
    private sealed class GroupRecipientModel { public string UserId { get; set; } = string.Empty; }
}
