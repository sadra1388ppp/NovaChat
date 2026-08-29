using System.Windows;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private void GroupsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new GroupView { Owner = Window.GetWindow(this) };
        window.ShowDialog();
    }
}