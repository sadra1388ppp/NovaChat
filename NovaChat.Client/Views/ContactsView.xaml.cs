using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NovaChat.Client.Views;

public partial class ContactsView : UserControl
{
    public event Action? BackToChatRequested;

    private readonly ApiService _apiService = new();

    public ContactsView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadContactsAsync();
    }

    private async Task LoadContactsAsync()
    {
        try
        {
            var contacts = await _apiService.GetAsync<List<ContactModel>>("api/Contact");
            ContactsList.ItemsSource = contacts ?? [];
            StatusText.Text = contacts == null ? "Could not load contacts." : $"{contacts.Count} contact(s)";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private async void SearchTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            await SearchAsync();
    }

    private async Task SearchAsync()
    {
        var query = SearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            SearchResultsList.ItemsSource = [];
            StatusText.Text = "Type a User ID, name, or email to search.";
            return;
        }

        try
        {
            StatusText.Text = "Searching...";
            var results = await _apiService.GetAsync<List<UserSearchResultModel>>(
                $"api/User/search?q={Uri.EscapeDataString(query)}");

            SearchResultsList.ItemsSource = results ?? [];
            StatusText.Text = results == null
                ? "Search failed."
                : $"{results.Count} result(s) found.";
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async void AddSearchResultButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not UserSearchResultModel user)
            return;

        await AddContactAsync(user.Id);
    }

    private async void ChatButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not UserSearchResultModel user)
            return;

        await StartChatAsync(user.Id);
    }

    private async void ChatContactButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not ContactModel contact)
            return;

        await StartChatAsync(contact.UserId);
    }

    private async Task AddContactAsync(string userId)
    {
        try
        {
            var result = await _apiService.PostAsync<AddContactRequest, ContactActionResponse>(
                "api/Contact",
                new AddContactRequest { UserId = userId });

            if (result == null)
            {
                StatusText.Text = "Could not add contact. It may already exist.";
                return;
            }

            StatusText.Text = result.Message;
            await LoadContactsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
    }

    private async Task StartChatAsync(string userId)
    {
        try
        {
            var result = await _apiService.PostAsync<CreateChatRequest, CreateChatResponse>(
                "api/Chat",
                new CreateChatRequest { UserId = userId });

            if (result?.Chat == null)
            {
                MessageBox.Show(
                    "Could not start the chat. Make sure the user still exists.",
                    "NovaChat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            StatusText.Text = "Chat created. Opening your chats...";
            BackToChatRequested?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not start chat.\n\n{ex.Message}",
                "NovaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var userId = SearchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            StatusText.Text = "Search for a user first.";
            return;
        }

        await AddContactAsync(userId);
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not ContactModel contact)
            return;

        if (MessageBox.Show($"Remove {contact.DisplayName} from contacts?", "NovaChat", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var removed = await _apiService.DeleteAsync($"api/Contact/{Uri.EscapeDataString(contact.UserId)}");
        StatusText.Text = removed ? "Contact removed." : "Could not remove contact.";
        if (removed)
            await LoadContactsAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        BackToChatRequested?.Invoke();
    }

    private sealed class ContactActionResponse
    {
        public string Message { get; set; } = string.Empty;
    }
}