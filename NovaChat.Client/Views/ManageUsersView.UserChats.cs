using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class ManageUsersView
{
    private Button? _viewChatsButton;
    private static readonly bool _userChatsButtonRegistered = RegisterUserChatsButton();

    private static bool RegisterUserChatsButton()
    {
        EventManager.RegisterClassHandler(
            typeof(ManageUsersView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnManageUsersLoadedForChats));
        return true;
    }

    private static void OnManageUsersLoadedForChats(object sender, RoutedEventArgs e)
    {
        if (sender is ManageUsersView view)
            view.EnsureViewChatsButton();
    }

    private void EnsureViewChatsButton()
    {
        if (_viewChatsButton != null || DeleteButton.Parent is not Grid grid)
            return;

        while (grid.RowDefinitions.Count < 9)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Grid.SetRow(DeleteButton, 6);
        Grid.SetRow(FindVisualChildByName<TextBlock>(grid, "DetailCreatedText")!, 3);
        Grid.SetRow(ValidationText, 8);

        var infoText = grid.Children
            .OfType<TextBlock>()
            .FirstOrDefault(x => Grid.GetRow(x) == 6 && x != ValidationText);
        if (infoText != null)
            Grid.SetRow(infoText, 7);

        _viewChatsButton = new Button
        {
            Content = "💬  View User Chats",
            Height = 42,
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Style = TryFindResource("SecondaryButtonStyle") as Style,
            IsEnabled = false
        };
        _viewChatsButton.Click += ViewChatsButton_Click;
        Grid.SetRow(_viewChatsButton, 5);
        grid.Children.Add(_viewChatsButton);
        UpdateViewChatsButtonState();
    }

    private async void ViewChatsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null || _viewChatsButton == null)
            return;

        _viewChatsButton.IsEnabled = false;
        try
        {
            var window = new OwnerUserChatsWindow(_selectedUser.Id, _selectedUser.DisplayName)
            {
                Owner = Window.GetWindow(this)
            };
            window.ShowDialog();
        }
        finally
        {
            UpdateViewChatsButtonState();
        }
    }

    private void UpdateViewChatsButtonState()
    {
        if (_viewChatsButton != null)
            _viewChatsButton.IsEnabled = _selectedUser != null && !_isSaving;
    }

    private static T? FindVisualChildByName<T>(DependencyObject parent, string name) where T : FrameworkElement
    {
        if (parent is T element && string.Equals(element.Name, name, StringComparison.Ordinal))
            return element;

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var result = FindVisualChildByName<T>(VisualTreeHelper.GetChild(parent, i), name);
            if (result != null)
                return result;
        }

        return null;
    }
}
