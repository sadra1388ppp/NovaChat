using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView : UserControl
{
    public event Action? ProfileRequested;
    public event Action? SettingsRequested;

    private readonly ApiService _apiService;

    private HubConnection? _hubConnection;
    private readonly List<ChatListItem> _chats = [];

    private int? _currentChatId;

    private string _currentOtherUserId = string.Empty;

    private bool _isOwner;

    public MainView()
    {
        InitializeComponent();

        _apiService = new ApiService();

        SetOwnerMode(false);

        Loaded += MainView_Loaded;
        Unloaded += MainView_Unloaded;
    }

    private async void MainView_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        try
        {
            await ConnectSignalRAsync();
            await LoadChatsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not initialize chat.\n\n{ex.Message}",
                "NovaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void MainView_Unloaded(
        object sender,
        RoutedEventArgs e)
    {
        await DisconnectSignalRAsync();
    }

    public void SetOwnerMode(bool isOwner)
    {
        _isOwner = isOwner;

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

    private async Task ConnectSignalRAsync()
    {
        if (!AuthState.IsAuthenticated)
            return;

        if (_hubConnection != null)
            return;

        _hubConnection =
            new HubConnectionBuilder()
                .WithUrl(
                    "http://localhost:5256/hubs/chat",
                    options =>
                    {
                        options.AccessTokenProvider =
                            () => Task.FromResult(
                                AuthState.Token)!;
                    })
                .WithAutomaticReconnect()
                .Build();

        _hubConnection.On<MessageModel>(
            "ReceiveMessage",
            OnMessageReceived);

        await _hubConnection.StartAsync();
    }

    private async Task DisconnectSignalRAsync()
    {
        if (_hubConnection == null)
            return;

        try
        {
            await _hubConnection.StopAsync();
            await _hubConnection.DisposeAsync();
        }
        finally
        {
            _hubConnection = null;
        }
    }

    private async void OnMessageReceived(
        MessageModel message)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            if (_currentChatId != message.ChatId)
                return;

            AddMessageToUi(message);

            _ = ScrollMessagesToBottomAsync();
        });
    }

    private async Task LoadChatsAsync()
    {
        var chats =
            await _apiService.GetAsync<
                List<ChatModel>>(
                    "api/Chat");

        _chats.Clear();

        if (chats != null)
        {
            foreach (var chat in chats)
            {
                _chats.Add(
                    new ChatListItem
                    {
                        Chat = chat,

                        DisplayName =
                            chat.OtherUserName(
                                AuthState.UserId),

                        LastMessage =
                            "No messages yet."
                    });
            }
        }

        ChatsList.ItemsSource = null;
        ChatsList.ItemsSource = _chats;

        NoChatsText.Visibility =
            _chats.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private async void NewChatButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        var userId = ShowNewChatDialog();

        if (string.IsNullOrWhiteSpace(userId))
            return;

        userId = userId.Trim();

        if (string.Equals(
                userId,
                AuthState.UserId,
                StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "You cannot create a chat with yourself.",
                "New Chat",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        try
        {
            var result =
                await _apiService.PostAsync<
                    CreateChatRequest,
                    CreateChatResponse>(
                        "api/Chat",
                        new CreateChatRequest
                        {
                            UserId = userId
                        });

            if (result?.Chat == null)
            {
                MessageBox.Show(
                    "User not found or chat could not be created.",
                    "New Chat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            await LoadChatsAsync();

            var createdChat =
                _chats.FirstOrDefault(
                    x => x.Chat.Id == result.Chat.Id);

            if (createdChat != null)
            {
                await OpenChatAsync(
                    createdChat.Chat);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not create chat.\n\n{ex.Message}",
                "NovaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private string? ShowNewChatDialog()
    {
        var window = new Window
        {
            Title = "New Chat",
            Width = 400,
            Height = 230,
            WindowStartupLocation =
                WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background =
                (Brush)FindResource(
                    "PanelBackgroundBrush"),
            Foreground =
                (Brush)FindResource(
                    "TextBrush"),
            Owner =
                Window.GetWindow(this)
        };

        var mainPanel = new StackPanel
        {
            Margin = new Thickness(25)
        };

        var title = new TextBlock
        {
            Text = "Start a new conversation",
            FontSize = 20,
            FontWeight = FontWeights.SemiBold,
            Foreground =
                (Brush)FindResource(
                    "TextBrush"),
            Margin =
                new Thickness(0, 0, 0, 18)
        };

        var label = new TextBlock
        {
            Text = "Enter User ID",
            FontSize = 13,
            Foreground =
                (Brush)FindResource(
                    "SecondaryTextBrush"),
            Margin =
                new Thickness(0, 0, 0, 7)
        };

        var textBox = new TextBox
        {
            Height = 40,
            FontSize = 14,
            Padding = new Thickness(10),
            Background =
                (Brush)FindResource(
                    "InputBackgroundBrush"),
            Foreground =
                (Brush)FindResource(
                    "TextBrush"),
            BorderBrush =
                (Brush)FindResource(
                    "BorderBrush"),
            BorderThickness =
                new Thickness(1)
        };

        var buttons = new StackPanel
        {
            Orientation =
                Orientation.Horizontal,

            HorizontalAlignment =
                HorizontalAlignment.Right,

            Margin =
                new Thickness(0, 18, 0, 0)
        };

        var cancelButton = new Button
        {
            Content = "Cancel",
            Width = 85,
            Height = 35,
            Margin =
                new Thickness(0, 0, 8, 0)
        };

        var startButton = new Button
        {
            Content = "Start Chat",
            Width = 100,
            Height = 35,
            Background =
                (Brush)FindResource(
                    "PrimaryBrush"),
            Foreground = Brushes.White,
            BorderThickness =
                new Thickness(0)
        };

        string? result = null;

        cancelButton.Click += (_, _) =>
        {
            window.DialogResult = false;
        };

        startButton.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(
                    textBox.Text))
            {
                MessageBox.Show(
                    "Please enter a User ID.",
                    "New Chat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                return;
            }

            result = textBox.Text.Trim();

            window.DialogResult = true;
        };

        textBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                startButton.RaiseEvent(
                    new RoutedEventArgs(
                        Button.ClickEvent));
            }
        };

        buttons.Children.Add(cancelButton);
        buttons.Children.Add(startButton);

        mainPanel.Children.Add(title);
        mainPanel.Children.Add(label);
        mainPanel.Children.Add(textBox);
        mainPanel.Children.Add(buttons);

        window.Content = mainPanel;

        window.Loaded += (_, _) =>
        {
            textBox.Focus();
        };

        window.ShowDialog();

        return result;
    }

    private async void ChatButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.DataContext is not ChatListItem item)
        {
            return;
        }

        await OpenChatAsync(item.Chat);
    }

    private async void DeleteChatButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (sender is not Button button ||
            button.DataContext is not ChatListItem item)
        {
            return;
        }

        var result =
            MessageBox.Show(
                $"Delete chat with {item.DisplayName}?\n\nAll messages in this chat will also be deleted.",
                "Delete Chat",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            var deleted =
                await _apiService.DeleteAsync(
                    $"api/Chat/{item.Chat.Id}");

            if (!deleted)
            {
                MessageBox.Show(
                    "The chat could not be deleted.",
                    "Delete Chat",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return;
            }

            if (_currentChatId == item.Chat.Id)
            {
                _currentChatId = null;
                _currentOtherUserId =
                    string.Empty;

                ChatUserNameText.Text =
                    "Select a chat";

                ChatStatusText.Text =
                    "Offline";

                MessagesPanel.Children.Clear();
                MessageTextBox.Clear();
            }

            _chats.Remove(item);

            ChatsList.ItemsSource = null;
            ChatsList.ItemsSource = _chats;

            NoChatsText.Visibility =
                _chats.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not delete chat.\n\n{ex.Message}",
                "Delete Chat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task OpenChatAsync(
        ChatModel chat)
    {
        _currentChatId = chat.Id;

        _currentOtherUserId =
            chat.OtherUserId(
                AuthState.UserId);

        ChatUserNameText.Text =
            chat.OtherUserName(
                AuthState.UserId);

        ChatStatusText.Text =
            "Online connection";

        MessagesPanel.Children.Clear();

        try
        {
            if (_hubConnection != null)
            {
                await _hubConnection.InvokeAsync(
                    "JoinChat",
                    chat.Id);
            }

            var messages =
                await _apiService.GetAsync<
                    List<MessageModel>>(
                    $"api/Chat/{chat.Id}/messages");

            if (messages != null)
            {
                foreach (var message in messages)
                {
                    AddMessageToUi(message);
                }
            }

            await ScrollMessagesToBottomAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not open chat.\n\n{ex.Message}",
                "NovaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async void SendButton_Click(
        object sender,
        RoutedEventArgs e)
    {
        await SendCurrentMessageAsync();
    }

    private async void MessageTextBox_KeyDown(
        object sender,
        KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;

            await SendCurrentMessageAsync();
        }
    }

    private async Task SendCurrentMessageAsync()
    {
        if (_currentChatId == null)
        {
            MessageBox.Show(
                "Please select a chat first.",
                "NovaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            return;
        }

        string content =
            MessageTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(content))
            return;

        if (_hubConnection == null ||
            _hubConnection.State !=
            HubConnectionState.Connected)
        {
            MessageBox.Show(
                "Real-time connection is not available.",
                "NovaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);

            return;
        }

        try
        {
            MessageTextBox.Clear();

            await _hubConnection.InvokeAsync(
                "SendMessage",
                _currentChatId.Value,
                content);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Message could not be sent.\n\n{ex.Message}",
                "NovaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void AddMessageToUi(
        MessageModel message)
    {
        bool isMine =
            string.Equals(
                message.SenderId,
                AuthState.UserId,
                StringComparison.OrdinalIgnoreCase);

        var border = new Border
        {
            Background =
                isMine
                    ? (Brush)FindResource(
                        "PrimaryBrush")
                    : (Brush)FindResource(
                        "PanelBackgroundBrush"),

            Padding =
                new Thickness(12),

            CornerRadius =
                new CornerRadius(12),

            HorizontalAlignment =
                isMine
                    ? HorizontalAlignment.Right
                    : HorizontalAlignment.Left,

            MaxWidth = 450,

            Margin =
                new Thickness(0, 0, 0, 12)
        };

        var panel = new StackPanel();

        var contentText = new TextBlock
        {
            Text = message.Content,

            TextWrapping =
                TextWrapping.Wrap,

            Foreground =
                isMine
                    ? Brushes.White
                    : (Brush)FindResource(
                        "TextBrush")
        };

        panel.Children.Add(contentText);

        var timeText = new TextBlock
        {
            Text =
                message.SentAt
                    .ToLocalTime()
                    .ToString("HH:mm"),

            FontSize = 10,

            Margin =
                new Thickness(0, 5, 0, 0),

            Foreground =
                isMine
                    ? Brushes.White
                    : (Brush)FindResource(
                        "SecondaryTextBrush"),

            HorizontalAlignment =
                HorizontalAlignment.Right
        };

        panel.Children.Add(timeText);

        border.Child = panel;

        MessagesPanel.Children.Add(border);
    }

    private async Task ScrollMessagesToBottomAsync()
    {
        await Task.Delay(50);

        MessagesScrollViewer.ScrollToEnd();
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

    private sealed class ChatListItem
    {
        public ChatModel Chat { get; set; } = new();

        public string DisplayName { get; set; } =
            string.Empty;

        public string LastMessage { get; set; } =
            string.Empty;
    }
}