using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;

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

    private async void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var userId = UserIdTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(userId))
        {
            StatusText.Text = "Enter a User ID.";
            return;
        }

        try
        {
            var result = await _apiService.PostAsync<AddContactRequest, ContactActionResponse>(
                "api/Contact",
                new AddContactRequest { UserId = userId });

            if (result == null)
            {
                StatusText.Text = "Could not add contact.";
                return;
            }

            UserIdTextBox.Clear();
            StatusText.Text = result.Message;
            await LoadContactsAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
        }
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
