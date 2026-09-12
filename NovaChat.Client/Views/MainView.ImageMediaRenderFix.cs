using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views;

public partial class MainView
{
    [ModuleInitializer]
    internal static void InitializeImageMediaRenderFix()
    {
        EventManager.RegisterClassHandler(
            typeof(Border),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ImageMediaRenderFixLoaded));
    }

    private static void ImageMediaRenderFixLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border)
            return;

        if (FindImageMediaLabel(border) is not string label)
            return;

        if (!TryGetMediaMessageId(label, out var messageId))
            return;

        if (FindRenderedImage(border) != null)
            return;

        if (FindMainViewAncestor(border) is not MainView view)
            return;

        _ = view.RenderImageMediaBubbleFixAsync(border, messageId, label);
    }

    private async Task RenderImageMediaBubbleFixAsync(Border border, int messageId, string label)
    {
        try
        {
            if (FindRenderedImage(border) != null)
                return;

            await ReplaceWithImageBubbleAsync(border, messageId, label);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"Image media rendering failed for message {messageId}: {ex}");
        }
    }

    private static string? FindImageMediaLabel(Border border)
    {
        if (border.Child is not StackPanel panel)
            return null;

        return panel.Children
            .OfType<TextBlock>()
            .Select(x => x.Text)
            .FirstOrDefault(x =>
                !string.IsNullOrWhiteSpace(x) &&
                x.StartsWith("📷", StringComparison.Ordinal) &&
                x.Contains("\u200B", StringComparison.Ordinal));
    }

    private static bool TryGetMediaMessageId(string label, out int messageId)
    {
        messageId = 0;
        var match = Regex.Match(label, @"\u200B(\d+)$");
        return match.Success &&
               int.TryParse(match.Groups[1].Value, out messageId) &&
               messageId > 0;
    }

    private static Image? FindRenderedImage(DependencyObject root)
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is Image image)
                return image;

            var nested = FindRenderedImage(child);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static MainView? FindMainViewAncestor(DependencyObject element)
    {
        DependencyObject? current = element;
        while (current != null)
        {
            if (current is MainView view)
                return view;

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
