using Microsoft.AspNetCore.SignalR.Client;
using System.Windows;
using System.Windows.Input;

namespace NovaChat.Client.Views;

public partial class MainView
{
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
            if (_hubConnection != null &&
                _hubConnection.State == HubConnectionState.Connected)
            {
                await _hubConnection.InvokeAsync(
                    "SendGroupMessage",
                    groupId,
                    content);

                MessageTextBox.Clear();
                return;
            }

            MessageBox.Show(
                Window.GetWindow(this),
                "The real-time connection is not available. Please wait a moment and try again.",
                "NovaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
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
