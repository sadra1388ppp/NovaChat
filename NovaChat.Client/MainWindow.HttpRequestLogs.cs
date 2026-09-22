using NovaChat.Client.Views;

namespace NovaChat.Client;

public partial class MainWindow
{
    public void ShowHttpRequestLogs()
    {
        if (!_isOwner)
            return;

        MainContainer.Children.Clear();

        var view = new HttpRequestLogsView();
        view.BackToChatRequested += ShowMain;
        MainContainer.Children.Add(view);
    }
}
