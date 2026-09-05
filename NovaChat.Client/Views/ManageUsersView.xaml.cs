using NovaChat.Client.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class ManageUsersView : UserControl
{
    public event Action? BackToChatRequested;

    private readonly ApiService _apiService = new();
    private readonly ObservableCollection<AdminUserModel> _users = [];
    private readonly ObservableCollection<AdminUserModel> _filteredUsers = [];
    private AdminUserModel? _selectedUser;
    private bool _isLoading;
    private bool _isSaving;

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
            MessageBox.Show($"Could not load users.\n\n{ex.Message}", "Manage Users", MessageBoxButton.OK, MessageBoxImage.Error);
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
                Contains(user.Username, query) ||
                Contains(user.Email, query));

        _filteredUsers.Clear();
        foreach (var user in results)
            _filteredUsers.Add(user);

        CountText.Text = _filteredUsers.Count == _users.Count
            ? $"{_users.Count} user{(_users.Count == 1 ? "" : "s")}"
            : $"{_filteredUsers.Count} of {_users.Count}";

        EmptyText.Visibility = _filteredUsers.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static bool Contains(string? value, string query) =>
        !string.IsNullOrWhiteSpace(value) && value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await LoadUsersAsync();

    private async void UserRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: AdminUserModel user }) return;
        UsersList.SelectedItem = user;
        UsersList.ScrollIntoView(user);
        await LoadSelectedUserDetailsAsync(user.Id);
    }

    private async void UsersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UsersList.SelectedItem is not AdminUserModel user) return;
        _selectedUser = user;
        PopulateBasicDetails(user);
        await LoadSelectedUserDetailsAsync(user.Id);
    }

    private async Task LoadSelectedUserDetailsAsync(string id)
    {
        if (_selectedUser == null || !string.Equals(_selectedUser.Id, id, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var details = await _apiService.GetAsync<UserDetailsModel>($"api/User/{Uri.EscapeDataString(id)}");
            if (details == null) return;

            _selectedUser.Id = details.Id;
            _selectedUser.Username = details.Username;
            _selectedUser.DisplayName = details.DisplayName;
            _selectedUser.Email = details.Email;
            _selectedUser.PhoneNumber = details.PhoneNumber ?? string.Empty;
            _selectedUser.Bio = details.Bio ?? string.Empty;
            _selectedUser.IsOnline = details.IsOnline;
            _selectedUser.LastSeenAt = details.LastSeenAt;
            _selectedUser.CreatedAt = details.CreatedAt;
            _selectedUser.Initials = BuildInitials(details.DisplayName);
            _selectedUser.StatusBrush = details.IsOnline ? Brushes.LimeGreen : Brushes.Gray;

            PopulateDetails(_selectedUser);
            PopulateEditor(_selectedUser);
            UpdateActionState();
        }
        catch (Exception ex)
        {
            ValidationText.Text = $"Could not load full user details: {ex.Message}";
            ValidationText.Visibility = Visibility.Visible;
        }
    }

    private void PopulateBasicDetails(AdminUserModel user)
    {
        DetailInitialsText.Text = user.Initials;
        DetailNameText.Text = user.DisplayName;
        DetailIdText.Text = $"@{user.Username}";
        DetailCreatedText.Text = FormatDate(user.CreatedAt);
        StatusText.Text = "Loading details…";
        StatusText.Foreground = FindBrush("SecondaryTextBrush");
        ValidationText.Visibility = Visibility.Collapsed;
        ClearEditor();
        DeleteButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
    }

    private void PopulateDetails(AdminUserModel user)
    {
        DetailInitialsText.Text = user.Initials;
        DetailNameText.Text = user.DisplayName;
        DetailIdText.Text = $"@{user.Username}";
        DetailCreatedText.Text = $"Joined  •  {FormatDate(user.CreatedAt)}";
        DetailLastSeenText.Text = user.IsOnline
            ? "Currently online"
            : user.LastSeenAt.HasValue
                ? $"Last seen  •  {FormatDate(user.LastSeenAt.Value)}"
                : "Last seen  •  Never recorded";
        StatusText.Text = user.IsOnline ? "● Online" : "● Offline";
        StatusText.Foreground = user.IsOnline ? Brushes.LimeGreen : FindBrush("SecondaryTextBrush");
    }

    private void PopulateEditor(AdminUserModel user)
    {
        DisplayNameBox.Text = user.DisplayName;
        EmailBox.Text = user.Email;
        PhoneBox.Text = user.PhoneNumber;
        UserIdBox.Text = user.Username;
        BioBox.Text = user.Bio;
        UpdateActionState();
    }

    private void ClearEditor()
    {
        DisplayNameBox.Text = string.Empty;
        EmailBox.Text = string.Empty;
        PhoneBox.Text = string.Empty;
        UserIdBox.Text = string.Empty;
        BioBox.Text = string.Empty;
        DetailEmailTextFallback();
        DetailLastSeenText.Text = string.Empty;
        StatusText.Text = string.Empty;
        ValidationText.Visibility = Visibility.Collapsed;
    }

    private void DetailEmailTextFallback()
    {
        // Email is represented by the editor while a detailed request is loading.
    }

    private void UpdateActionState()
    {
        var hasUser = _selectedUser != null;
        var isOwner = hasUser && IsOwner(_selectedUser!.Id);
        SaveButton.IsEnabled = hasUser && !_isSaving;
        CancelButton.IsEnabled = hasUser && !_isSaving;
        DeleteButton.IsEnabled = hasUser && !isOwner && !_isSaving;
    }

    private async void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null || _isSaving) return;

        ValidationText.Visibility = Visibility.Collapsed;

        var displayName = DisplayNameBox.Text.Trim();
        var email = EmailBox.Text.Trim();
        var phone = PhoneBox.Text.Trim();
        var username = UserIdBox.Text.Trim();
        var bio = BioBox.Text.Trim();

        if (displayName.Length < 2 || displayName.Length > 50)
        {
            ShowValidation("Display name must be between 2 and 50 characters.");
            return;
        }

        if (!IsValidEmail(email))
        {
            ShowValidation("Enter a valid email address.");
            return;
        }

        if (!IsValidPhone(phone))
        {
            ShowValidation("Enter a valid phone number using 7 to 15 digits.");
            return;
        }

        if (username.Length < 3 || username.Length > 32 || !Regex.IsMatch(username, "^[a-zA-Z0-9_.-]+$"))
        {
            ShowValidation("Username must be 3–32 characters and may contain only letters, numbers, dot, underscore and hyphen.");
            return;
        }

        if (bio.Length > 160)
        {
            ShowValidation("Bio must be 160 characters or fewer.");
            return;
        }

        if (IsOwner(_selectedUser.Id) && !string.Equals(username, _selectedUser.Username, StringComparison.OrdinalIgnoreCase))
        {
            ShowValidation("The Owner Username cannot be changed from this panel.");
            return;
        }

        var oldId = _selectedUser.Id;
        var request = new UpdateUserRequest
        {
            DisplayName = displayName,
            Email = email,
            PhoneNumber = phone,
            Bio = bio,
            NewUsername = username
        };

        _isSaving = true;
        UpdateActionState();
        try
        {
            var response = await _apiService.PutAsync<UpdateUserRequest, UpdateUserResponse>(
                $"api/Admin/users/{Uri.EscapeDataString(oldId)}", request);

            if (response?.User == null)
                throw new InvalidOperationException("The server did not return the updated user.");

            var updated = response.User;
            var target = _users.FirstOrDefault(u => string.Equals(u.Id, oldId, StringComparison.OrdinalIgnoreCase));
            if (target != null)
            {
                target.Id = updated.Id;
                target.Username = updated.Username;
                target.DisplayName = updated.DisplayName;
                target.Email = updated.Email;
                target.PhoneNumber = updated.PhoneNumber ?? string.Empty;
                target.Bio = updated.Bio ?? string.Empty;
                target.IsOnline = updated.IsOnline;
                target.LastSeenAt = updated.LastSeenAt;
                target.CreatedAt = updated.CreatedAt;
                target.Initials = BuildInitials(updated.DisplayName);
                target.StatusBrush = updated.IsOnline ? Brushes.LimeGreen : Brushes.Gray;
                _selectedUser = target;
            }

            ApplyFilter();
            PopulateDetails(_selectedUser!);
            PopulateEditor(_selectedUser!);

            MessageBox.Show(
                response.Message ?? "User updated successfully.",
                "Manage Users",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowValidation($"Could not save changes.\n{ex.Message}");
        }
        finally
        {
            _isSaving = false;
            UpdateActionState();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        PopulateDetails(_selectedUser);
        PopulateEditor(_selectedUser);
        ValidationText.Visibility = Visibility.Collapsed;
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null || IsOwner(_selectedUser.Id) || _isSaving) return;

        var user = _selectedUser;
        var result = MessageBox.Show(
            $"Delete the account for {user.DisplayName}?\n\nThis action cannot be undone.\nThe user's chats, messages and contacts will also be removed.",
            "Delete User",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes) return;

        _isSaving = true;
        UpdateActionState();
        try
        {
            await _apiService.DeleteAsync($"api/Admin/users/{Uri.EscapeDataString(user.Id)}");
            _users.Remove(user);
            ApplyFilter();
            ClearSelection();

            MessageBox.Show("The user account was deleted successfully.", "Manage Users", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowValidation($"Could not delete the user.\n{ex.Message}");
        }
        finally
        {
            _isSaving = false;
            UpdateActionState();
        }
    }

    private void ClearSelection()
    {
        _selectedUser = null;
        UsersList.SelectedItem = null;
        DetailInitialsText.Text = "";
        DetailNameText.Text = "Select a user";
        DetailIdText.Text = "";
        DetailCreatedText.Text = "";
        DetailLastSeenText.Text = "";
        StatusText.Text = "";
        ClearEditor();
        DeleteButton.IsEnabled = false;
        SaveButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
    }

    private static bool IsOwner(string userId) =>
        !string.IsNullOrWhiteSpace(AuthState.UserId) &&
        string.Equals(userId, AuthState.UserId, StringComparison.OrdinalIgnoreCase);

    private static bool IsValidEmail(string email)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(email) &&
                   new System.Net.Mail.MailAddress(email).Address.Equals(email, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsValidPhone(string phone) =>
        Regex.IsMatch(phone.Replace(" ", string.Empty).Replace("-", string.Empty).Replace("(", string.Empty).Replace(")", string.Empty), "^\\+?[0-9]{7,15}$");

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }

    private Brush FindBrush(string key)
    {
        return TryFindResource(key) as Brush ?? Brushes.Gray;
    }

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
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public bool IsOnline { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Initials { get; set; } = "?";
        public Brush StatusBrush { get; set; } = Brushes.Gray;
    }

    private sealed class UserDetailsModel
    {
        public string Id { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? Bio { get; set; }
        public bool IsOnline { get; set; }
        public DateTime? LastSeenAt { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    private sealed class UpdateUserRequest
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Bio { get; set; } = string.Empty;
        public string NewUsername { get; set; } = string.Empty;
    }

    private sealed class UpdateUserResponse
    {
        public string? Message { get; set; }
        public UserDetailsModel? User { get; set; }
    }
}
