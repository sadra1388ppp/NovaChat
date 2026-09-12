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
    private readonly HashSet<int> _seenMessageIds = [];
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
        await view.MarkCurrentChatAsReadAsync();
        view.UpdateMessageReceiptsUi();
    }

    private void RegisterReadReceiptHandlers()
    {
        if (_readReceiptHandlersRegistered || _hubConnection == null) return;
        _readReceiptHandlersRegistered = true;
        _hubConnection.On<MessagesReadEvent>("MessagesRead", OnMessagesRead);
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
            // Persist the currently open conversation as read BEFORE asking the
            // server for unread counts. This makes the read state authoritative
            // on the server instead of relying only on the local badge state.
            if (_currentChatId.HasValue)
                await MarkCurrentChatAsReadAsync();

            var counts = await _apiService.GetAsync<Dictionary<string, int>>("api/message-read/unread") ?? [];
            await Dispatcher.InvokeAsync(() =>
            {
                var currentChatId = _currentChatId;

                foreach (var item in _chats)
                {
                    // The conversation currently visible on screen is always read.
                    // Never restore its badge from the server's unread snapshot.
                    if (currentChatId == item.Chat.Id)
                    {
                        item.UnreadCount = 0;
                        continue;
                    }

                    item.UnreadCount = counts.TryGetValue(item.Chat.Id.ToString(), out var count) ? count : 0;
                }
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
            var item = _chats.FirstOrDefault(x => x.Chat.Id == chatId);
            if (item != null) item.UnreadCount = 0;

            // Persist the read state immediately. MessageReads uses a primary key
            // per message/user, so repeating this operation is safe and idempotent.
            if (_hubConnection?.State == HubConnectionState.Connected)
                await _hubConnection.InvokeAsync("MarkChatAsRead", chatId);
            else
                await _apiService.PostAsync<object, ReadMessagesResponse>($"api/message-read/{chatId}/read", new { });
        }
        catch { }
    }

    private void OnMessagesRead(MessagesReadEvent evt)
    {
        if (evt == null || evt.MessageIds == null || evt.MessageIds.Count == 0) return;

        Dispatcher.InvokeAsync(() =>
        {
            foreach (var messageId in evt.MessageIds)
                _seenMessageIds.Add(messageId);

            UpdateMessageReceiptsUi();
        });
    }

    private void UpdateMessageReceiptsUi()
    {
        foreach (var border in MessagesPanel.Children.OfType<Border>())
        {
            if (border.Tag is not int messageId || border.HorizontalAlignment != HorizontalAlignment.Right)
                continue;

            if (FindReceiptText(border) is TextBlock receipt)
            {
                var seen = _seenMessageIds.Contains(messageId);
                receipt.Text = seen ? "✓✓" : "✓";
                receipt.Foreground = seen
                    ? new SolidColorBrush(Color.FromRgb(116, 203, 255))
                    : Brushes.White;
            }
        }
    }

    private static TextBlock? FindReceiptText(Border border) =>
        border.Child is StackPanel panel
            ? panel.Children.OfType<TextBlock>().FirstOrDefault(x => Equals(x.Tag, "receipt"))
            : null;

    private sealed class MessagesReadEvent
    {
        public int ChatId { get; set; }
        public string ReaderUserId { get; set; } = string.Empty;
        public List<int> MessageIds { get; set; } = [];
    }

    private sealed class ReadMessagesResponse
    {
        public int ChatId { get; set; }
        public List<int> MessageIds { get; set; } = [];
    }
}
