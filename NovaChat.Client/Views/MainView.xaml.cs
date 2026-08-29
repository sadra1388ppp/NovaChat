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

    private const int MessagePageSize = 50;

    private readonly ApiService _apiService;
    private HubConnection? _hubConnection;
    private readonly List<ChatModel> _chats = [];
    private readonly HashSet<int> _loadedMessageIds = [];
    private readonly HashSet<string> _onlineUserIds = new(StringComparer.OrdinalIgnoreCase);
    private int? _currentChatId;
    private int? _oldestLoadedMessageId;
    private string _currentOtherUserId = string.Empty;
    private bool _isOwner;
    private bool _isLoadingOlderMessages;
    private bool _hasMoreMessages;
    private bool _isOpeningChat;
    private static bool _messageDeletionHandlerRegistered;

    public MainView()
    {
        InitializeComponent();
        _apiService = new ApiService();

        if (!_messageDeletionHandlerRegistered)
        {
            EventManager.RegisterClassHandler(typeof(Border), UIElement.MouseRightButtonUpEvent,
                new MouseButtonEventHandler(OnMessageBubbleRightClick));
            _messageDeletionHandlerRegistered = true;
        }

        SetOwnerMode(false);
        Loaded += MainView_Loaded;
        Unloaded += MainView_Unloaded;
    }

    private async void MainView_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await ConnectSignalRAsync();
            await LoadChatsAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not initialize chat.\n\n{ex.Message}", "NovaChat",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void MainView_Unloaded(object sender, RoutedEventArgs e)
    {
        await DisconnectSignalRAsync();
    }
}