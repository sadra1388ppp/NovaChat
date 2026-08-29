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
        await EnsureGroupsLoadedAsync();
    }

    private async Task EnsureGroupsLoadedAsync()
    {
        if (_groupsLoaded || !AuthState.IsAuthenticated)
            return;

        await LoadGroupsIntoConversationsAsync();
    }

    private async Task LoadGroupsIntoConversationsAsync()
    {
        if (!AuthState.IsAuthenticated)
            return;

        try
        {
            var groups = await _apiService.GetAsync<List<GroupModel>>("api/Group");
            _groups = groups ?? [];
            GroupsList.ItemsSource = null;
            GroupsList.ItemsSource = _groups;
            NoGroupsText.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            _groupsLoaded = true;
        }
        catch
        {
            _groups = [];
            GroupsList.ItemsSource = null;
            GroupsList.ItemsSource = _groups;
            NoGroupsText.Visibility = Visibility.Visible;
            _groupsLoaded = false;
        }
    }

    private async void GroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not GroupModel group)
            return;

        // A group is a first-class conversation. Do not require a private chat
        // to be opened before a group can become the active conversation.
        _currentChatId = null;
        _currentOtherUserId = string.Empty;
        _currentGroupId = group.Id;

        await OpenGroupInMainViewAsync(group);
    }

    private async void GroupsButton_Click(object sender, RoutedEventArgs e)
    {
        var view = new GroupView
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        view.ShowDialog();
        _groupsLoaded = false;
        await LoadGroupsIntoConversationsAsync();
    }

    private async Task OpenGroupInMainViewAsync(GroupModel group)
    {
        // Set group state before any asynchronous operation so the send button
        // is immediately routed to the group even when no private chat was opened.
        _currentChatId = null;
        _currentOtherUserId = string.Empty;
        _currentGroupId = group.Id;

        ChatUserNameText.Text = group.Name;
        ChatStatusText.Text = string.IsNullOrWhiteSpace(group.Description) ? "Group" : group.Description;
        ChatStatusIndicator.Fill = Brushes.Gray;
        MessagesPanel.Children.Clear();
        MessageTextBox.Clear();

        try
        {
            await EnsureGroupHubAsync();

            var messages = await _apiService.GetAsync<List<GroupMessageModel>>($"api/Group/{group.Id}/messages");

            foreach (var message in messages ?? [])
                AddGroupMessageToUi(message);

            if (_hubConnection != null && _hubConnection.State == HubConnectionState.Connected)
                await _hubConnection.InvokeAsync("JoinGroup", group.Id);

            ChatStatusText.Text = string.IsNullOrWhiteSpace(group.Description) ? "Group" : group.Description;
        }
        catch (Exception ex)
        {
            MessageBox.Show(Window.GetWindow(this), $"Could not open group.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task EnsureGroupHubAsync()
    {
        if (_hubConnection == null || _hubConnection.State == HubConnectionState.Disconnected)
            return;

        if (_groupEventsAttached)
            return;

        _hubConnection.On<GroupMessageModel>("ReceiveGroupMessage", message => Dispatcher.Invoke(() =>
        {
            if (_currentGroupId == message.GroupId)
                AddGroupMessageToUi(message);
        }));

        _hubConnection.On<GroupMessageModel>("GroupMessageDeleted", message => Dispatcher.Invoke(() =>
        {
            if (_currentGroupId != message.GroupId)
                return;

            if (message.DeletedForMe)
            {
                RemoveGroupMessageFromUi(message.Id);
                return;
            }

            ReplaceGroupMessageWithDeletedState(message);
        }));

        _groupEventsAttached = true;
    }

    private void AddGroupMessageToUi(GroupMessageModel message)
    {
        if (message.DeletedForMe)
            return;

        var isMine = string.Equals(message.SenderId, AuthState.UserId, StringComparison.OrdinalIgnoreCase);

        var bubble = new Border
        {
            Tag = message.Id,
            Background = isMine ? new SolidColorBrush(Color.FromRgb(225, 245, 254)) : new SolidColorBrush(Color.FromRgb(245, 247, 250)),
            CornerRadius = new CornerRadius(14),
            Padding = new Thickness(12, 8, 12, 8),
            Margin = isMine ? new Thickness(70, 4, 0, 4) : new Thickness(0, 4, 70, 4),
            HorizontalAlignment = isMine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
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
            Text = message.DeletedForEveryone ? "This message was deleted." : message.Content,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            FontStyle = message.DeletedForEveryone ? FontStyles.Italic : FontStyles.Normal,
            Foreground = message.DeletedForEveryone ? Brushes.Gray : Brushes.Black
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

        if (!message.DeletedForEveryone)
        {
            var menu = new ContextMenu();
            var deleteForMe = new MenuItem { Header = "Delete for me" };
            deleteForMe.Click += async (_, _) => await DeleteGroupMessageAsync(message.Id, false);
            menu.Items.Add(deleteForMe);

            if (isMine)
            {
                var deleteForEveryone = new MenuItem { Header = "Delete for everyone" };
                deleteForEveryone.Click += async (_, _) => await DeleteGroupMessageAsync(message.Id, true);
                menu.Items.Add(deleteForEveryone);
            }

            bubble.ContextMenu = menu;
        }

        MessagesPanel.Children.Add(bubble);
        MessagesScrollViewer.ScrollToEnd();
    }

    private async Task DeleteGroupMessageAsync(int messageId, bool forEveryone)
    {
        if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
        {
            MessageBox.Show("Real-time connection is not available.", "Delete Message", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (forEveryone)
        {
            var confirm = MessageBox.Show("Delete this message for everyone?", "Delete Message", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes)
                return;
        }

        try
        {
            await _hubConnection.InvokeAsync("DeleteGroupMessage", messageId, forEveryone);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Message could not be deleted.\n\n{ex.Message}", "Delete Message", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RemoveGroupMessageFromUi(int messageId)
    {
        var bubble = MessagesPanel.Children.OfType<Border>().FirstOrDefault(x => x.Tag is int id && id == messageId);
        if (bubble != null)
            MessagesPanel.Children.Remove(bubble);
    }

    private void ReplaceGroupMessageWithDeletedState(GroupMessageModel message)
    {
        var bubble = MessagesPanel.Children.OfType<Border>().FirstOrDefault(x => x.Tag is int id && id == message.Id);
        if (bubble == null)
            return;

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = "This message was deleted.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.Gray
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
        bubble.ContextMenu = null;
    }
}
