using System.Drawing;
using Forms = System.Windows.Forms;

namespace NovaChat.Client.Services;

public static class NotificationService
{
    private static readonly object SyncRoot = new();
    private static Forms.NotifyIcon? _notifyIcon;

    public static void ShowMessageNotification(string title, string message)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(message))
            return;

        try
        {
            lock (SyncRoot)
            {
                _notifyIcon ??= CreateNotifyIcon();
                _notifyIcon.BalloonTipTitle = title.Trim();
                _notifyIcon.BalloonTipText = message.Trim();
                _notifyIcon.ShowBalloonTip(3500);
            }
        }
        catch
        {
            // Notifications must never be allowed to break message delivery or the UI.
        }
    }

    public static void Dispose()
    {
        lock (SyncRoot)
        {
            if (_notifyIcon == null)
                return;

            try
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
            catch
            {
                // Best-effort cleanup during application shutdown.
            }
            finally
            {
                _notifyIcon = null;
            }
        }
    }

    private static Forms.NotifyIcon CreateNotifyIcon()
    {
        return new Forms.NotifyIcon
        {
            Icon = SystemIcons.Information,
            Visible = false,
            Text = "NovaChat"
        };
    }
}
