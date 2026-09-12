using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace NovaChat.Client.Views;

public partial class MainView
{
    static MainView()
    {
        EventManager.RegisterClassHandler(
            typeof(MenuItem),
            MenuItem.ClickEvent,
            new RoutedEventHandler(E2eeCopyMenuItem_Click));

        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(E2eeMediaRecoveryLoaded));
    }

    private static async void E2eeCopyMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || !string.Equals(item.Header?.ToString(), "Copy message", StringComparison.Ordinal))
            return;

        var contextMenu = item.Parent as ContextMenu;
        var placementTarget = contextMenu?.PlacementTarget as DependencyObject;
        if (placementTarget == null)
            return;

        var mainView = FindAncestor<MainView>(placementTarget);
        if (mainView == null)
            return;

        var messageBorder = FindMessageRootBorderForCopy(placementTarget, mainView.MessagesPanel);
        if (messageBorder == null)
            return;

        var text = ExtractVisibleMessageText(messageBorder, mainView);
        if (string.IsNullOrWhiteSpace(text))
            return;

        e.Handled = true;
        try
        {
            Clipboard.SetText(text);
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not copy the message.\n\n{ex.Message}",
                "Copy Message",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }

        await Task.CompletedTask;
    }

    private static string ExtractVisibleMessageText(Border messageBorder, MainView mainView)
    {
        if (messageBorder.Child is not StackPanel panel)
            return string.Empty;

        var directTextBlocks = panel.Children
            .OfType<TextBlock>()
            .Where(textBlock => !string.IsNullOrWhiteSpace(textBlock.Text))
            .ToList();

        if (directTextBlocks.Count == 0)
            return string.Empty;

        var content = directTextBlocks.FirstOrDefault(textBlock =>
            !textBlock.Text.Contains('\u200B', StringComparison.Ordinal) &&
            !LooksLikeClock(textBlock.Text));

        var chat = mainView._currentChatId.HasValue
            ? mainView._chats.FirstOrDefault(x => x.Chat.Id == mainView._currentChatId.Value)?.Chat
            : null;

        if (chat?.IsGroup == true && directTextBlocks.Count > 1)
        {
            var first = directTextBlocks[0];
            if (string.Equals(first.Text, mainView.AuthenticatedDisplaySenderName(), StringComparison.OrdinalIgnoreCase) ||
                !LooksLikeClock(first.Text))
            {
                content = directTextBlocks.Skip(1).FirstOrDefault(textBlock =>
                    !textBlock.Text.Contains('\u200B', StringComparison.Ordinal) &&
                    !LooksLikeClock(textBlock.Text));
            }
        }

        return content?.Text?.Trim() ?? string.Empty;
    }

    private string AuthenticatedDisplaySenderName() => AuthState.Username ?? string.Empty;

    private static bool LooksLikeClock(string value)
    {
        return TimeSpan.TryParseExact(value.Trim(), new[] { "hh\\:mm", "h\\:mm" }, null, out _);
    }

    private static Border? FindMessageRootBorderForCopy(DependencyObject element, Panel messagesPanel)
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is Border border && messagesPanel.Children.Contains(border))
                return border;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static async void E2eeMediaRecoveryLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainView view)
            return;

        await view.RecoverUnrenderedMediaAsync();
    }

    private async Task RecoverUnrenderedMediaAsync()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);

            var pending = MessagesPanel.Children
                .OfType<Border>()
                .Where(IsPendingMediaBubble)
                .ToList();

            if (pending.Count == 0)
                return;

            foreach (var border in pending)
            {
                if (border.Tag is not int messageId || messageId <= 0)
                    continue;

                try
                {
                    await RenderMediaBubbleAsync(border, messageId);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"E2EE media recovery failed for message {messageId}: {ex}");
                }
            }

            if (attempt < 2)
                await Task.Delay(250);
        }
    }

    private static bool IsPendingMediaBubble(Border border)
    {
        if (border.Child is not StackPanel panel)
            return false;

        return panel.Children
            .OfType<TextBlock>()
            .Any(textBlock => textBlock.Text.Contains('\u200B', StringComparison.Ordinal));
    }

    private static MainView? FindAncestor<T>(DependencyObject element) where T : DependencyObject
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
