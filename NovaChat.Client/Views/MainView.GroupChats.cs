using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Services;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaChat.Client.Models;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private sealed class GroupMemberModel
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
    private static bool _groupUiRegistered;
    private Button? _createGroupButton;
    private DispatcherTimer? _groupEventTimer;
    private bool IsCurrentGroupChat => _currentChatId.HasValue && _chats.FirstOrDefault(x => x.Chat.Id == _currentChatId.Value)?.Chat.Type?.Equals("Group", StringComparison.OrdinalIgnoreCase) == true;

    private void RefreshGroupOnlineStatus()
    {
        if (!IsCurrentGroupChat || !_currentChatId.HasValue) return;
        _ = RefreshCurrentGroupInfoAsync();
    }

    private async Task RefreshCurrentGroupInfoAsync()
    {
        if (!_currentChatId.HasValue || !IsCurrentGroupChat) return;
        try
        {
            var members = await _apiService.GetAsync<List<GroupMemberModel>>($"api/Chat/{_currentChatId.Value}/members");
            if (members == null) return;
            var online = members.Count(m => _onlineUserIds.Contains(m.UserId));
            ChatStatusText.Text = $"{online} member{(online == 1 ? "" : "s")} online";
            ChatStatusIndicator.Fill = online > 0 ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Gray;
        }
        catch { }
    }

    private void ChatHeaderGroupInfo_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (!IsCurrentGroupChat) return;
        OpenGroupInfo();
    }

    private async void OpenGroupInfo()
    {
        if (!IsCurrentGroupChat || !_currentChatId.HasValue) return;
        var members = await _apiService.GetAsync<List<GroupMemberModel>>($"api/Chat/{_currentChatId.Value}/members") ?? [];
        var online = members.Count(m => _onlineUserIds.Contains(m.UserId));
        var dialog = new Window { Title = "Group Info", Width = 460, Height = 620, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)FindResource("PanelBackgroundBrush") };
        var panel = new StackPanel { Margin = new Thickness(22) };
        panel.Children.Add(new TextBlock { Text = ChatUserNameText.Text, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") });
        panel.Children.Add(new TextBlock { Text = $"{online} member{(online == 1 ? "" : "s")} online • {members.Count} members", Margin = new Thickness(0,6,0,18), Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush") });
        panel.Children.Add(new TextBlock { Text = "MEMBERS", FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0,0,0,8) });
        var list = new ListBox { Height = 410, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent };
        foreach (var m in members)
        {
            var isOnline = _onlineUserIds.Contains(m.UserId);
            list.Items.Add(new TextBlock { Text = $"{(isOnline ? "●" : "○")}  {m.DisplayName}  •  {m.Role}", FontSize = 15, Margin = new Thickness(6,8,6,8), Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") });
        }
        panel.Children.Add(list); dialog.Content = panel; dialog.ShowDialog();
    }

    static MainView()
    {
        if (_groupUiRegistered) return;
        _groupUiRegistered = true;
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnGroupUiLoaded));
    }

    private static void OnGroupUiLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainView view) return;
        view.InstallGroupUi();
        view.StartGroupEventWatcher();
    }

    private void InstallGroupUi()
    {
        if (_createGroupButton != null) return;
        if (SearchTextBox.Parent is not Grid searchGrid || searchGrid.Parent is not Border searchBorder || searchBorder.Parent is not StackPanel panel) return;

        _createGroupButton = new Button
        {
            Content = "👥   New group",
            Height = 40,
            Margin = new Thickness(0, 6, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 0, 10, 0),
            Style = (Style)FindResource("SecondaryButtonStyle")
        };
        _createGroupButton.Click += CreateGroupButton_Click;
        panel.Children.Add(_createGroupButton);
    }

    private void StartGroupEventWatcher()
    {
        if (_groupEventTimer != null) return;
        _groupEventTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _groupEventTimer.Tick += GroupEventTimer_Tick;
        _groupEventTimer.Start();
    }

    private async void GroupEventTimer_Tick(object? sender, EventArgs e)
    {
        if (_hubConnection?.State != Microsoft.AspNetCore.SignalR.Client.HubConnectionState.Connected)
        {
            if (_groupEventTimer != null) _groupEventTimer.Interval = TimeSpan.FromSeconds(2);
            return;
        }
        _groupEventTimer.Stop();
        try
        {
            _hubConnection.On<ChatModel>("ChatCreated", OnGroupCreatedFromServer);
            _hubConnection.On<ChatModel>("GroupUpdated", OnGroupUpdatedFromServer);
            _hubConnection.On<object>("ChatMemberRemoved", OnGroupMemberRemovedFromServer);
        }
        catch { }
    }

    private async void OnGroupCreatedFromServer(ChatModel chat)
    {
        if (chat == null || chat.Id <= 0) return;
        await Dispatcher.InvokeAsync(async () =>
        {
            try { await LoadChatsAsync(); } catch { }
        });
    }

    private async void OnGroupUpdatedFromServer(ChatModel chat)
    {
        if (chat == null || chat.Id <= 0) return;
        await Dispatcher.InvokeAsync(async () => { try { await LoadChatsAsync(); } catch { } });
    }

    private async void OnGroupMemberRemovedFromServer(object _)
    {
        await Dispatcher.InvokeAsync(async () => { try { await LoadChatsAsync(); } catch { } });
    }

    private async void CreateGroupButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window
        {
            Title = "Create Group",
            Width = 460,
            Height = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Background = (System.Windows.Media.Brush)FindResource("PanelBackgroundBrush")
        };

        var nameBox = new TextBox { Height = 40, Margin = new Thickness(20, 8, 20, 8), Padding = new Thickness(10) };
        var membersBox = new TextBox { Height = 110, Margin = new Thickness(20, 8, 20, 8), Padding = new Thickness(10), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var createButton = new Button { Content = "Create Group", Width = 120, Height = 38, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20), Style = (Style)FindResource("PrimaryButtonStyle") };
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Group name", Margin = new Thickness(20, 18, 20, 0), Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") });
        panel.Children.Add(nameBox);
        panel.Children.Add(new TextBlock { Text = "Member usernames (comma or newline separated)", Margin = new Thickness(20, 8, 20, 0), Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") });
        panel.Children.Add(membersBox);
        panel.Children.Add(createButton);
        dialog.Content = panel;

        CreateGroupRequest? request = null;
        createButton.Click += (_, _) =>
        {
            var name = nameBox.Text.Trim();
            var usernames = membersBox.Text.Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => !string.Equals(x, AuthState.Username, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("Enter a group name.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            request = new CreateGroupRequest { Name = name, Usernames = usernames };
            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) => nameBox.Focus();
        dialog.ShowDialog();
        if (request == null) return;

        try
        {
            var result = await _apiService.PostAsync<CreateGroupRequest, CreateGroupResponse>("api/Chat/group", request);
            if (result?.Chat == null) throw new InvalidOperationException("The server did not return the created group.");
            await LoadChatsAsync();
            await OpenChatAsync(result.Chat);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not create group.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
