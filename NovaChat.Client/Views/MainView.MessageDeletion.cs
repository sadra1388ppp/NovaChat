using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static void OnMessageBubbleRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border clickedBorder) return;
        var mainView = FindAncestor<MainView>(clickedBorder);
        if (mainView == null || mainView._currentChatId == null) return;
        var messageBorder = FindMessageRootBorder(clickedBorder, mainView.MessagesPanel);
        if (messageBorder == null) return;
        var messageId = messageBorder.Tag is int id ? id : (int?)null;
        if (!messageId.HasValue) return;

        var menu = new ContextMenu();
        var copyItem = new MenuItem { Header = "Copy message" };
        copyItem.Tag = new MessageBubbleInfo(messageBorder, messageId.Value);
        copyItem.Click += mainView.CopyMessageMenuItem_Click;
        menu.Items.Add(copyItem);

        var separator = new Separator();
        menu.Items.Add(separator);
        var deleteItem = new MenuItem { Header = "Delete message" };
        deleteItem.Tag = new MessageBubbleInfo(messageBorder, messageId.Value);
        deleteItem.Click += mainView.DeleteMessageMenuItem_Click;
        menu.Items.Add(deleteItem);

        messageBorder.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private static Border? FindMessageRootBorder(DependencyObject element, DependencyObject messagePanel)
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is Border border && (ReferenceEquals(border.Parent, messagePanel) || messagePanel is Panel panel && panel.Children.Contains(border))) return border;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private async void CopyMessageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not MessageBubbleInfo info) return;
        var message = await GetMessageByIdAsync(_currentChatId ?? 0, info.MessageId);
        if (message == null || string.IsNullOrWhiteSpace(message.Content)) return;
        try
        {
            Clipboard.SetText(message.Content);
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not copy the message.\n\n{ex.Message}", "Copy Message", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteMessageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not MessageBubbleInfo info || _currentChatId == null) return;
        var history = await GetMessageByIdAsync(_currentChatId.Value, info.MessageId);
        if (history == null) { MessageBox.Show("The message could not be located.", "Delete Message", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var isMine = string.Equals(history.SenderId, AuthState.Username, StringComparison.OrdinalIgnoreCase);
        var mode = "me";
        if (isMine)
        {
            var result = MessageBox.Show("Delete this message for everyone?\n\nChoose No to delete it only for yourself.", "Delete Message", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel) return;
            mode = result == MessageBoxResult.Yes ? "everyone" : "me";
        }
        else if (MessageBox.Show("Delete this message for yourself?", "Delete Message", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

        try
        {
            var deleted = await DeleteMessageAsync(history.Id, mode);
            if (!deleted) { MessageBox.Show("The message could not be deleted.", "Delete Message", MessageBoxButton.OK, MessageBoxImage.Error); return; }
            await Dispatcher.InvokeAsync(() => RemoveMessageBubbleCompletely(info.Border, MessagesPanel));
            _loadedMessageIds.Remove(history.Id);
            await LoadChatsAsync();
        }
        catch (Exception ex) { MessageBox.Show($"Could not delete message.\n\n{ex.Message}", "Delete Message", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static void RemoveMessageBubbleCompletely(Border clickedOrRoot, Panel messagesPanel)
    {
        var root = FindMessageRootBorder(clickedOrRoot, messagesPanel) ?? clickedOrRoot;
        if (messagesPanel.Children.Contains(root)) { messagesPanel.Children.Remove(root); return; }
        root.Visibility = Visibility.Collapsed; root.Height = 0; root.MinHeight = 0; root.MaxHeight = 0; root.Margin = new Thickness(0); root.Padding = new Thickness(0); root.Child = null;
    }

    private async Task<MessageModel?> GetMessageByIdAsync(int chatId, int messageId)
    {
        int? beforeId = null;
        for (var page = 0; page < 20; page++)
        {
            var endpoint = $"api/Chat/{chatId}/messages?pageSize=100";
            if (beforeId.HasValue) endpoint += $"&beforeMessageId={beforeId.Value}";
            var response = await _apiService.GetAsync<ChatHistoryResponse>(endpoint);
            if (response == null) return null;
            var match = response.Messages.FirstOrDefault(m => m.Id == messageId);
            if (match != null) return match;
            if (!response.HasMore || !response.NextBeforeMessageId.HasValue) break;
            beforeId = response.NextBeforeMessageId.Value;
        }
        return null;
    }

    private static async Task<bool> DeleteMessageAsync(int messageId, string mode)
    {
        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5256/") };
        if (!string.IsNullOrWhiteSpace(AuthState.Token)) client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthState.Token);
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/message-deletion/{messageId}") { Content = JsonContent.Create(new { Mode = mode }) };
        using var response = await client.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        DependencyObject? current = VisualTreeHelper.GetParent(element);
        while (current != null) { if (current is T match) return match; current = VisualTreeHelper.GetParent(current); }
        return null;
    }

    private sealed record MessageBubbleInfo(Border Border, int MessageId);
}
