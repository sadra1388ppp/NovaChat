using NovaChat.Client;
using System.Windows;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private void ManageUsersButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_isOwner) return;
        (Window.GetWindow(this) as MainWindow)?.ShowManageUsers();
    }

    private void AdminSettingsButton_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Admin settings are coming next.", "NovaChat Owner", MessageBoxButton.OK, MessageBoxImage.Information);
}
