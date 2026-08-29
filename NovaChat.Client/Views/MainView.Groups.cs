using NovaChat.Client.Models;
using System.Windows;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private List<GroupModel> _groups = [];
    private bool _groupsLoaded;

    private async void GroupsList_Loaded(object sender, RoutedEventArgs e)
    {
        if (_groupsLoaded || !AuthState.IsAuthenticated)
            return;

        _groupsLoaded = true;
        await LoadGroupsIntoConversationsAsync();
    }

    private async Task LoadGroupsIntoConversationsAsync()
    {
        try
        {
            var groups = await _apiService.GetAsync<List<GroupModel>>("api/Group");
            _groups = groups ?? [];
            GroupsList.ItemsSource = _groups;
            NoGroupsText.Visibility = _groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        catch
        {
            _groups = [];
            GroupsList.ItemsSource = _groups;
            NoGroupsText.Visibility = Visibility.Visible;
        }
    }

    private void GroupButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not GroupModel group)
            return;

        var view = new GroupView
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        view.Show();
    }

    private void GroupsButton_Click(object sender, RoutedEventArgs e)
    {
        var window = new GroupView
        {
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        window.ShowDialog();
    }
}
