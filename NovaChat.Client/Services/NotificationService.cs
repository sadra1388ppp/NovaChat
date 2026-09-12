using System.Windows;
using NovaChat.Client.Views;

namespace NovaChat.Client.Services;

public static class NotificationService
{
    private static readonly object SyncRoot = new();
    private static readonly List<NotificationWindow> ActiveNotifications = [];

    public static void ShowMessageNotification(int chatId, string title, string message)
    {
        if (chatId <= 0 || string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            return;

        // The message event can arrive from an older MainView instance while WPF is
        // transitioning between views. Always make the final notification decision
        // against the currently active MainView, not the instance that received the event.
        if (MainView.IsCurrentChat(chatId))
            return;

        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                // Re-check on the UI thread immediately before displaying the popup.
                // This closes the race where the user opens the chat while the event
                // is waiting to be dispatched.
                if (MainView.IsCurrentChat(chatId))
                    return;

                var notification = new NotificationWindow(title.Trim(), message.Trim());
                lock (SyncRoot)
                {
                    ActiveNotifications.Add(notification);
                    RepositionNotifications();
                }

                notification.Closed += (_, _) =>
                {
                    lock (SyncRoot)
                    {
                        ActiveNotifications.Remove(notification);
                        RepositionNotifications();
                    }
                };

                notification.Show();
                notification.Activate();
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification failed: {ex}");
        }
    }

    public static void Dispose()
    {
        try
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                lock (SyncRoot)
                {
                    foreach (var notification in ActiveNotifications.ToArray())
                    {
                        try { notification.Close(); } catch { }
                    }
                    ActiveNotifications.Clear();
                }
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Notification cleanup failed: {ex}");
        }
    }

    private static void RepositionNotifications()
    {
        var workArea = SystemParameters.WorkArea;
        const double rightMargin = 18;
        const double bottomMargin = 18;
        const double gap = 10;
        var bottom = workArea.Bottom - bottomMargin;

        for (var i = ActiveNotifications.Count - 1; i >= 0; i--)
        {
            var notification = ActiveNotifications[i];
            if (!notification.IsLoaded) continue;

            notification.Left = workArea.Right - notification.Width - rightMargin;
            notification.Top = bottom - notification.Height;
            bottom -= notification.Height + gap;
        }
    }
}
