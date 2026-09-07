using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private bool _isCreatingChatSafely;

    private async void StartNewChatSafelyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isCreatingChatSafely) return;

        var dialog = new Window
        {
            Title = "New Chat",
            Width = 400,
            Height = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("PanelBackgroundBrush")
        };

        var box = new TextBox { Margin = new Thickness(20), Height = 40, Padding = new Thickness(10) };
        var button = new Button
        {
            Content = "Start Chat",
            Width = 100,
            Height = 35,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(20),
            Background = (Brush)FindResource("PrimaryBrush"),
            Foreground = Brushes.White
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Enter Username", Margin = new Thickness(20, 20, 20, 0), Foreground = (Brush)FindResource("TextBrush") });
        panel.Children.Add(box);
        panel.Children.Add(button);
        dialog.Content = panel;

        string? username = null;
        button.Click += (_, _) => { username = box.Text.Trim(); if (!string.IsNullOrWhiteSpace(username)) dialog.DialogResult = true; };
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        dialog.Loaded += (_, _) => box.Focus();
        dialog.ShowDialog();

        if (string.IsNullOrWhiteSpace(username) || string.Equals(username, AuthState.Username, StringComparison.OrdinalIgnoreCase)) return;

        _isCreatingChatSafely = true;
        try
        {
            var result = await _apiService.PostAsync<CreateChatRequest, CreateChatResponse>("api/Chat", new CreateChatRequest { Username = username });
            if (result?.Chat == null)
            {
                MessageBox.Show("Username not found or chat could not be created.", "New Chat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var item = _chats.FirstOrDefault(x => x.Chat.Id == result.Chat.Id);
            if (item == null)
            {
                await LoadChatsAsync();
                item = _chats.FirstOrDefault(x => x.Chat.Id == result.Chat.Id);
            }

            if (item != null) await OpenChatAsync(item.Chat);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create chat.\n\n{ex.Message}", "New Chat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally { _isCreatingChatSafely = false; }
    }

    private async void DeleteSingleChatButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ChatListItem item }) return;
        var chatId = item.Chat.Id;
        if (chatId <= 0) return;

        if (item.Chat.IsGroup)
        {
            await HandleGroupConversationMenuAsync(item);
            return;
        }

        if (MessageBox.Show(
                $"Remove chat with {item.DisplayName} from your chat list?\n\nThe other person will keep the conversation and its messages. You can start the chat again later.",
                "Remove Chat",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        try
        {
            if (!await _apiService.DeleteAsync($"api/Conversation/{chatId}"))
            {
                MessageBox.Show("The selected chat could not be removed.", "Remove Chat", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await RemoveChatFromLocalUiAsync(chatId);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not remove the chat.\n\n{ex.Message}", "Remove Chat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task HandleGroupConversationMenuAsync(ChatListItem item)
    {
        try
        {
            var members = await _apiService.GetAsync<List<GroupMemberMenuModel>>($"api/Chat/{item.Chat.Id}/members") ?? [];
            var currentMember = members.FirstOrDefault(m => string.Equals(m.UserId, AuthState.UserId, StringComparison.OrdinalIgnoreCase));
            if (currentMember == null)
            {
                MessageBox.Show("You are no longer a member of this group.", "Group", MessageBoxButton.OK, MessageBoxImage.Information);
                await RemoveChatFromLocalUiAsync(item.Chat.Id);
                return;
            }

            var isOwner = string.Equals(currentMember.Role, "Owner", StringComparison.OrdinalIgnoreCase);
            var groupName = string.IsNullOrWhiteSpace(item.Chat.Name) ? "this group" : $"'{item.Chat.Name}'";

            if (isOwner)
            {
                var result = MessageBox.Show(
                    $"Do you want to delete {groupName}?\n\nYes = permanently delete the group for everyone.\nNo = keep the group.",
                    "Delete Group",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (result != MessageBoxResult.Yes) return;

                if (!await _apiService.DeleteAsync($"api/GroupManagement/{item.Chat.Id}"))
                {
                    MessageBox.Show("The group could not be deleted.", "Delete Group", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                await RemoveChatFromLocalUiAsync(item.Chat.Id);
                return;
            }

            var leaveResult = MessageBox.Show(
                $"Do you want to leave {groupName}?\n\nYes = leave the group and remove it from your conversations.\nNo = keep the group.",
                "Leave Group",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question,
                MessageBoxResult.No);

            if (leaveResult != MessageBoxResult.Yes) return;

            if (!await _apiService.PostAsync<object, object>($"api/GroupManagement/{item.Chat.Id}/leave", new { }))
            {
                MessageBox.Show("The group could not be left.", "Leave Group", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await RemoveChatFromLocalUiAsync(item.Chat.Id);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not process the group action.\n\n{ex.Message}", "Group", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class GroupMemberMenuModel
    {
        public string UserId { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }

    private async Task RemoveChatFromLocalUiAsync(int chatId)
    {
        _chats.RemoveAll(x => x.Chat.Id == chatId);
        if (_currentChatId == chatId)
        {
            if (_hubConnection?.State == HubConnectionState.Connected)
                try { await _hubConnection.InvokeAsync("LeaveChat", chatId); } catch { }

            _currentChatId = null;
            _currentOtherUserId = string.Empty;
            _loadedMessageIds.Clear();
            _oldestLoadedMessageId = null;
            _hasMoreMessages = false;
            ChatUserNameText.Text = "Select a chat";
            ChatStatusText.Text = "Offline";
            ChatStatusIndicator.Fill = Brushes.Gray;
            ChatHeaderAvatarImage.Source = null;
            ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
            ChatAvatarInitialsText.Text = "N";
            ChatAvatarInitialsText.Visibility = Visibility.Visible;
            MessagesPanel.Children.Clear();
            MessageTextBox.Clear();
            UpdateLoadOlderButton();
        }
        RefreshChatsList();
    }

    private async void ChatHeaderProfile_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (string.IsNullOrWhiteSpace(_currentOtherUserId)) return;

        try
        {
            var profile = await _apiService.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(_currentOtherUserId.Trim())}");
            if (profile == null)
            {
                MessageBox.Show("This user's profile could not be loaded.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ShowPublicProfile(profile);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load this profile.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
