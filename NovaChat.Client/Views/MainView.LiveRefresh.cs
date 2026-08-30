using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private DispatcherTimer? _liveRefreshTimer;
    private bool _liveRefreshBusy;

    internal static void RegisterLiveRefresh()
    {
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(LiveRefreshLoaded));
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.UnloadedEvent, new RoutedEventHandler(LiveRefreshUnloaded));
    }

    private static void LiveRefreshLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view) view.StartLiveRefresh();
    }

    private static void LiveRefreshUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view) view.StopLiveRefresh();
    }

    private void StartLiveRefresh()
    {
        if (_liveRefreshTimer != null) return;

        _liveRefreshTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _liveRefreshTimer.Tick += LiveRefreshTimer_Tick;
        _liveRefreshTimer.Start();
        _ = LiveRefreshOnceAsync();
    }

    private void StopLiveRefresh()
    {
        if (_liveRefreshTimer == null) return;
        _liveRefreshTimer.Stop();
        _liveRefreshTimer.Tick -= LiveRefreshTimer_Tick;
        _liveRefreshTimer = null;
    }

    private async void LiveRefreshTimer_Tick(object? sender, EventArgs e)
    {
        await LiveRefreshOnceAsync();
    }

    private async Task LiveRefreshOnceAsync()
    {
        // Never compete with the explicit New Chat operation or a full chat reload.
        if (_liveRefreshBusy || _isCreatingChatSafely || !AuthState.IsAuthenticated || _hubConnection == null)
            return;

        _liveRefreshBusy = true;
        try
        {
            var onlineTask = _hubConnection.State == HubConnectionState.Connected
                ? _hubConnection.InvokeAsync<List<string>>("GetOnlineUsers")
                : Task.FromResult<List<string>?>(null);
            var chatsTask = _apiService.GetAsync<List<ChatModel>>("api/Chat");

            await Task.WhenAll(onlineTask, chatsTask);

            var onlineUsers = onlineTask.Status == TaskStatus.RanToCompletion ? await onlineTask : null;
            var serverChats = chatsTask.Status == TaskStatus.RanToCompletion ? await chatsTask : null;

            // A New Chat may have started while the HTTP requests above were in flight.
            // In that case, do not mutate the local list with a stale snapshot.
            if (_isCreatingChatSafely)
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (_isCreatingChatSafely) return;
                ApplyOnlineUsers(onlineUsers);
                ApplyChatSnapshot(serverChats);
                UpdateCurrentChatPresence();
            }, DispatcherPriority.Background);
        }
        catch
        {
        }
        finally
        {
            _liveRefreshBusy = false;
        }
    }

    private void ApplyOnlineUsers(IEnumerable<string>? ids)
    {
        if (ids == null) return;

        var next = ids.Where(x => !string.IsNullOrWhiteSpace(x))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (_onlineUserIds.SetEquals(next)) return;
        _onlineUserIds.Clear();
        foreach (var id in next) _onlineUserIds.Add(id);

        foreach (var item in _chats)
        {
            var online = IsUserOnline(item.Chat.OtherUserId(AuthState.UserId));
            if (item.IsOnline != online) item.IsOnline = online;
        }
    }

    private void ApplyChatSnapshot(IEnumerable<ChatModel>? serverChats)
    {
        if (serverChats == null || _isCreatingChatSafely) return;

        // The API must be the source of truth. A Chat ID can exist only once in the UI.
        var snapshot = serverChats
            .Where(x => x.Id > 0)
            .GroupBy(x => x.Id)
            .Select(g => g.First())
            .ToDictionary(x => x.Id);

        var changed = false;

        foreach (var serverChat in snapshot.Values)
        {
            if (_isCreatingChatSafely) return;

            var existing = _chats.FirstOrDefault(x => x.Chat.Id == serverChat.Id);
            if (existing == null)
            {
                var newItem = new ChatListItem
                {
                    Chat = serverChat,
                    DisplayName = serverChat.OtherUserName(AuthState.UserId),
                    LastMessage = serverChat.LastMessage == null ? "No messages yet." : FormatLastMessage(serverChat.LastMessage),
                    IsOnline = IsUserOnline(serverChat.OtherUserId(AuthState.UserId))
                };
                _chats.Add(newItem);
                _ = LoadAvatarForNewChatItemAsync(newItem);
                changed = true;
            }
        }

        // Defensive cleanup for any duplicates produced by an earlier race.
        foreach (var group in _chats.GroupBy(x => x.Chat.Id).ToList())
        {
            foreach (var duplicate in group.Skip(1))
            {
                _chats.Remove(duplicate);
                changed = true;
            }
        }

        foreach (var item in _chats.ToList())
        {
            if (!snapshot.TryGetValue(item.Chat.Id, out var serverChat))
            {
                _chats.Remove(item);
                if (_currentChatId == item.Chat.Id)
                    ClearCurrentChatUi();
                changed = true;
                continue;
            }

            var serverLastId = serverChat.LastMessage?.Id;
            var localLastId = item.Chat.LastMessage?.Id;
            if (serverLastId != localLastId)
            {
                item.Chat.LastMessage = serverChat.LastMessage;
                item.LastMessage = serverChat.LastMessage == null ? "No messages yet." : FormatLastMessage(serverChat.LastMessage);
                changed = true;
            }

            var serverName = serverChat.OtherUserName(AuthState.UserId);
            if (!string.Equals(item.DisplayName, serverName, StringComparison.Ordinal))
            {
                item.DisplayName = serverName;
                changed = true;
            }

            var online = IsUserOnline(serverChat.OtherUserId(AuthState.UserId));
            if (item.IsOnline != online) item.IsOnline = online;

            _ = RefreshAvatarOnlyWhenChangedAsync(item, serverChat);
        }

        if (changed)
            RefreshChatsList();
    }

    private async Task LoadAvatarForNewChatItemAsync(ChatListItem item)
    {
        var avatarUrl = item.Chat.OtherUserAvatarUrl;
        if (string.IsNullOrWhiteSpace(avatarUrl)) return;

        var absolute = _apiService.BuildAbsoluteUrl(avatarUrl);
        item.AvatarUri = absolute;
        try
        {
            var image = await LoadConversationAvatarAsync(absolute);
            await Dispatcher.InvokeAsync(() => item.AvatarSource = image);
            if (IsLoaded) RefreshChatsList();
        }
        catch { }
    }

    private async Task RefreshAvatarOnlyWhenChangedAsync(ChatListItem item, ChatModel serverChat)
    {
        var avatarUrl = string.Equals(serverChat.User1Id, AuthState.UserId, StringComparison.OrdinalIgnoreCase)
            ? serverChat.User2AvatarUrl
            : serverChat.User1AvatarUrl;

        var absolute = string.IsNullOrWhiteSpace(avatarUrl) ? null : _apiService.BuildAbsoluteUrl(avatarUrl);
        if (string.Equals(item.AvatarUri, absolute, StringComparison.OrdinalIgnoreCase)) return;

        item.Chat.User1AvatarUrl = serverChat.User1AvatarUrl;
        item.Chat.User2AvatarUrl = serverChat.User2AvatarUrl;
        item.AvatarUri = absolute;

        if (!string.IsNullOrWhiteSpace(absolute))
        {
            try
            {
                var image = await LoadConversationAvatarAsync(absolute);
                await Dispatcher.InvokeAsync(() => item.AvatarSource = image);
            }
            catch { }
        }
        else
        {
            item.AvatarSource = null;
        }
    }

    private void ClearCurrentChatUi()
    {
        if (!_currentChatId.HasValue) return;
        if (_chats.Any(x => x.Chat.Id == _currentChatId.Value)) return;

        _currentChatId = null;
        _currentOtherUserId = string.Empty;
        _loadedMessageIds.Clear();
        _oldestLoadedMessageId = null;
        _hasMoreMessages = false;
        ChatUserNameText.Text = "Select a chat";
        ChatStatusText.Text = "Offline";
        ChatStatusIndicator.Fill = System.Windows.Media.Brushes.Gray;
        ChatHeaderAvatarImage.Source = null;
        ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
        ChatAvatarInitialsText.Visibility = Visibility.Visible;
        MessagesPanel.Children.Clear();
        MessageTextBox.Clear();
        UpdateLoadOlderButton();
    }
}
