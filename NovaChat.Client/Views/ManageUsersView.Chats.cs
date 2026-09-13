using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views;

public partial class ManageUsersView
{
    private static bool _userChatsUiRegistered;

    static ManageUsersView()
    {
        if (_userChatsUiRegistered) return;
        _userChatsUiRegistered = true;
        EventManager.RegisterClassHandler(
            typeof(ManageUsersView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnManageUsersChatsLoaded));
    }

    private static void OnManageUsersChatsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not ManageUsersView view) return;
        view.InjectViewChatsButton();
    }

    private void InjectViewChatsButton()
    {
        if (ViewUserChatsButtonExists()) return;
        if (DeleteButton.Parent is not Grid detailsGrid) return;

        detailsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var button = new Button
        {
            Content = "View user's chats",
            Height = 42,
            Margin = new Thickness(0, 10, 0, 0),
            Style = (Style)FindResource("SecondaryButtonStyle"),
            ToolTip = "View all conversations that belong to the selected user"
        };
        button.Click += ViewUserChatsButton_Click;
        Grid.SetRow(button, detailsGrid.RowDefinitions.Count - 1);
        detailsGrid.Children.Add(button);
    }

    private bool ViewUserChatsButtonExists() =>
        DeleteButton.Parent is Grid grid &&
        grid.Children.OfType<Button>().Any(b => string.Equals(b.Content?.ToString(), "View user's chats", StringComparison.Ordinal));

    private async void ViewUserChatsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedUser == null) return;
        await ShowSelectedUserChatsAsync(_selectedUser.Id, _selectedUser.DisplayName, _selectedUser.Username);
    }

    private async Task ShowSelectedUserChatsAsync(string userId, string displayName, string username)
    {
        var window = new Window
        {
            Title = "User Chats",
            Width = 760,
            Height = 680,
            MinWidth = 640,
            MinHeight = 520,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ShowInTaskbar = false,
            Background = Brush("AppBackgroundBrush")
        };

        var root = new Grid { Margin = new Thickness(24) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new StackPanel();
        header.Children.Add(new TextBlock
        {
            Text = "Conversation history",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = Brush("TextBrush")
        });
        header.Children.Add(new TextBlock
        {
            Text = $"{displayName}  •  @{username}",
            FontSize = 12,
            Foreground = Brush("SecondaryTextBrush"),
            Margin = new Thickness(0, 4, 0, 0)
        });
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var status = new TextBlock
        {
            Text = "Loading conversations…",
            FontSize = 12,
            Foreground = Brush("SecondaryTextBrush"),
            Margin = new Thickness(0, 16, 0, 10)
        };
        Grid.SetRow(status, 1);
        root.Children.Add(status);

        var list = new ListBox
        {
            BorderThickness = new Thickness(0),
            Background = Brushes.Transparent,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(0)
        };
        Grid.SetRow(list, 2);
        root.Children.Add(list);

        var close = new Button
        {
            Content = "Close",
            Width = 100,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Right,
            Style = (Style)FindResource("SecondaryButtonStyle"),
            Margin = new Thickness(0, 12, 0, 0)
        };
        close.Click += (_, _) => window.Close();
        Grid.SetRow(close, 3);
        root.Children.Add(close);

        window.Content = root;

        try
        {
            var chats = await _apiService.GetAsync<List<OwnerUserChatModel>>($"api/OwnerUser/{Uri.EscapeDataString(userId)}/chats") ?? [];
            list.Items.Clear();

            foreach (var chat in chats)
            {
                var isGroup = string.Equals(chat.Type, "Group", StringComparison.OrdinalIgnoreCase);
                var title = isGroup
                    ? (string.IsNullOrWhiteSpace(chat.Name) ? "Unnamed group" : chat.Name)
                    : (string.IsNullOrWhiteSpace(chat.OtherDisplayName) ? $"@{chat.OtherUsername}" : chat.OtherDisplayName);
                var kind = isGroup ? "GROUP" : "PRIVATE CHAT";
                var last = chat.LastMessage == null
                    ? "No messages yet"
                    : $"Last activity: {chat.LastMessage.SentAt.ToLocalTime():MMM d, yyyy  HH:mm}";

                var card = new Border
                {
                    Background = Brush("PanelBackgroundBrush"),
                    BorderBrush = Brush("BorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(15),
                    Margin = new Thickness(0, 0, 0, 8)
                };

                var content = new Grid();
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var badge = new Border
                {
                    Width = 42,
                    Height = 42,
                    CornerRadius = new CornerRadius(13),
                    Background = Brush("PrimarySoftBrush")
                };
                badge.Child = new TextBlock
                {
                    Text = isGroup ? "G" : "P",
                    FontSize = 16,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brush("PrimaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                content.Children.Add(badge);

                var details = new StackPanel { Margin = new Thickness(11, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
                details.Children.Add(new TextBlock
                {
                    Text = title,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brush("TextBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                details.Children.Add(new TextBlock
                {
                    Text = $"{kind}  •  {chat.MemberCount} member{(chat.MemberCount == 1 ? "" : "s")}  •  {chat.MessageCount} message{(chat.MessageCount == 1 ? "" : "s")}",
                    FontSize = 11,
                    Foreground = Brush("SecondaryTextBrush"),
                    Margin = new Thickness(0, 3, 0, 0)
                });
                details.Children.Add(new TextBlock
                {
                    Text = last,
                    FontSize = 11,
                    Foreground = Brush("SecondaryTextBrush"),
                    Margin = new Thickness(0, 3, 0, 0)
                });
                Grid.SetColumn(details, 1);
                content.Children.Add(details);

                var messagesButton = new Button
                {
                    Content = "View messages",
                    Height = 34,
                    Padding = new Thickness(12, 0, 12, 0),
                    Style = (Style)FindResource("SecondaryButtonStyle"),
                    Tag = chat
                };
                messagesButton.Click += async (_, _) => await ShowSelectedUserChatMessagesAsync(window, list, chat);
                Grid.SetColumn(messagesButton, 2);
                content.Children.Add(messagesButton);

                card.Child = content;
                list.Items.Add(card);
            }

            status.Text = chats.Count == 0
                ? "This user has no active conversations."
                : $"{chats.Count} conversation{(chats.Count == 1 ? "" : "s")} found. Encrypted message contents remain protected from administrators.";
        }
        catch (Exception ex)
        {
            status.Text = "Could not load this user's conversations.";
            list.Items.Clear();
            ShowSafeError(window, "User Chats", ex);
        }

        window.ShowDialog();
    }

    private async Task ShowSelectedUserChatMessagesAsync(Window owner, ListBox list, OwnerUserChatModel chat)
    {
        try
        {
            if (_selectedUser == null) return;
            var messages = await _apiService.GetAsync<List<OwnerUserMessageModel>>($"api/OwnerUser/{Uri.EscapeDataString(_selectedUser.Id)}/chats/{chat.Id}/messages") ?? [];

            var dialog = new Window
            {
                Title = "Chat Messages",
                Width = 680,
                Height = 620,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner,
                ShowInTaskbar = false,
                Background = Brush("PanelBackgroundBrush")
            };

            var root = new DockPanel { Margin = new Thickness(22) };
            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            heading.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(chat.Name) ? "Private chat" : chat.Name,
                FontSize = 21,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("TextBrush")
            });
            heading.Children.Add(new TextBlock
            {
                Text = $"{messages.Count} message{(messages.Count == 1 ? "" : "s")}  •  Encrypted content",
                FontSize = 11,
                Foreground = Brush("SecondaryTextBrush"),
                Margin = new Thickness(0, 3, 0, 0)
            });
            DockPanel.SetDock(heading, Dock.Top);
            root.Children.Add(heading);

            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var panel = new StackPanel();
            foreach (var message in messages)
            {
                panel.Children.Add(new Border
                {
                    Background = Brush("InputBackgroundBrush"),
                    BorderBrush = Brush("BorderBrush"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12),
                    Margin = new Thickness(0, 0, 0, 7),
                    Child = new StackPanel
                    {
                        Children =
                        {
                            new TextBlock { Text = $"{message.SenderId}  •  {message.SentAt.ToLocalTime():MMM d, yyyy HH:mm}", FontSize = 11, Foreground = Brush("SecondaryTextBrush") },
                            new TextBlock { Text = message.Content, FontSize = 12, Foreground = Brush("TextBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 0) }
                        }
                    }
                });
            }
            if (messages.Count == 0)
                panel.Children.Add(new TextBlock { Text = "No messages found.", FontSize = 13, Foreground = Brush("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 30, 0, 0) });
            scroll.Content = panel;
            DockPanel.SetDock(scroll, Dock.Top);
            root.Children.Add(scroll);

            var close = new Button
            {
                Content = "Close",
                Width = 100,
                Height = 38,
                HorizontalAlignment = HorizontalAlignment.Right,
                Style = (Style)FindResource("SecondaryButtonStyle"),
                Margin = new Thickness(0, 12, 0, 0)
            };
            close.Click += (_, _) => dialog.Close();
            DockPanel.SetDock(close, Dock.Bottom);
            root.Children.Add(close);
            dialog.Content = root;
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowSafeError(owner, "Chat Messages", ex);
        }
    }

    private void ShowSafeError(Window owner, string title, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine($"{title}: {ex}");
        NovaChat.Client.MessageBox.Show(
            owner,
            "NovaChat could not load this information. The operation was stopped safely.",
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private Brush Brush(string key) =>
        TryFindResource(key) as Brush ?? Brushes.Gray;

    private sealed class OwnerUserChatModel
    {
        public int Id { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int MemberCount { get; set; }
        public int MessageCount { get; set; }
        public string OtherUserId { get; set; } = string.Empty;
        public string OtherUsername { get; set; } = string.Empty;
        public string OtherDisplayName { get; set; } = string.Empty;
        public string? OtherAvatarUrl { get; set; }
        public OwnerUserMessageModel? LastMessage { get; set; }
    }

    private sealed class OwnerUserMessageModel
    {
        public int Id { get; set; }
        public DateTime SentAt { get; set; }
        public string SenderId { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }
}
