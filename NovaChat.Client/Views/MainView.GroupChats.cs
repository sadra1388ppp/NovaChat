using System.Windows;
using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Services;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaChat.Client.Models;
using Microsoft.Win32;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private sealed class GroupMemberModel { public string UserId { get; set; } = string.Empty; public string Username { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public string Role { get; set; } = string.Empty; }
    private static bool _groupUiRegistered;
    private Button? _createGroupButton;
    private DispatcherTimer? _groupEventTimer;
    private bool _groupEventsHooked;
    private List<GroupMemberModel> _currentGroupMembers = [];
    private bool IsCurrentGroupChat => _currentChatId.HasValue && _chats.FirstOrDefault(x => x.Chat.Id == _currentChatId.Value)?.Chat.IsGroup == true;

    private void RefreshGroupOnlineStatus() { if (!IsCurrentGroupChat || !_currentChatId.HasValue) return; _ = RefreshCurrentGroupInfoAsync(); }
    private void UpdateGroupOnlineStatusFromCache() { if (!IsCurrentGroupChat) return; var online = _currentGroupMembers.Count(member => _onlineUserIds.Contains(member.UserId)); ChatStatusText.Text = $"{online} member{(online == 1 ? "" : "s")} online"; ChatStatusIndicator.Fill = online > 0 ? System.Windows.Media.Brushes.LimeGreen : System.Windows.Media.Brushes.Gray; }
    private async Task RefreshCurrentGroupInfoAsync()
    {
        if (!_currentChatId.HasValue || !IsCurrentGroupChat) return;
        try { var members = await _apiService.GetAsync<List<GroupMemberModel>>($"api/Chat/{_currentChatId.Value}/members") ?? []; _currentGroupMembers = members; UpdateGroupOnlineStatusFromCache(); await RefreshCurrentGroupAvatarAsync(); } catch { }
    }

    private async Task RefreshCurrentGroupAvatarAsync()
    {
        if (!_currentChatId.HasValue || !IsCurrentGroupChat) return;
        var item = _chats.FirstOrDefault(x => x.Chat.Id == _currentChatId.Value);
        if (item == null) return;
        if (string.IsNullOrWhiteSpace(item.Chat.AvatarUrl)) { ChatHeaderAvatarImage.Source = null; ChatHeaderAvatarImage.Visibility = Visibility.Collapsed; ChatAvatarInitialsText.Visibility = Visibility.Visible; return; }
        try { var image = await LoadConversationAvatarAsync(_apiService.BuildAbsoluteUrl(item.Chat.AvatarUrl)); if (image == null) return; await Dispatcher.InvokeAsync(() => { ChatHeaderAvatarImage.Source = image; ChatHeaderAvatarImage.Visibility = Visibility.Visible; ChatAvatarInitialsText.Visibility = Visibility.Collapsed; }); } catch { }
    }

    private void ChatHeaderGroupInfo_Click(object sender, System.Windows.Input.MouseButtonEventArgs e) { if (!IsCurrentGroupChat) return; e.Handled = true; OpenGroupInfo(); }

    private async void OpenGroupInfo()
    {
        if (!IsCurrentGroupChat || !_currentChatId.HasValue) return;
        try
        {
            var chatId = _currentChatId.Value;
            var members = await _apiService.GetAsync<List<GroupMemberModel>>($"api/Chat/{chatId}/members") ?? [];
            _currentGroupMembers = members;
            var online = members.Count(member => _onlineUserIds.Contains(member.UserId));
            var currentMember = members.FirstOrDefault(m => string.Equals(m.UserId, AuthState.UserId, StringComparison.OrdinalIgnoreCase));
            var canEdit = string.Equals(currentMember?.Role, "Owner", StringComparison.OrdinalIgnoreCase) || string.Equals(currentMember?.Role, "Admin", StringComparison.OrdinalIgnoreCase);
            var chat = _chats.FirstOrDefault(x => x.Chat.Id == chatId)?.Chat;

            var dialog = new Window { Title = "Group Info", Width = 460, Height = 700, Owner = Window.GetWindow(this), WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = (System.Windows.Media.Brush)FindResource("PanelBackgroundBrush") };
            var panel = new StackPanel { Margin = new Thickness(22) };
            var avatarGrid = new Grid { Width = 96, Height = 96, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
            avatarGrid.Children.Add(new Border { CornerRadius = new CornerRadius(48), Background = (System.Windows.Media.Brush)FindResource("PrimarySoftBrush") });
            var avatarInitials = new TextBlock { Text = BuildGroupInitials(chat?.Name), FontSize = 28, FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)FindResource("PrimaryBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            avatarGrid.Children.Add(avatarInitials);
            if (!string.IsNullOrWhiteSpace(chat?.AvatarUrl)) { var img = new Image { Width = 96, Height = 96, Stretch = System.Windows.Media.Stretch.UniformToFill, Source = await LoadConversationAvatarAsync(_apiService.BuildAbsoluteUrl(chat.AvatarUrl)) }; img.Clip = new EllipseGeometry(new System.Windows.Point(48, 48), 48, 48); avatarGrid.Children.Add(img); }
            panel.Children.Add(avatarGrid);
            panel.Children.Add(new TextBlock { Text = chat?.Name ?? ChatUserNameText.Text, FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") });
            panel.Children.Add(new TextBlock { Text = $"{online} member{(online == 1 ? "" : "s")} online • {members.Count} members", HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 14), Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush") });

            if (canEdit)
            {
                var avatarButtons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 16) };
                var changeButton = new Button { Content = "Change picture", Height = 34, Padding = new Thickness(12, 0, 12, 0), Style = (Style)FindResource("SecondaryButtonStyle") };
                changeButton.Click += async (_, _) => { var picker = new OpenFileDialog { Filter = "Image files|*.jpg;*.jpeg;*.png;*.webp", Title = "Choose group picture" }; if (picker.ShowDialog(dialog) != true) return; try { var result = await _apiService.UploadFileAsync<GroupAvatarResponse>($"api/Chat/{chatId}/avatar", picker.FileName); if (result?.Chat == null) throw new InvalidOperationException("The server did not return the updated group."); var item = _chats.FirstOrDefault(x => x.Chat.Id == chatId); if (item != null) item.Chat.AvatarUrl = result.Chat.AvatarUrl; await RefreshConversationAvatarsAsync(); await RefreshCurrentGroupAvatarAsync(); dialog.Close(); OpenGroupInfo(); } catch (Exception ex) { MessageBox.Show(ex.Message, "Group Picture", MessageBoxButton.OK, MessageBoxImage.Warning); } };
                avatarButtons.Children.Add(changeButton);
                var removeButton = new Button { Content = "Remove", Height = 34, Padding = new Thickness(12, 0, 12, 0), Margin = new Thickness(8, 0, 0, 0), Style = (Style)FindResource("DangerButtonStyle") };
                removeButton.Click += async (_, _) => { try { var ok = await _apiService.DeleteAsync($"api/Chat/{chatId}/avatar"); if (ok) { var item = _chats.FirstOrDefault(x => x.Chat.Id == chatId); if (item != null) item.Chat.AvatarUrl = null; await RefreshConversationAvatarsAsync(); await RefreshCurrentGroupAvatarAsync(); dialog.Close(); OpenGroupInfo(); } } catch (Exception ex) { MessageBox.Show(ex.Message, "Group Picture", MessageBoxButton.OK, MessageBoxImage.Warning); } };
                avatarButtons.Children.Add(removeButton); panel.Children.Add(avatarButtons);
            }

            panel.Children.Add(new TextBlock { Text = "MEMBERS", FontWeight = FontWeights.Bold, Foreground = (System.Windows.Media.Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 0, 0, 8) });
            var list = new ListBox { Height = 390, BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent };
            foreach (var member in members) { var isOnline = _onlineUserIds.Contains(member.UserId); var role = string.IsNullOrWhiteSpace(member.Role) ? "Member" : member.Role; list.Items.Add(new TextBlock { Text = $"{(isOnline ? "●" : "○")}  {member.DisplayName}  •  {role}  •  {(isOnline ? "Online" : "Offline")}", FontSize = 15, Margin = new Thickness(6, 8, 6, 8), Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") }); }
            panel.Children.Add(list); dialog.Content = panel; dialog.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show($"Could not load group information.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private static string BuildGroupInitials(string? name) { if (string.IsNullOrWhiteSpace(name)) return "G"; var parts = name.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries); return parts.Length > 1 ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant() : name.Trim()[..Math.Min(2, name.Trim().Length)].ToUpperInvariant(); }
    private sealed class GroupAvatarResponse { public string Message { get; set; } = string.Empty; public ChatModel? Chat { get; set; } }

    static MainView()
    {
        if (_groupUiRegistered) return; _groupUiRegistered = true; EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnGroupUiLoaded));
    }
    private static void OnGroupUiLoaded(object sender, RoutedEventArgs e) { if (sender is not MainView view) return; view.InstallGroupUi(); view.StartGroupEventWatcher(); }
    private void InstallGroupUi()
    {
        if (_createGroupButton != null) return; if (SearchTextBox.Parent is not Grid searchGrid || searchGrid.Parent is not Border searchBorder || searchBorder.Parent is not StackPanel panel) return;
        _createGroupButton = new Button { Content = "👥   New group", Height = 40, Margin = new Thickness(0, 6, 0, 0), HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 0, 10, 0), Style = (Style)FindResource("SecondaryButtonStyle") }; _createGroupButton.Click += CreateGroupButton_Click; panel.Children.Add(_createGroupButton);
    }
    private void StartGroupEventWatcher()
    {
        if (_groupEventTimer != null) return; _groupEventTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) }; _groupEventTimer.Tick += GroupEventTimer_Tick; _groupEventTimer.Start();
    }
    private async void GroupEventTimer_Tick(object? sender, EventArgs e)
    {
        if (_hubConnection?.State != HubConnectionState.Connected) return;
        if (!_groupEventsHooked) { try { _hubConnection.On<ChatModel>("ChatCreated", OnGroupCreatedFromServer); _hubConnection.On<ChatModel>("GroupUpdated", OnGroupUpdatedFromServer); _hubConnection.On<object>("ChatMemberRemoved", OnGroupMemberRemovedFromServer); _groupEventsHooked = true; } catch { } }
        RefreshGroupOnlineStatus(); _ = RefreshConversationAvatarsAsync();
    }
    private async void OnGroupCreatedFromServer(ChatModel chat) { if (chat == null || chat.Id <= 0) return; await Dispatcher.InvokeAsync(async () => { try { await LoadChatsAsync(); } catch { } }); }
    private async void OnGroupUpdatedFromServer(ChatModel chat) { if (chat == null || chat.Id <= 0) return; await Dispatcher.InvokeAsync(async () => { try { var item = _chats.FirstOrDefault(x => x.Chat.Id == chat.Id); if (item != null) item.Chat.AvatarUrl = chat.AvatarUrl; await LoadChatsAsync(); if (_currentChatId == chat.Id) await RefreshCurrentGroupAvatarAsync(); } catch { } }); }
    private async void OnGroupMemberRemovedFromServer(object _) { await Dispatcher.InvokeAsync(async () => { try { await LoadChatsAsync(); } catch { } }); }

    private async void CreateGroupButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new Window { Title = "Create Group", Width = 460, Height = 360, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Window.GetWindow(this), ResizeMode = ResizeMode.NoResize, Background = (System.Windows.Media.Brush)FindResource("PanelBackgroundBrush") };
        var nameBox = new TextBox { Height = 40, Margin = new Thickness(20, 8, 20, 8), Padding = new Thickness(10) }; var membersBox = new TextBox { Height = 110, Margin = new Thickness(20, 8, 20, 8), Padding = new Thickness(10), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }; var createButton = new Button { Content = "Create Group", Width = 120, Height = 38, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20), Style = (Style)FindResource("PrimaryButtonStyle") }; var panel = new StackPanel(); panel.Children.Add(new TextBlock { Text = "Group name", Margin = new Thickness(20, 18, 20, 0), Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") }); panel.Children.Add(nameBox); panel.Children.Add(new TextBlock { Text = "Member usernames (comma or newline separated)", Margin = new Thickness(20, 8, 20, 0), Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") }); panel.Children.Add(membersBox); panel.Children.Add(createButton); dialog.Content = panel;
        CreateGroupRequest? request = null; createButton.Click += (_, _) => { var name = nameBox.Text.Trim(); var usernames = membersBox.Text.Split([',', '\n', '\r', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => !string.Equals(x, AuthState.Username, StringComparison.OrdinalIgnoreCase)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(); if (string.IsNullOrWhiteSpace(name)) { MessageBox.Show("Enter a group name.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Information); return; } request = new CreateGroupRequest { Name = name, Usernames = usernames }; dialog.DialogResult = true; };
        dialog.Loaded += (_, _) => nameBox.Focus(); dialog.ShowDialog(); if (request == null) return;
        try { var result = await _apiService.PostAsync<CreateGroupRequest, CreateGroupResponse>("api/Chat/group", request); if (result?.Chat == null) throw new InvalidOperationException("The server did not return the created group."); await LoadChatsAsync(); await OpenChatAsync(result.Chat); } catch (Exception ex) { MessageBox.Show($"Could not create group.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
}
