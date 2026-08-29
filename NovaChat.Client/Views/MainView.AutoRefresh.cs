using System.Windows;
using System.Windows.Threading;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private readonly DispatcherTimer _autoRefreshTimer = new()
    {
        Interval = TimeSpan.FromSeconds(5)
    };

    private bool _isAutoRefreshing;

    static MainView()
    {
        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAutoRefreshLoaded));

        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(OnAutoRefreshUnloaded));
    }

    private static void OnAutoRefreshLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view)
            view.StartAutoRefresh();
    }

    private static void OnAutoRefreshUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view)
            view.StopAutoRefresh();
    }

    private void StartAutoRefresh()
    {
        _autoRefreshTimer.Tick -= AutoRefreshTimer_Tick;
        _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
        _autoRefreshTimer.Start();
    }

    private void StopAutoRefresh()
    {
        _autoRefreshTimer.Stop();
        _autoRefreshTimer.Tick -= AutoRefreshTimer_Tick;
    }

    private async void AutoRefreshTimer_Tick(object? sender, EventArgs e)
    {
        if (_isAutoRefreshing || !AuthState.IsAuthenticated)
            return;

        _isAutoRefreshing = true;

        try
        {
            await LoadChatsAsync();
            await RefreshCurrentChatMessagesAsync();
            await RefreshCurrentUserPresenceAsync();
        }
        catch
        {
            // SignalR remains the primary real-time path.
            // Auto-refresh is a silent recovery/synchronization path.
        }
        finally
        {
            _isAutoRefreshing = false;
        }
    }

    private async Task RefreshCurrentChatMessagesAsync()
    {
        if (!_currentChatId.HasValue)
            return;

        var history = await _apiService.GetAsync<ChatHistoryResponse>(
            $"api/Chat/{_currentChatId.Value}/messages?pageSize={MessagePageSize}");

        if (history?.Messages == null || history.Messages.Count == 0)
            return;

        var newMessages = history.Messages
            .Where(message => !_loadedMessageIds.Contains(message.Id))
            .OrderBy(message => message.SentAt)
            .ThenBy(message => message.Id)
            .ToList();

        if (newMessages.Count == 0)
            return;

        await Dispatcher.InvokeAsync(() =>
        {
            foreach (var message in newMessages)
            {
                if (!_loadedMessageIds.Add(message.Id))
                    continue;

                AddMessageToUi(message);
                UpdateChatPreview(message);
            }

            _ = ScrollMessagesToBottomAsync();
        });
    }
}
