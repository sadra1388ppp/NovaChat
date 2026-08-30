using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static void OnMessageBubbleRightClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border)
            return;

        var mainView = FindAncestor<MainView>(border);
        if (mainView == null || mainView._currentChatId == null || !IsInsideMessagePanel(border, mainView.MessagesPanel))
            return;

        var messageId = border.Tag is int id ? id : (int?)null;
        string content = string.Empty;
        string timeText = string.Empty;

        if (border.Child is StackPanel panel)
        {
            content = panel.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty;
            timeText = panel.Children.OfType<TextBlock>().Skip(1).FirstOrDefault()?.Text ?? string.Empty;
        }

        var menu = new ContextMenu();
        var deleteItem = new MenuItem { Header = "Delete message" };
        deleteItem.Tag = new MessageBubbleInfo(border, messageId, content, timeText);
        deleteItem.Click += mainView.DeleteMessageMenuItem_Click;
        menu.Items.Add(deleteItem);

        border.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private async void DeleteMessageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not MessageBubbleInfo info || _currentChatId == null)
            return;

        MessageModel? history = null;
        if (info.MessageId.HasValue)
            history = await GetMessageByIdAsync(_currentChatId.Value, info.MessageId.Value);

        history ??= await FindMessageAsync(_currentChatId.Value, info.Content, info.TimeText);
        if (history == null)
        {
            MessageBox.Show("The message could not be located.", "Delete Message", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var isMine = string.Equals(history.SenderId, AuthState.UserId, StringComparison.OrdinalIgnoreCase);
        var mode = "me";

        if (isMine)
        {
            var result = MessageBox.Show(
                "Delete this message for everyone?\n\nChoose No to delete it only for yourself.",
                "Delete Message", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Cancel) return;
            mode = result == MessageBoxResult.Yes ? "everyone" : "me";
        }
        else
        {
            var result = MessageBox.Show(
                "Delete this message for yourself?",
                "Delete Message", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;
        }

        try
        {
            var deleted = await DeleteMessageAsync(history.Id, mode);
            if (!deleted)
            {
                MessageBox.Show("The message could not be deleted.", "Delete Message", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                if (info.Border.Parent is Panel parent)
                    parent.Children.Remove(info.Border);
            });

            _loadedMessageIds.Remove(history.Id);
            await LoadChatsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not delete message.\n\n{ex.Message}", "Delete Message", MessageBoxButton.OK, MessageBoxImage.Error);
        }
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

    private async Task<MessageModel?> FindMessageAsync(int chatId, string content, string timeText)
    {
        int? beforeId = null;
        for (var page = 0; page < 20; page++)
        {
            var endpoint = $"api/Chat/{chatId}/messages?pageSize=100";
            if (beforeId.HasValue) endpoint += $"&beforeMessageId={beforeId.Value}";
            var response = await _apiService.GetAsync<ChatHistoryResponse>(endpoint);
            if (response == null) return null;

            var candidates = response.Messages
                .Where(m => string.Equals(m.Content, content, StringComparison.Ordinal))
                .Where(m => string.Equals(m.SentAt.ToLocalTime().ToString("HH:mm"), timeText, StringComparison.Ordinal))
                .OrderByDescending(m => m.Id)
                .ToList();

            var own = candidates.FirstOrDefault(m => string.Equals(m.SenderId, AuthState.UserId, StringComparison.OrdinalIgnoreCase));
            if (own != null) return own;
            if (candidates.Count > 0) return candidates[0];
            if (!response.HasMore || !response.NextBeforeMessageId.HasValue) break;
            beforeId = response.NextBeforeMessageId.Value;
        }
        return null;
    }

    private static async Task<bool> DeleteMessageAsync(int messageId, string mode)
    {
        using var client = new HttpClient { BaseAddress = new Uri("http://localhost:5256/") };
        if (!string.IsNullOrWhiteSpace(AuthState.Token))
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthState.Token);

        using var request = new HttpRequestMessage(HttpMethod.Delete, $"api/message-deletion/private/{messageId}")
        {
            Content = JsonContent.Create(new { Mode = mode })
        };
        using var response = await client.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private static bool IsInsideMessagePanel(DependencyObject element, DependencyObject messagePanel)
    {
        var current = element;
        while (current != null)
        {
            if (ReferenceEquals(current, messagePanel)) return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private static T? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        var current = VisualTreeHelper.GetParent(element);
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private sealed record MessageBubbleInfo(Border Border, int? MessageId, string Content, string TimeText);
}
