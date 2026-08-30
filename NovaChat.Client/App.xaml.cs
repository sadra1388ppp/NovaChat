using System.Windows;
using NovaChat.Client.Views;

namespace NovaChat.Client;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        MainView.RegisterMediaFeatures();
        MainView.RegisterLiveRefresh();
        MainView.RegisterVoiceSendFix();
        MainView.RegisterReliableVoiceFix();
        MainView.RegisterImageViewer();
        base.OnStartup(e);
    }
}
