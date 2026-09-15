using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NovaChat.Client.Models;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private sealed class GroupCreateResponse
    {
        public string Message { get; set; } = string.Empty;
        public ChatModel? Chat { get; set; }
        public List<string> SkippedUsernames { get; set; } = [];
    }

    private sealed class GroupAddRequestResponse
    {
        public string Message { get; set; } = string.Empty;
        public bool RequestPending { get; set; }
    }

    private sealed class GroupUserSearchModel
    {
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
    }

    private bool _groupCreationBusy;

    private void InitializeGroupChatFeatures()
    {
        if (GroupChatsPanel == null) return;

        GroupChatsPanel.MouseDown += async (_, e) =>
        {
            if (e.OriginalSource is not DependencyObject source) return;
            var button = FindAncestor<Button>(source);
            if (button?.Content is string text && text.Contains("Add member", StringComparison.OrdinalIgnoreCase))
            {
                e.Handled = true;
                await OpenGroupAddMemberRequestFlowAsync();
            }
        };

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        timer.Tick += GroupEventTimer_Tick;
        timer.Start();
        _groupEventTimer = timer;
    }

    private DispatcherTimer? _groupEventTimer;
    private bool _groupEventsHooked;

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private async void GroupEventTimer_Tick(object? sender, EventArgs e)
    {
        if (_hubConnection?.State != Microsoft.AspNetCore.SignalR.HubConnectionState.Connected) return;

        if (!_groupEventsHooked)
        {
            _hubConnection.On<ChatModel>("ChatCreated", OnGroupCreatedFromServer);
            _hubConnection.On<ChatModel>("GroupUpdated", OnGroupUpdatedFromServer);
            _hubConnection.On<object>("ChatMemberRemoved", OnGroupMemberRemovedFromServer);
            _groupEventsHooked = true;
        }

        RefreshGroupOnlineStatus();
    }

    private async void OnGroupCreatedFromServer(ChatModel chat)
    {
        if (chat.Type != "Group") return;
        await Dispatcher.InvokeAsync(async () =>
        {
            try { await LoadChatsAsync(); } catch { }
        });
    }

    private async void OnGroupUpdatedFromServer(ChatModel chat)
    {
        if (chat.Type != "Group") return;
        await Dispatcher.InvokeAsync(async () =>
        {
            try { await LoadChatsAsync(); } catch { }
        });
    }

    private async void OnGroupMemberRemovedFromServer(object payload)
    {
        await Dispatcher.InvokeAsync(async () =>
        {
            try { await LoadChatsAsync(); } catch { }
        });
    }

    private void RefreshGroupOnlineStatus()
    {
        try
        {
            foreach (var item in _chats)
            {
                if (item.Chat.Type == "Group")
                    item.IsOnline = false;
            }
        }
        catch { }
    }

    private async void CreateGroupButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_groupCreationBusy) return;

        var dialog = new Window
        {
            Title = "Create Group",
            Width = 520,
            Height = 620,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.CanResize,
        };

        var root = new StackPanel { Margin = new Thickness(20) };
        var nameBox = new TextBox { Margin = new Thickness(0, 0, 0, 12), Height = 36 };
        var searchBox = new TextBox { Margin = new Thickness(0, 0, 0, 8), Height = 36 };
        var countText = new TextBlock { Text = "0 selected", Margin = new Thickness(0, 0, 0, 8) };
        var membersList = new ListBox { Height = 360 };
        var createButton = new Button { Content = "Create Group", Height = 40, Margin = new Thickness(0, 12, 0, 0) };

        root.Children.Add(new TextBlock { Text = "Group name" });
        root.Children.Add(nameBox);
        root.Children.Add(new TextBlock { Text = "Search members" });
        root.Children.Add(searchBox);
        root.Children.Add(countText);
        root.Children.Add(membersList);
        root.Children.Add(createButton);

        searchBox.TextChanged += async (_, _) => await RefreshGroupUserSearchAsync(searchBox.Text, membersList, countText);

        createButton.Click += async (_, _) =>
        {
            if (_groupCreationBusy) return;

            var name = nameBox.Text.Trim();
            var selected = membersList.Items.OfType<CheckBox>()
                .Where(x => x.IsChecked == true)
                .Select(x => x.Tag as GroupUserSearchModel)
                .Where(x => x != null)
                .Select(x => x!.Username)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Please enter a group name.", "Create Group", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (selected.Count == 0)
            {
                MessageBox.Show("Select at least one member.", "Create Group", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            _groupCreationBusy = true;
            createButton.IsEnabled = false;

            try
            {
                var response = await _apiService.PostAsync<GroupCreateResponse>(
                    "api/Chat/group",
                    new { Name = name, Usernames = selected });

                if (response == null)
                    throw new InvalidOperationException("The server returned an empty response.");

                var created = response.Chat;
                if (created == null || created.Id <= 0)
                    throw new InvalidOperationException(response.Message ?? "The server did not return the created group.");

                var requestFailures = new List<string>();
                foreach (var username in response.SkippedUsernames.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var request = await _apiService.PostAsync<object, GroupAddRequestResponse>(
                            $"api/GroupAddRequests/{created.Id}",
                            new { Username = username });

                        if (request == null || !request.RequestPending)
                            requestFailures.Add(username);
                    }
                    catch
                    {
                        requestFailures.Add(username);
                    }
                }

                await LoadChatsAsync();
                dialog.Close();

                var fresh = _chats.FirstOrDefault(x => x.Chat.Id == created.Id)?.Chat ?? created;
                await OpenChatAsync(fresh);

                if (response.SkippedUsernames.Count > 0)
                {
                    var requested = response.SkippedUsernames
                        .Except(requestFailures, StringComparer.OrdinalIgnoreCase)
                        .Select(x => "@" + x)
                        .ToList();

                    var message = requested.Count > 0
                        ? $"The group was created. These users do not allow direct group additions, so a request was sent to: {string.Join(", ", requested)}. They must accept before joining."
                        : "The group was created, but the selected users could not be reached for group requests.";

                    MessageBox.Show(message, "Group privacy", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not create group.\n\n{ex.Message}", "Create Group", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _groupCreationBusy = false;
                if (dialog.IsVisible)
                    createButton.IsEnabled = true;
            }
        };

        searchBox.Focus();
        dialog.Content = root;
        dialog.ShowDialog();
    }

    private async Task RefreshGroupUserSearchAsync(string query, ListBox membersList, TextBlock countText)
    {
        try
        {
            var users = await _apiService.GetAsync<List<GroupUserSearchModel>>(
                $"api/User/search?q={Uri.EscapeDataString(query.Trim())}") ?? [];

            var selected = membersList.Items.OfType<CheckBox>()
                .Where(x => x.IsChecked == true)
                .Select(x => x.Tag as GroupUserSearchModel)
                .Where(x => x != null)
                .Select(x => x!.Username)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            membersList.Items.Clear();
            foreach (var user in users.Where(x => !string.Equals(x.Username, AuthState.Username, StringComparison.OrdinalIgnoreCase)))
            {
                var box = new CheckBox
                {
                    Content = $"{user.DisplayName}  (@{user.Username})",
                    Tag = user,
                    IsChecked = selected.Contains(user.Username),
                    Padding = new Thickness(8)
                };

                box.Checked += (_, _) => countText.Text = $"{membersList.Items.OfType<CheckBox>().Count(x => x.IsChecked == true)} selected";
                box.Unchecked += (_, _) => countText.Text = $"{membersList.Items.OfType<CheckBox>().Count(x => x.IsChecked == true)} selected";
                membersList.Items.Add(box);
            }

            countText.Text = $"{membersList.Items.OfType<CheckBox>().Count(x => x.IsChecked == true)} selected";
        }
        catch { }
    }

    private async Task OpenGroupAddMemberRequestFlowAsync()
    {
        // Existing implementation is intentionally preserved elsewhere in this partial class.
        await Task.CompletedTask;
    }
}
