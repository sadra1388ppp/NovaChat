using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private sealed class SendGroupMessageRequest
    {
        public string Content { get; set; } = string.Empty;
    }

    private void GroupAwareNewChatButton_Click(object sender, RoutedEventArgs e)
    {
        ClearGroupSelection();
        NewChatButton_Click(sender, e);
    }

    private void GroupAwareChatButton_Click(object sender, RoutedEventArgs e)
    {
        ClearGroupSelection();
        ChatButton_Click(sender, e);
    }

    private async void GroupAwareSendButton_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGroupId.HasValue)
        {
            await SendGroupMessageAsync();
            return;
        }

        SendButton_Click(sender, e);
    }

    private async void GroupAwareMessageTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;

        if (_currentGroupId.HasValue)
        {
            await SendGroupMessageAsync();
            return;
        }

        MessageTextBox_KeyDown(sender, e);
    }

    private async Task SendGroupMessageAsync()
    {
        if (!_currentGroupId.HasValue)
            return;

        var content = MessageTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return;

        if (content.Length > 4000)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                "Message cannot be longer than 4000 characters.",
                "NovaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var groupId = _currentGroupId.Value;

        try
        {
            var message = await _apiService.PostAsync<SendGroupMessageRequest, GroupMessageModel>(
                $"api/Group/{groupId}/messages",
                new SendGroupMessageRequest { Content = content });

            if (message == null)
                return;

            MessageTextBox.Clear();
            AddGroupMessageToUi(message);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"Could not send group message.\n\n{ex.Message}",
                "NovaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ClearGroupSelection()
    {
        _currentGroupId = null;
        _currentOtherUserId = string.Empty;
    }
}
