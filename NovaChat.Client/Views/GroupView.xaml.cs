using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Input;

namespace NovaChat.Client.Views;

public partial class GroupView : Window
{
    private readonly ApiService _api = new();
    private HubConnection? _hub;
    private GroupModel? _selected;

    public GroupView()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadGroupsAsync();
        Closed += async (_, _) => { if (_hub != null) await _hub.DisposeAsync(); };
    }

    private async Task LoadGroupsAsync()
    {
        var groups = await _api.GetAsync<List<GroupModel>>("api/Group");
        GroupsList.ItemsSource = groups ?? [];
    }

    private async void CreateGroup_Click(object sender, RoutedEventArgs e)
    {
        var group = await _api.PostAsync<CreateGroupRequest, GroupModel>("api/Group", new CreateGroupRequest { Name = GroupNameBox.Text, Description = GroupDescriptionBox.Text });
        if (group == null) { MessageBox.Show("Could not create the group."); return; }
        GroupNameBox.Clear(); GroupDescriptionBox.Clear(); await LoadGroupsAsync(); GroupsList.SelectedItem = group;
    }

    private async void GroupsList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selected = GroupsList.SelectedItem as GroupModel;
        if (_selected == null) return;
        SelectedGroupName.Text = _selected.Name;
        SelectedGroupDescription.Text = _selected.Description;
        await LoadMembersAsync(); await LoadMessagesAsync(); await ConnectGroupAsync();
    }

    private async Task LoadMembersAsync()
    {
        if (_selected == null) return;
        MembersList.ItemsSource = await _api.GetAsync<List<GroupMemberModel>>($"api/Group/{_selected.Id}/members") ?? [];
    }

    private async Task LoadMessagesAsync()
    {
        if (_selected == null) return;
        MessagesPanel.Children.Clear();
        var messages = await _api.GetAsync<List<GroupMessageModel>>($"api/Group/{_selected.Id}/messages");
        foreach (var m in messages ?? []) AddMessage(m);
    }

    private async Task ConnectGroupAsync()
    {
        if (_hub == null)
        {
            _hub = new HubConnectionBuilder().WithUrl("http://localhost:5256/hubs/chat", o => o.AccessTokenProvider = () => Task.FromResult(AuthState.Token)).WithAutomaticReconnect().Build();
            _hub.On<GroupMessageModel>("ReceiveGroupMessage", m => Dispatcher.Invoke(() => { if (_selected?.Id == m.GroupId) AddMessage(m); }));
            await _hub.StartAsync();
        }
        if (_selected != null) await _hub.InvokeAsync("JoinGroup", _selected.Id);
    }

    private async void AddMember_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;
        var result = await _api.PostAsync<AddGroupMemberRequest, object>($"api/Group/{_selected.Id}/members", new AddGroupMemberRequest { UserId = MemberIdBox.Text });
        if (result == null) MessageBox.Show("Could not add member."); else { MemberIdBox.Clear(); await LoadMembersAsync(); }
    }

    private void MembersList_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) { }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

    private async void MessageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await SendMessageAsync(); }
    }

    private async Task SendMessageAsync()
    {
        if (_selected == null || string.IsNullOrWhiteSpace(MessageBox.Text) || _hub == null) return;
        var text = MessageBox.Text.Trim(); MessageBox.Clear();
        await _hub.InvokeAsync("SendGroupMessage", _selected.Id, text);
    }

    private void AddMessage(GroupMessageModel message)
    {
        MessagesPanel.Children.Add(new System.Windows.Controls.TextBlock { Text = $"{message.SenderName}: {message.Content}", Margin = new Thickness(0, 4, 0, 4), TextWrapping = TextWrapping.Wrap });
    }
}