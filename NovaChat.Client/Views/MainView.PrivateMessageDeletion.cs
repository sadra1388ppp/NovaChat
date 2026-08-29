using NovaChat.Client.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static readonly object PrivateMessageDeletionRegistration = RegisterPrivateMessageDeletionHandler();

    private static object RegisterPrivateMessageDeletionHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(Border),
            UIElement.PreviewMouseRightButtonUpEvent,
            new MouseButtonEventHandler(OnPrivateMessageRightClick));

        return new object();
    }

    private static async void OnPrivateMessageRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source)
            return;

        var bubble = FindParentBorder(source);
        if (bubble == null)
            return;

        if (bubble.ContextMenu != null)
            return;

        var mainView = FindParentMainView(bubble);
        if (mainView == null || mainView._currentGroupId.HasValue || !mainView._currentChatId.HasValue)
            return;

        var message = await mainView.ResolvePrivateMessageAsync(bubble);
        if (message == null)
            return;

        e.Handled = true;

        var menu = new ContextMenu();
        var deleteItem = new MenuItem { Header = "Delete message" };
        deleteItem.Click += async (_, _) => await mainView.DeletePrivateMessageAsync(message.Id, bubble);
        menu.Items.Add(deleteItem);

        bubble.ContextMenu = menu;
        menu.IsOpen = true;
    }

    private async Task<MessageModel?> ResolvePrivateMessageAsync(Border bubble)
    {
        if (!_currentChatId.HasValue)
            return null;

        try
        {
            var response = await _apiService.GetAsync<ChatHistoryResponse>(
                $"api/Chat/{_currentChatId.Value}/messages?pageSize=100");

            if (response?.Messages == null || response.Messages.Count == 0)
                return null;

            var bubbles = MessagesPanel.Children
                .OfType<Border>()
                .ToList();

            var bubbleIndex = bubbles.IndexOf(bubble);
            if (bubbleIndex < 0)
                return null;

            var offset = Math.Max(0, response.Messages.Count - bubbles.Count);
            var indexed = bubbleIndex + offset;

            if (indexed >= 0 && indexed < response.Messages.Count)
            {
                var candidate = response.Messages[indexed];
                if (MessageMatchesBubble(candidate, bubble))
                    return candidate;
            }

            return response.Messages.FirstOrDefault(message =>
                MessageMatchesBubble(message, bubble));
        }
        catch
        {
            return null;
        }
    }

    private static bool MessageMatchesBubble(MessageModel message, Border bubble)
    {
        if (bubble.Child is not StackPanel panel)
            return false;

        var textBlocks = panel.Children.OfType<TextBlock>().ToList();
        if (textBlocks.Count < 2)
            return false;

        var contentText = textBlocks[0].Text;
        var timeText = textBlocks[^1].Text;
        var expectedTime = message.SentAt.ToLocalTime().ToString("HH:mm");

        return string.Equals(contentText, message.Content, StringComparison.Ordinal) &&
               string.Equals(timeText, expectedTime, StringComparison.Ordinal);
    }

    private async Task DeletePrivateMessageAsync(int messageId, Border bubble)
    {
        var confirm = MessageBox.Show(
            Window.GetWindow(this),
            "Delete this message?",
            "Delete Message",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes)
            return;

        try
        {
            var deleted = await _apiService.DeleteAsync(
                $"api/Chat/messages/{messageId}");

            if (!deleted)
                return;

            MessagesPanel.Children.Remove(bubble);
            await LoadChatsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                Window.GetWindow(this),
                $"Message could not be deleted.\n\n{ex.Message}",
                "Delete Message",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static Border? FindParentBorder(DependencyObject source)
    {
        var current = source;
        while (current != null)
        {
            if (current is Border border)
                return border;

            current = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static MainView? FindParentMainView(DependencyObject source)
    {
        var current = source;
        while (current != null)
        {
            if (current is MainView mainView)
                return mainView;

            current = LogicalTreeHelper.GetParent(current) ?? VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
