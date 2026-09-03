using NovaChat.Client.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views;

public partial class ManageUsersView : UserControl
{
    public event Action? BackToChatRequested;

    private readonly ApiService _apiService = new();
    private readonly ObservableCollection<AdminUserModel> _users = [];
    private readonly ObservableCollection<AdminUserModel> _filteredUsers = [];
    private AdminUserModel? _selectedUser;
    private bool _isLoading;

    public ManageUsersView()
    {
        InitializeComponent();
        UsersList.ItemsSource = _filteredUsers;
        Loaded += ManageUsersView_Loaded;
    }

    private async void ManageUsersView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= ManageUsersView_Loaded;
        await LoadUsersAsync();
    }

    private async Task LoadUsersAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        try
        {
            var users = await _apiService.GetAsync<List<AdminUserModel>>("api/Admin/users") ?? [];

            _users.Clear();
            foreach (var user in users)
            {
                user.Initials = BuildInitials(user.DisplayName);
                _users.Add(user);
            }

            ApplyFilter();
            ClearSelection();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not load users.\n\n{ex.Message}",
                "Manage Users",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var results = string.IsNullOrWhiteSpace(query)
            ? _users
            : _users.Where(user =>
                Contains(user.DisplayName, query) ||
                Contains(user.Email, query) ||
                Contains(user.Id, query));

        _filteredUsers.Clear();
        foreach (var user in results)
            _filteredUsers.Add(user);

        CountText.Text = _filteredUsers.Count == _users.Count
            ? $"{_users.Count} user{(_users.Count == 1 ? "" : "s")}"
            : $"{_filteredUsers.Count} of {_users.Count}";

        EmptyText.Visibility = _filteredUsers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadUsersAsync();

    private void UsersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selectedUser = UsersList.SelectedItem as AdminUserModel;
        UpdateDetails();
    }

    private void UpdateDetails()
    {
        if (_selectedUser == null)
        {
            ClearSelection();
            return;
        }

        DetailInitialsText.Text = _selectedUser.Initials;
        DetailNameText.Text = _selectedUser.DisplayName;
        DetailIdText.Text = $"User ID  •  {_selectedUser.Id}";
        DetailEmailText.Text = string.IsNullOrWhiteSpace(_selectedUser.Email)
            ? "No email address"
            : _selectedUser.Email;
        DetailCreatedText.Text = FormatDate(_selectedUser.CreatedAt);
        DeleteButton.IsEnabled = !IsOwner(_selectedUser.Id);
    }

    private void ClearSelection()
    {
        _selectedUser = null;
        UsersList.SelectedItem = null;
        DetailInitialsText.Text = "";
        DetailNameText.Text = "Select a user";
        DetailIdText.Text = "";
        DetailEmailText.Text = "";
        DetailCreatedText.Text = "";
        DeleteButton.IsEnabled = false;
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null || IsOwner(_selectedUser.Id)) return;

        var user = _selectedUser;
        var result = MessageBox.Show(
            $"Delete the account for {user.DisplayName}?\n\nThis action cannot be undone.",
            "Delete User",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        DeleteButton.IsEnabled = false;
        try
        {
            await _apiService.DeleteAsync($"api/Admin/users/{Uri.EscapeDataString(user.Id)}");
            _users.Remove(user);
            ApplyFilter();
            ClearSelection();

            MessageBox.Show(
                "The user account was deleted successfully.",
                "Manage Users",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not delete the user.\n\n{ex.Message}",
                "Delete User",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            UpdateDetails();
        }
    }

    private static bool IsOwner(string userId) =>
        !string.IsNullOrWhiteSpace(AuthState.UserId) &&
        string.Equals(userId, AuthState.UserId, StringComparison.OrdinalIgnoreCase);

    private static string BuildInitials(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "?";

        var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
            return parts[0].Length >= 2 ? parts[0][..2].ToUpperInvariant() : parts[0].ToUpperInvariant();

        return string.Concat(parts[0][0], parts[^1][0]).ToUpperInvariant();
    }

    private static string FormatDate(DateTime value) =>
        value.ToLocalTime().ToString("MMM d, yyyy  •  HH:mm", CultureInfo.InvariantCulture);

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackToChatRequested?.Invoke();

    private sealed class AdminUserModel
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string Initials { get; set; } = "?";
    }
}
