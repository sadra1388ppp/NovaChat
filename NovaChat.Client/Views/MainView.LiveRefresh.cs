using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
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
        if (_liveRefreshBusy || _isCreatingChatSafely || !AuthState.IsAuthenticated)
            return;

        _liveRefreshBusy = true;
        try
        {
            Task<List<string>?>? onlineTask = null;
            if (_hubConnection?.State == HubConnectionState.Connected)
                onlineTask = _hubConnection.InvokeAsync<List<string>>("GetOnlineUsers");

            var chatsTask = _apiService.GetAsync<List<ChatModel>>("api/Chat");

            if (onlineTask != null)
                await Task.WhenAll(onlineTask, chatsTask);
            else
                await chatsTask;

            var onlineUsers = onlineTask?.Status == TaskStatus.RanToCompletion
                ? await onlineTask
                : null;
            var serverChats = chatsTask.Status == TaskStatus.RanToCompletion
                ? await chatsTask
                : null;

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

        if (_onlineUserIds.SetEquals(next))
        {
            foreach (var item in _chats)
            {
                var online = IsUserOnline(item.Chat.OtherUserId(AuthState.UserId));
                if (item.IsOnline != online) item.IsOnline = online;
            }
            return;
        }

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

        var snapshot = serverChats
            .Where(x => x.Id > 0)
            .GroupBy(x => GetConversationKey(x, AuthState.UserId), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(x => x.Id).First())
            .ToDictionary(x => GetConversationKey(x, AuthState.UserId), StringComparer.OrdinalIgnoreCase);

        var changed = false;

        foreach (var serverChat in snapshot.Values)
        {
            var key = GetConversationKey(serverChat, AuthState.UserId);
            var existing = _chats.FirstOrDefault(x =>
                string.Equals(GetConversationKey(x.Chat, AuthState.UserId), key, StringComparison.OrdinalIgnoreCase));

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
                continue;
            }

            var serverLastId = serverChat.LastMessage?.Id;
            var localLastId = existing.Chat.LastMessage?.Id;
            if (serverLastId != localLastId)
            {
                existing.Chat.LastMessage = serverChat.LastMessage;
                existing.LastMessage = serverChat.LastMessage == null ? "No messages yet." : FormatLastMessage(serverChat.LastMessage);
                changed = true;
            }

            var serverName = serverChat.OtherUserName(AuthState.UserId);
            if (!string.Equals(existing.DisplayName, serverName, StringComparison.Ordinal))
            {
                existing.DisplayName = serverName;
                changed = true;
            }

            existing.Chat.User1AvatarUrl = serverChat.User1AvatarUrl;
            existing.Chat.User2AvatarUrl = serverChat.User2AvatarUrl;

            var online = IsUserOnline(serverChat.OtherUserId(AuthState.UserId));
            if (existing.IsOnline != online)
            {
                existing.IsOnline = online;
                changed = true;
            }

            _ = RefreshAvatarOnlyWhenChangedAsync(existing, serverChat);
        }

        foreach (var duplicate in _chats
            .GroupBy(x => GetConversationKey(x.Chat, AuthState.UserId), StringComparer.OrdinalIgnoreCase)
            .SelectMany(g => g.OrderBy(x => x.Chat.Id).Skip(1))
            .ToList())
        {
            _chats.Remove(duplicate);
            if (_currentChatId == duplicate.Chat.Id)
                ClearCurrentChatUi();
            changed = true;
        }

        foreach (var item in _chats.ToList())
        {
            var key = GetConversationKey(item.Chat, AuthState.UserId);
            if (!snapshot.ContainsKey(key))
            {
                _chats.Remove(item);
                if (_currentChatId == item.Chat.Id)
                    ClearCurrentChatUi();
                changed = true;
            }
        }

        if (changed)
            RefreshChatsList();
    }

    private static string GetConversationKey(ChatModel chat, string currentUserId)
    {
        var otherUserId = chat.OtherUserId(currentUserId);
        if (string.IsNullOrWhiteSpace(otherUserId))
            return $"chat:{chat.Id}";

        return $"user:{otherUserId.Trim().ToUpperInvariant()}";
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
