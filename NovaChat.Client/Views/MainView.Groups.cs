using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private List<GroupModel> _groups = [];
    private bool _groupsLoaded;
    private int? _currentGroupId;
    private bool _groupEventsAttached;

    private async void GroupsList_Loaded(object sender, RoutedEventArgs e)
    {
        if (_groupsLoaded || !AuthState.IsAuthenticated)
            return;

        _groupsLoaded = true;
        await LoadGroupsIntoConversationsAsync();
    }

    private async Task LoadGroupsIntoConversationsAsync()
    {
        try
        {
            var groups = await _apiService.GetAsync<List<GroupModel>>("api/Group");
            _groups = groups ?? [];
            GroupsList.ItemsSource = _groups;
            NoGroupsText.Visibility = _groups.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch
        {
            _groups = [];
            GroupsList.ItemsSource = _groups;
            NoGroupsText.Visibility = Visibility.Visible;
        }
    }

    private async void GroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is GroupModel group)
        {
            _currentChatId = null;
            _currentOtherUserId = string.Empty;
            await OpenGroupInMainViewAsync(group);
        }
    }

    private async void GroupsButton_Click(object sender, RoutedEventArgs e)
    {
        var view = new GroupView
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        view.ShowDialog();
        await LoadGroupsIntoConversationsAsync();
    }

    private async Task OpenGroupInMainViewAsync(GroupModel group)
    {
        _currentGroupId = group.Id;

        ChatUserNameText.Text = group.Name;
        ChatStatusText.Text = string.IsNullOrWhiteSpace(group.Description)
            ? "Group"
            : group.Description;

        MessagesPanel.Children.Clear();

        try
        {
            var messages = await _apiService.GetAsync<List<GroupMessageModel>>(
                $"api/Group/{group.Id}/messages");

            foreach (var message in messages ?? [])
                AddGroupMessageToUi(message);

            await EnsureGroupHubAsync();

            if (_hubConnection != null &&
                _hubConnection.State == HubConnectionState.Connected)
            {
                await _hubConnection.InvokeAsync("JoinGroup", group.Id);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"Could not open group.\n\n{ex.Message}",
                "NovaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task EnsureGroupHubAsync()
    {
        if (_hubConnection == null ||
            _hubConnection.State == HubConnectionState.Disconnected)
        {
            return;
        }

        if (_groupEventsAttached)
            return;

        _hubConnection.On<GroupMessageModel>(
            "ReceiveGroupMessage",
            message => Dispatcher.Invoke(() =>
            {
                if (_currentGroupId == message.GroupId)
                    AddGroupMessageToUi(message);
            }));

        _groupEventsAttached = true;
    }

    private void AddGroupMessageToUi(GroupMessageModel message)
    {
        var isMine = string.Equals(
            message.SenderId,
            AuthState.UserId,
            StringComparison.OrdinalIgnoreCase);

        var bubble = new Border
        {
            Background = isMine
                ? new SolidColorBrush(Color.FromRgb(225, 245, 254))
                : new SolidColorBrush(Color.FromRgb(245, 247, 250)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = isMine
                ? new Thickness(70, 4, 0, 4)
                : new Thickness(0, 4, 70, 4),
            HorizontalAlignment = isMine
                ? HorizontalAlignment.Right
                : HorizontalAlignment.Left,
            MaxWidth = 520
        };

        var panel = new StackPanel();

        if (!isMine)
        {
            panel.Children.Add(new TextBlock
            {
                Text = message.SenderName,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 3)
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = message.Content,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14
        });

        panel.Children.Add(new TextBlock
        {
            Text = message.SentAt.ToLocalTime().ToString("HH:mm"),
            HorizontalAlignment = HorizontalAlignment.Right,
            FontSize = 10,
            Foreground = Brushes.Gray,
            Margin = new Thickness(0, 4, 0, 0)
        });

        bubble.Child = panel;
        MessagesPanel.Children.Add(bubble);
        MessagesScrollViewer.ScrollToEnd();
    }
}
