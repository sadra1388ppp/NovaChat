using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static readonly bool _messageEditRealtimeRegistered = RegisterMessageEditRealtime();

    private HubConnection? _messageEditAttachedConnection;
    private int _messageEditAttachAttempts;

    private static bool RegisterMessageEditRealtime()
    {
        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMessageEditRealtimeLoaded));
        return true;
    }

    private static void OnMessageEditRealtimeLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view)
        {
            view._messageEditAttachAttempts = 0;
            view.QueueMessageEditHandlerAttach();
        }
    }

    private void QueueMessageEditHandlerAttach()
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Loaded,
            new Action(AttachMessageEditHandlerWhenReady));
    }

    private async void AttachMessageEditHandlerWhenReady()
    {
        if (_hubConnection != null && !ReferenceEquals(_messageEditAttachedConnection, _hubConnection))
        {
            _hubConnection.On<MessageModel>("MessageEdited", OnRealtimeMessageEdited);
            _messageEditAttachedConnection = _hubConnection;
            return;
        }

        if (_hubConnection == null && _messageEditAttachAttempts++ < 50 && IsLoaded)
        {
            await Task.Delay(100);
            if (IsLoaded)
                QueueMessageEditHandlerAttach();
        }
    }

    private async void OnRealtimeMessageEdited(MessageModel message)
    {
        if (message.Id <= 0 || message.ChatId <= 0)
            return;

        await Dispatcher.InvokeAsync(() =>
        {
            if (_currentChatId != message.ChatId)
                return;

            var bubble = MessagesPanel.Children
                .OfType<Border>()
                .FirstOrDefault(x => x.Tag is int id && id == message.Id);

            if (bubble?.Child is StackPanel panel)
            {
                var text = panel.Children.OfType<TextBlock>().FirstOrDefault();
                if (text != null)
                    text.Text = message.Content;
            }
            else
            {
                _ = ReloadCurrentChatMessagesAfterEditAsync(message.ChatId);
            }

            UpdateChatPreview(message);
        }, DispatcherPriority.Normal);
    }

    private async Task ReloadCurrentChatMessagesAfterEditAsync(int chatId)
    {
        if (_currentChatId != chatId)
            return;

        MessagesPanel.Children.Clear();
        _loadedMessageIds.Clear();
        _oldestLoadedMessageId = null;
        _hasMoreMessages = false;
        UpdateLoadOlderButton();
        await LoadInitialMessagesAsync(chatId);
    }
}
