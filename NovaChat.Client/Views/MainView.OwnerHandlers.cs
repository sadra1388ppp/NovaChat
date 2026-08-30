using System.Windows;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private void ManageUsersButton_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("User management panel is coming next.", "NovaChat Owner", MessageBoxButton.OK, MessageBoxImage.Information);

    private void AllChatsButton_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("All chats panel is coming next.", "NovaChat Owner", MessageBoxButton.OK, MessageBoxImage.Information);

    private void DeleteChatsButton_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Select a chat from the list to delete it.", "NovaChat Owner", MessageBoxButton.OK, MessageBoxImage.Information);

    private void ServerOverviewButton_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Server overview is coming next.", "NovaChat Owner", MessageBoxButton.OK, MessageBoxImage.Information);

    private void AdminSettingsButton_Click(object sender, RoutedEventArgs e)
        => MessageBox.Show("Admin settings are coming next.", "NovaChat Owner", MessageBoxButton.OK, MessageBoxImage.Information);
}
