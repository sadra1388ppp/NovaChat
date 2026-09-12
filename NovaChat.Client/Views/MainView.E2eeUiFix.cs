using System.Windows;
using System.Windows.Controls;
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
            typeof(Border),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(E2eeMediaBorderLoaded));
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

        var chat = mainView._currentChatId.HasValue
            ? mainView._chats.FirstOrDefault(x => x.Chat.Id == mainView._currentChatId.Value)?.Chat
            : null;

        var candidates = directTextBlocks
            .Where(textBlock =>
                !textBlock.Text.Contains("\u200B", StringComparison.Ordinal) &&
                !LooksLikeClock(textBlock.Text))
            .ToList();

        if (chat?.IsGroup == true && candidates.Count > 1)
            candidates.RemoveAt(0);

        return candidates.FirstOrDefault()?.Text?.Trim() ?? string.Empty;
    }

    private static bool LooksLikeClock(string value)
    {
        return TimeSpan.TryParseExact(
            value.Trim(),
            new[] { "hh\:mm", "h\:mm" },
            System.Globalization.CultureInfo.InvariantCulture,
            out _);
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

    private static async void E2eeMediaBorderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border || border.Tag is not int messageId || messageId <= 0)
            return;
        if (!IsPendingMediaBubble(border))
            return;
        if (FindAncestor<MainView>(border) is not MainView view)
            return;

        await Task.Delay(350);
        if (!IsPendingMediaBubble(border))
            return;

        try
        {
            await view.RenderMediaBubbleAsync(border, messageId);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"E2EE media recovery failed for message {messageId}: {ex}");
        }
    }

    private static bool IsPendingMediaBubble(Border border)
    {
        if (border.Child is not StackPanel panel)
            return false;

        return panel.Children
            .OfType<TextBlock>()
            .Any(textBlock => textBlock.Text.Contains("\u200B", StringComparison.Ordinal));
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
