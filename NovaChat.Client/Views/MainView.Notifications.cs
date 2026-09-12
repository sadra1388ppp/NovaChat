using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Threading;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private DispatcherTimer? _notificationHookTimer;
    private HubConnection? _notificationConnection;
    private IDisposable? _notificationHandler;

    static MainView()
    {
        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(NotificationHookLoaded));

        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(NotificationHookUnloaded));
    }

    private static void NotificationHookLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view)
            view.StartNotificationHook();
    }

    private static void NotificationHookUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view)
            view.StopNotificationHook();
    }

    private void StartNotificationHook()
    {
        if (_notificationHookTimer != null)
            return;

        _notificationHookTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _notificationHookTimer.Tick += NotificationHookTimer_Tick;
        _notificationHookTimer.Start();
        TryAttachNotificationHandler();
    }

    private void StopNotificationHook()
    {
        if (_notificationHookTimer != null)
        {
            _notificationHookTimer.Stop();
            _notificationHookTimer.Tick -= NotificationHookTimer_Tick;
            _notificationHookTimer = null;
        }

        DetachNotificationHandler();
    }

    private void NotificationHookTimer_Tick(object? sender, EventArgs e)
    {
        TryAttachNotificationHandler();
    }

    private void TryAttachNotificationHandler()
    {
        if (_hubConnection == null || _hubConnection.State == HubConnectionState.Disconnected)
        {
            if (_notificationConnection != null)
                DetachNotificationHandler();
            return;
        }

        if (ReferenceEquals(_notificationConnection, _hubConnection))
            return;

        DetachNotificationHandler();
        _notificationConnection = _hubConnection;
        _notificationHandler = _notificationConnection.On<MessageModel>("ReceiveMessage", OnNotificationMessageReceived);
    }

    private void DetachNotificationHandler()
    {
        try
        {
            _notificationHandler?.Dispose();
        }
        catch
        {
            // Notification cleanup must never affect the chat connection.
        }
        finally
        {
            _notificationHandler = null;
            _notificationConnection = null;
        }
    }

    private void OnNotificationMessageReceived(MessageModel message)
    {
        if (message == null || message.Id <= 0 || message.ChatId <= 0)
            return;

        // Never notify for messages sent by the current user.
        if (string.Equals(message.SenderId, AuthState.Username, StringComparison.OrdinalIgnoreCase))
            return;

        // If this exact conversation is currently open, the message is already visible.
        if (_currentChatId == message.ChatId)
            return;

        _ = Dispatcher.InvokeAsync(() =>
        {
            if (_currentChatId == message.ChatId)
                return;

            var chat = _chats.FirstOrDefault(x => x.Chat.Id == message.ChatId);
            var isGroup = chat?.Chat.IsGroup == true;

            var title = isGroup
                ? (string.IsNullOrWhiteSpace(chat?.DisplayName) ? "NovaChat" : chat!.DisplayName)
                : (string.IsNullOrWhiteSpace(message.SenderName) ? "New message" : message.SenderName);

            var body = isGroup && !string.IsNullOrWhiteSpace(message.SenderName)
                ? $"{message.SenderName}: {BuildNotificationPreview(message.Content)}"
                : BuildNotificationPreview(message.Content);

            NotificationService.ShowMessageNotification(title, body);
        });
    }

    private static string BuildNotificationPreview(string? content)
    {
        var text = string.IsNullOrWhiteSpace(content) ? "New message" : content.Trim();
        const int maxLength = 140;
        return text.Length <= maxLength ? text : text[..(maxLength - 1)] + "…";
    }
}
