using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
            NoGroupsText.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
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
        if (sender is not FrameworkElement element || element.DataContext is not GroupModel group)
            return;

        await OpenGroupInMainViewAsync(group);
    }

    private async void GroupsButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new CreateGroupDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
            return;

        var group = await _apiService.PostAsync<CreateGroupRequest, GroupModel>(
            "api/Group",
            new CreateGroupRequest { Name = dialog.GroupName, Description = dialog.GroupDescription });

        if (group == null)
        {
            MessageBox.Show(Window.GetWindow(this), "Could not create the group.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await LoadGroupsIntoConversationsAsync();
        await OpenGroupInMainViewAsync(group);
    }

    private async Task OpenGroupInMainViewAsync(GroupModel group)
    {
        _currentGroupId = group.Id;
        _currentChatId = null;
        _currentOtherUserId = string.Empty;
        _loadedMessageIds.Clear();
        _oldestLoadedMessageId = null;
        _hasMoreMessages = false;

        ChatUserNameText.Text = group.Name;
        ChatStatusText.Text = string.IsNullOrWhiteSpace(group.Description) ? "Group conversation" : group.Description;
        ChatStatusIndicator.Fill = Brushes.DodgerBlue;
        ChatAvatarStatusDot.Visibility = Visibility.Collapsed;
        MessagesPanel.Children.Clear();
        LoadOlderMessagesButton.Visibility = Visibility.Collapsed;

        if (!_groupEventsAttached)
        {
            MessageTextBox.KeyDown += GroupMessageTextBox_KeyDown;
            if (MessageTextBox.Parent is Grid inputGrid)
            {
                var sendButton = inputGrid.Children.OfType<Button>().LastOrDefault();
                if (sendButton != null)
                    sendButton.Click += GroupSendButtonProxy;
            }
            _groupEventsAttached = true;
        }

        await LoadGroupMessagesAsync(group.Id);
    }

    private async Task LoadGroupMessagesAsync(int groupId)
    {
        try
        {
            var messages = await _apiService.GetAsync<List<GroupMessageModel>>($"api/Group/{groupId}/messages");
            MessagesPanel.Children.Clear();
            foreach (var message in messages ?? [])
                AddGroupMessageToUi(message);
            await ScrollMessagesToBottomAsync();
        }
        catch (Exception ex)
        {
            MessagesPanel.Children.Clear();
            MessagesPanel.Children.Add(new TextBlock
            {
                Text = $"Could not load group messages.\n{ex.Message}",
                Foreground = Brushes.Gray,
                Margin = new Thickness(12)
            });
        }
    }

    private async void GroupMessageTextBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_currentGroupId == null || e.Key != Key.Enter)
            return;

        e.Handled = true;
        await SendGroupMessageAsync();
    }

    private async void GroupSendButtonProxy(object? sender, RoutedEventArgs e)
    {
        if (_currentGroupId != null)
            await SendGroupMessageAsync();
    }

    private async Task SendGroupMessageAsync()
    {
        if (_currentGroupId == null || _hubConnection == null)
            return;

        var content = MessageTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        if (_hubConnection.State != Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
        {
            MessageBox.Show(Window.GetWindow(this), "Chat connection is not ready.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var groupId = _currentGroupId.Value;
        MessageTextBox.Clear();

        try
        {
            await _hubConnection.InvokeAsync("JoinGroup", groupId);
            await _hubConnection.InvokeAsync("SendGroupMessage", groupId, content);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Could not send message.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void AddGroupMessageToUi(GroupMessageModel message)
    {
        var isMine = string.Equals(message.SenderId, AuthState.UserId, StringComparison.OrdinalIgnoreCase);
        var bubble = new Border
        {
            Background = isMine ? (Brush)FindResource("PrimaryBrush") : (Brush)FindResource("PanelBackgroundBrush"),
            BorderBrush = isMine ? Brushes.Transparent : (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(isMine ? 0 : 1),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(13, 9, 13, 8),
            Margin = new Thickness(isMine ? 70 : 0, 4, isMine ? 0 : 70, 4),
            HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 560
        };

        var panel = new StackPanel();
        if (!isMine)
        {
            panel.Children.Add(new TextBlock
            {
                Text = message.SenderName,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("PrimaryBrush"),
                Margin = new Thickness(0, 0, 0, 3)
            });
        }

        panel.Children.Add(new TextBlock
        {
            Text = message.Content,
            TextWrapping = TextWrapping.Wrap,
            Foreground = isMine ? Brushes.White : (Brush)FindResource("TextBrush")
        });

        panel.Children.Add(new TextBlock
        {
            Text = message.SentAt.ToLocalTime().ToString("HH:mm"),
            FontSize = 10,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 4, 0, 0),
            Foreground = isMine ? Brushes.WhiteSmoke : (Brush)FindResource("SecondaryTextBrush")
        });

        bubble.Child = panel;
        MessagesPanel.Children.Add(bubble);
    }
}
