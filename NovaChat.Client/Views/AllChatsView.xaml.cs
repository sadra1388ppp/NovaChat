using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views;

public partial class AllChatsView : UserControl
{
    public event Action? BackToChatRequested;

    private readonly ApiService _apiService = new();
    private readonly ObservableCollection<AdminChatItem> _allChats = [];
    private readonly ObservableCollection<AdminChatItem> _filteredChats = [];
    private bool _isLoading;

    public AllChatsView()
    {
        InitializeComponent();
        ChatsList.ItemsSource = _filteredChats;
        Loaded += AllChatsView_Loaded;
    }

    private async void AllChatsView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        await LoadChatsAsync();
    }

    private async Task LoadChatsAsync()
    {
        _isLoading = true;
        StatusText.Text = "Loading conversations...";

        try
        {
            var chats = await _apiService.GetAsync<List<ChatModel>>("api/Chat/all") ?? [];
            _allChats.Clear();

            foreach (var chat in chats)
                _allChats.Add(new AdminChatItem(chat));

            ApplyFilter();
            TotalChatsText.Text = _allChats.Count.ToString();
            StatusText.Text = _allChats.Count == 0
                ? "No conversations are currently stored on the server."
                : $"Showing {_filteredChats.Count} of {_allChats.Count} conversations.";
        }
        catch (Exception ex)
        {
            _allChats.Clear();
            ApplyFilter();
            TotalChatsText.Text = "0";
            StatusText.Text = "Could not load conversations.";
            MessageBox.Show($"Could not load all chats.\n\n{ex.Message}", "All Chats", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _isLoading = false;
        }
    }

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();

        IEnumerable<AdminChatItem> filtered = _allChats;
        if (!string.IsNullOrWhiteSpace(query))
        {
            filtered = _allChats.Where(c => c.SearchText.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        _filteredChats.Clear();
        foreach (var item in filtered)
            _filteredChats.Add(item);

        FilteredCountText.Text = $"{_filteredChats.Count} conversation{(_filteredChats.Count == 1 ? string.Empty : "s")}";
        EmptyText.Visibility = _filteredChats.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isLoading) return;
        ClearDetails();
        await LoadChatsAsync();
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackToChatRequested?.Invoke();

    private async void ChatRowButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is AdminChatItem item)
        {
            ChatsList.SelectedItem = item;
            await ShowChatDetailsAsync(item);
        }
    }

    private async void ChatsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChatsList.SelectedItem is AdminChatItem item)
            await ShowChatDetailsAsync(item);
    }

    private async Task ShowChatDetailsAsync(AdminChatItem item)
    {
        SelectedChatIdText.Text = $"Chat #{item.Chat.Id}  •  Created {item.Chat.CreatedAt.ToLocalTime():g}";
        ParticipantsText.Text = $"{item.Chat.User1Name}  ·  {item.Chat.User1Id}\n{item.Chat.User2Name}  ·  {item.Chat.User2Id}";
        DeleteButton.IsEnabled = true;
        NoMessagesText.Visibility = Visibility.Collapsed;
        MessagesList.ItemsSource = null;
        StatusText.Text = $"Loading history for Chat #{item.Chat.Id}...";

        try
        {
            var history = await _apiService.GetAsync<ChatHistoryResponse>($"api/Chat/{item.Chat.Id}/messages?pageSize=100");
            var messages = history?.Messages ?? [];
            MessagesList.ItemsSource = messages.Select(m => new AdminMessageItem(m)).ToList();
            NoMessagesText.Visibility = messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.Text = $"Chat #{item.Chat.Id} • {messages.Count} message{(messages.Count == 1 ? string.Empty : "s")} loaded.";
        }
        catch (Exception ex)
        {
            NoMessagesText.Visibility = Visibility.Visible;
            StatusText.Text = "Could not load message history.";
            MessageBox.Show($"Could not load this conversation.\n\n{ex.Message}", "All Chats", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (ChatsList.SelectedItem is not AdminChatItem item) return;

        var result = MessageBox.Show(
            $"Delete Chat #{item.Chat.Id}?\n\nThis permanently deletes the conversation and all of its messages.\n\nThis action cannot be undone.",
            "Delete Conversation",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            DeleteButton.IsEnabled = false;
            StatusText.Text = "Deleting conversation...";

            if (!await _apiService.DeleteAsync($"api/Chat/{item.Chat.Id}"))
            {
                MessageBox.Show("The server could not delete this conversation.", "All Chats", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _allChats.Remove(item);
            _filteredChats.Remove(item);
            TotalChatsText.Text = _allChats.Count.ToString();
            ApplyFilter();
            ClearDetails();
            StatusText.Text = "Conversation deleted successfully.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Delete failed.";
            MessageBox.Show($"Could not delete the conversation.\n\n{ex.Message}", "All Chats", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            DeleteButton.IsEnabled = ChatsList.SelectedItem is AdminChatItem;
        }
    }

    private void ClearDetails()
    {
        ChatsList.SelectedItem = null;
        SelectedChatIdText.Text = string.Empty;
        ParticipantsText.Text = string.Empty;
        MessagesList.ItemsSource = null;
        NoMessagesText.Visibility = Visibility.Collapsed;
        DeleteButton.IsEnabled = false;
    }

    private sealed class AdminChatItem
    {
        public ChatModel Chat { get; }
        public string Participants => $"{Chat.User1Name}  ↔  {Chat.User2Name}";
        public string Preview => Chat.LastMessage == null
            ? "No messages yet"
            : $"{Chat.LastMessage.SenderName}: {Chat.LastMessage.Content}";
        public string LastActivityText => (Chat.LastMessage?.SentAt ?? Chat.CreatedAt).ToLocalTime().ToString("g");
        public string Initials
        {
            get
            {
                var a = GetInitials(Chat.User1Name);
                var b = GetInitials(Chat.User2Name);
                return $"{a}{b}";
            }
        }
        public string SearchText => string.Join(" ", Chat.User1Id, Chat.User2Id, Chat.User1Name, Chat.User2Name, Chat.LastMessage?.Content ?? string.Empty, Chat.LastMessage?.SenderName ?? string.Empty);

        public AdminChatItem(ChatModel chat) => Chat = chat;

        private static string GetInitials(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "?";
            var parts = value.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
            return $"{parts[0][..1]}{parts[^1][..1]}".ToUpperInvariant();
        }
    }

    private sealed class AdminMessageItem
    {
        public string SenderName { get; }
        public string Content { get; }
        public string TimeText { get; }

        public AdminMessageItem(MessageModel message)
        {
            SenderName = string.IsNullOrWhiteSpace(message.SenderName) ? message.SenderId : message.SenderName;
            Content = string.IsNullOrWhiteSpace(message.Content) ? "[No text content]" : message.Content;
            TimeText = message.SentAt.ToLocalTime().ToString("g");
        }
    }
}