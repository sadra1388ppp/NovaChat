using System;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views
{
    public partial class MainView : UserControl
    {
        public event Action? ProfileRequested;
        public event Action? SettingsRequested;

        public MainView()
        {
            InitializeComponent();

            SetOwnerMode(false);
        }

        public void SetOwnerMode(bool isOwner)
        {
            if (isOwner)
            {
                AccountTypeText.Text =
                    "OWNER • Full Access";

                OwnerPanel.Visibility =
                    Visibility.Visible;
            }
            else
            {
                AccountTypeText.Text =
                    "User Account";

                OwnerPanel.Visibility =
                    Visibility.Collapsed;
            }
        }

        private void ProfileButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            ProfileRequested?.Invoke();
        }

        private void SettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SettingsRequested?.Invoke();
        }

        private void ManageUsersButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show(
                "Manage Users",
                "Owner Control");
        }

        private void AllChatsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show(
                "All Chats",
                "Owner Control");
        }

        private void DeleteChatsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show(
                "Delete Chats",
                "Owner Control");
        }

        private void ServerOverviewButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show(
                "Server Overview",
                "Owner Control");
        }

        private void AdminSettingsButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            MessageBox.Show(
                "Admin Settings",
                "Owner Control");
        }
    }
}