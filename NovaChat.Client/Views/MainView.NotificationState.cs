namespace NovaChat.Client.Views;

public partial class MainView
{
    private static readonly object NotificationStateLock = new();
    private static int? _activeChatId;

    internal static void SetActiveChat(int chatId)
    {
        lock (NotificationStateLock)
            _activeChatId = chatId;
    }

    internal static void ClearActiveChat(int chatId)
    {
        lock (NotificationStateLock)
        {
            if (_activeChatId == chatId)
                _activeChatId = null;
        }
    }

    internal static bool IsCurrentChat(int chatId)
    {
        lock (NotificationStateLock)
            return _activeChatId == chatId;
    }
}
