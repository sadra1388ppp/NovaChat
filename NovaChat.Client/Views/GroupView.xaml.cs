using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
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
        var dialog = new CreateGroupDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var group = await _api.PostAsync<CreateGroupRequest, GroupModel>(
            "api/Group",
            new CreateGroupRequest { Name = dialog.GroupName, Description = dialog.GroupDescription });

        if (group == null)
        {
            System.Windows.MessageBox.Show(this, "Could not create the group.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await LoadGroupsAsync();
        GroupsList.SelectedItem = group;
    }

    private async void GroupsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _selected = GroupsList.SelectedItem as GroupModel;
        if (_selected == null) return;

        SelectedGroupName.Text = _selected.Name;
        SelectedGroupDescription.Text = _selected.Description;
        await LoadMembersAsync();
        await LoadMessagesAsync();
        await ConnectGroupAsync();
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
            _hub = new HubConnectionBuilder()
                .WithUrl("http://localhost:5256/hubs/chat", o =>
                    o.AccessTokenProvider = () => Task.FromResult<string?>(AuthState.Token))
                .WithAutomaticReconnect()
                .Build();

            _hub.On<GroupMessageModel>("ReceiveGroupMessage", m =>
                Dispatcher.Invoke(() =>
                {
                    if (_selected?.Id == m.GroupId) AddMessage(m);
                }));

            await _hub.StartAsync();
        }

        if (_selected != null)
            await _hub.InvokeAsync("JoinGroup", _selected.Id);
    }

    private async void AddMember_Click(object sender, RoutedEventArgs e)
    {
        if (_selected == null) return;

        var dialog = new AddMemberDialog { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (string.IsNullOrWhiteSpace(dialog.UserId)) return;

        var result = await _api.PostAsync<AddGroupMemberRequest, object>(
            $"api/Group/{_selected.Id}/members",
            new AddGroupMemberRequest { UserId = dialog.UserId.Trim() });

        if (result == null)
        {
            System.Windows.MessageBox.Show(
                this,
                "We couldn't add this user. Check the User ID and your permissions.",
                "NovaChat",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
        else
        {
            await LoadMembersAsync();
        }
    }

    private void MembersList_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

    private async void MessageBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        if (_selected == null || string.IsNullOrWhiteSpace(MessageBox.Text) || _hub == null) return;

        var text = MessageBox.Text.Trim();
        MessageBox.Clear();
        await _hub.InvokeAsync("SendGroupMessage", _selected.Id, text);
    }

    private void AddMessage(GroupMessageModel message)
    {
        MessagesPanel.Children.Add(new TextBlock
        {
            Text = $"{message.SenderName}: {message.Content}",
            Margin = new Thickness(0, 4, 0, 4),
            TextWrapping = TextWrapping.Wrap
        });
    }
}

public sealed class AddMemberDialog : Window
{
    public string UserId => UserIdBox.Text;
    private readonly TextBox UserIdBox = new();

    public AddMemberDialog()
    {
        Title = "Add Member";
        Width = 420;
        Height = 235;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;

        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock { Text = "Add a member", FontSize = 22, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "Enter the user's NovaChat ID",
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 5, 0, 14)
        });
        root.Children.Add(UserIdBox);

        var add = new Button
        {
            Content = "Add member",
            Width = 120,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        add.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(UserIdBox.Text))
            {
                DialogResult = true;
                Close();
            }
        };

        root.Children.Add(add);
        Content = root;
        UserIdBox.Focus();
    }
}

public sealed class CreateGroupDialog : Window
{
    public string GroupName => NameBox.Text.Trim();
    public string GroupDescription => DescriptionBox.Text.Trim();

    private readonly TextBox NameBox = new();
    private readonly TextBox DescriptionBox = new();

    public CreateGroupDialog()
    {
        Title = "New Group";
        Width = 460;
        Height = 330;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = System.Windows.Media.Brushes.White;

        var root = new StackPanel { Margin = new Thickness(28) };
        root.Children.Add(new TextBlock { Text = "Create a new group", FontSize = 23, FontWeight = FontWeights.SemiBold });
        root.Children.Add(new TextBlock
        {
            Text = "Give your group a name and optional description.",
            Foreground = System.Windows.Media.Brushes.Gray,
            Margin = new Thickness(0, 5, 0, 14)
        });
        root.Children.Add(new TextBlock { Text = "Group name", FontWeight = FontWeights.SemiBold });
        root.Children.Add(NameBox);
        root.Children.Add(new TextBlock
        {
            Text = "Description",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 0)
        });
        root.Children.Add(DescriptionBox);

        var create = new Button
        {
            Content = "Create group",
            Width = 130,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        create.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(NameBox.Text))
            {
                DialogResult = true;
                Close();
            }
        };

        root.Children.Add(create);
        Content = root;
        NameBox.Focus();
    }
}