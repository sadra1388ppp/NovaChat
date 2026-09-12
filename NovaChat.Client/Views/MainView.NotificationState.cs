using System.Windows;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static MainView? _activeMainView;
    private static readonly bool _notificationStateHandlersRegistered = RegisterNotificationStateHandlers();

    private static bool RegisterNotificationStateHandlers()
    {
        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainViewLoaded));

        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(OnMainViewUnloaded));

        return true;
    }

    private static void OnMainViewLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView mainView)
            _activeMainView = mainView;
    }

    private static void OnMainViewUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView mainView && ReferenceEquals(_activeMainView, mainView))
            _activeMainView = null;
    }

    internal static bool IsCurrentChatNotification(string title)
    {
        var mainView = _activeMainView;
        if (mainView == null || !mainView.IsLoaded || !mainView._currentChatId.HasValue)
            return false;

        return string.Equals(
            mainView.ChatUserNameText.Text?.Trim(),
            title.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }
}
