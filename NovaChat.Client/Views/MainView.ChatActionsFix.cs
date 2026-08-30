using NovaChat.Client.Models;
using System.Windows;
using System.Windows.Controls;
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

        var box = new TextBox
        {
            Margin = new Thickness(20),
            Height = 40,
            Padding = new Thickness(10)
        };

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
        panel.Children.Add(new TextBlock
        {
            Text = "Enter User ID",
            Margin = new Thickness(20, 20, 20, 0),
            Foreground = (Brush)FindResource("TextBrush")
        });
        panel.Children.Add(box);
        panel.Children.Add(button);
        dialog.Content = panel;

        string? userId = null;

        button.Click += (_, _) =>
        {
            userId = box.Text.Trim();
            if (!string.IsNullOrWhiteSpace(userId))
                dialog.DialogResult = true;
        };

        box.KeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.Enter)
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        };

        dialog.Loaded += (_, _) => box.Focus();
        dialog.ShowDialog();

        if (string.IsNullOrWhiteSpace(userId) ||
            string.Equals(userId, AuthState.UserId, StringComparison.OrdinalIgnoreCase))
            return;

        _isCreatingChatSafely = true;
        try
        {
            var result = await _apiService.PostAsync<CreateChatRequest, CreateChatResponse>(
                "api/Chat",
                new CreateChatRequest { UserId = userId });

            if (result?.Chat == null)
            {
                MessageBox.Show(
                    "User not found or chat could not be created.",
                    "New Chat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var existingItem = _chats.FirstOrDefault(x => x.Chat.Id == result.Chat.Id);
            if (existingItem == null)
            {
                await LoadChatsAsync();
                existingItem = _chats.FirstOrDefault(x => x.Chat.Id == result.Chat.Id);
            }

            if (existingItem != null)
                await OpenChatAsync(existingItem.Chat);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not create chat.\n\n{ex.Message}",
                "New Chat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isCreatingChatSafely = false;
        }
    }

    private async void DeleteSingleChatButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ChatListItem item })
            return;

        var chatId = item.Chat.Id;
        if (chatId <= 0)
            return;

        if (MessageBox.Show(
                $"Delete chat with {item.DisplayName}?\n\nAll messages in this chat will also be deleted.",
                "Delete Chat",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;

        try
        {
            var deleted = await _apiService.DeleteAsync($"api/Chat/{chatId}");
            if (!deleted)
            {
                MessageBox.Show(
                    "The selected chat could not be deleted.",
                    "Delete Chat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // Remove only the exact selected item locally. The SignalR event may
            // arrive as well, but it is harmless because the item is already gone.
            var localItem = _chats.FirstOrDefault(x => x.Chat.Id == chatId);
            if (localItem != null)
                _chats.Remove(localItem);

            if (_currentChatId == chatId)
            {
                if (_hubConnection?.State == Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
                {
                    try { await _hubConnection.InvokeAsync("LeaveChat", chatId); }
                    catch { }
                }

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
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not delete chat.\n\n{ex.Message}",
                "Delete Chat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
