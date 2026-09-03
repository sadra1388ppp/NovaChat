using System.Windows;

namespace NovaChat.Client.Views;

public partial class MainView
{
    // Owner Control -> All Chats
    // Kept in a separate partial class so the existing MainView.xaml.cs remains untouched.
    private void AllChatsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isOwner)
            return;

        var window = Window.GetWindow(this) as NovaChat.Client.MainWindow;
        if (window == null)
            return;

        window.ShowAllChats();
    }
}
