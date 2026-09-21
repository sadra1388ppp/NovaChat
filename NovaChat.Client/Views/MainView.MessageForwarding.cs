using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private async void ForwardMessageMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem item || item.Tag is not MessageBubbleInfo info || !_currentChatId.HasValue)
            return;

        try
        {
            var message = await GetMessageByIdAsync(_currentChatId.Value, info.MessageId);
            if (message == null)
            {
                MessageBox.Show("The message could not be located.", "Forward Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (message.IsDeletedForEveryone)
            {
                MessageBox.Show("Deleted messages cannot be forwarded.", "Forward Message", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (!string.Equals(message.MessageType, "text", StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show(
                    "Forwarding currently supports text messages. Media forwarding will be added separately so encrypted files can be re-encrypted safely for each destination.",
                    "Forward Message",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var decrypted = await DecryptMessageWithRecoveryAsync(message);
            if (string.IsNullOrWhiteSpace(decrypted.Content) ||
                decrypted.Content.StartsWith("[Encrypted message", StringComparison.Ordinal))
            {
                MessageBox.Show("This message could not be decrypted on this device.", "Forward Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selection = ShowForwardDestinationDialog(message, decrypted.Content);
            if (selection == null || selection.Destinations.Count == 0)
                return;

            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
            {
                MessageBox.Show("NovaChat is not connected to the server.", "Forward Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var forwardedText = BuildForwardedMessage(message, decrypted.Content, selection.Comment);

            var successCount = 0;
            var failedDestinations = new List<string>();

            foreach (var destination in selection.Destinations)
            {
                try
                {
                    var encrypted = await _e2ee.EncryptForChatAsync(
                        destination.Chat.Id,
                        forwardedText,
                        _apiService);

                    await _hubConnection.InvokeAsync(
                        "SendMessage",
                        destination.Chat.Id,
                        encrypted);

                    successCount++;
                }
                catch (Exception exception)
                {
                    failedDestinations.Add(destination.DisplayName);
                    System.Diagnostics.Debug.WriteLine(
                        $"Forwarding failed for chat {destination.Chat.Id}: {exception}");
                }
            }

            await LoadChatsAsync();
            System.Media.SystemSounds.Asterisk.Play();

            if (failedDestinations.Count == 0)
            {
                if (successCount == 1)
                {
                    return;
                }

                MessageBox.Show(
                    $"Message forwarded to {successCount} conversations.",
                    "Forward Message",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"Forwarded to {successCount} of {selection.Destinations.Count} conversations.\n\nFailed:\n• {string.Join("\n• ", failedDestinations)}",
                    "Forward Message",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not forward the message.\n\n{ex.Message}",
                "Forward Message",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private static string BuildForwardedMessage(
        MessageModel sourceMessage,
        string decryptedContent,
        string comment)
    {
        var header = $"↪ Forwarded from @{sourceMessage.SenderId}";
        var body = $"{header}\n\n{decryptedContent.Trim()}";

        return string.IsNullOrWhiteSpace(comment)
            ? body
            : $"{comment.Trim()}\n\n{body}";
    }

    private ForwardSelectionResult? ShowForwardDestinationDialog(
        MessageModel sourceMessage,
        string decryptedContent)
    {
        var destinations = _chats
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new ForwardDestinationItem(x))
            .ToList();

        if (destinations.Count == 0)
        {
            MessageBox.Show(
                "There are no conversations available to receive this message.",
                "Forward Message",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return null;
        }

        var selectedIds = new HashSet<int>();
        var dialog = new Window
        {
            Title = "Forward Message",
            Width = 620,
            Height = 760,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            MinWidth = 560,
            MinHeight = 650,
            ShowInTaskbar = false,
            Background = FindBrush("AppBackgroundBrush")
        };

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var titleRow = new Grid();
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new Border
        {
            Width = 48,
            Height = 48,
            CornerRadius = new CornerRadius(16),
            Background = FindBrush("PrimarySoftBrush")
        };
        icon.Child = new TextBlock
        {
            Text = "↗",
            FontSize = 24,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("PrimaryBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var titlePanel = new StackPanel
        {
            Margin = new Thickness(13, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Forward Message",
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = FindBrush("TextBrush")
        });
        titlePanel.Children.Add(new TextBlock
        {
            Text = "Select one or more conversations",
            FontSize = 11,
            Foreground = FindBrush("SecondaryTextBrush"),
            Margin = new Thickness(0, 3, 0, 0)
        });

        titleRow.Children.Add(icon);
        Grid.SetColumn(titlePanel, 1);
        titleRow.Children.Add(titlePanel);
        root.Children.Add(titleRow);

        var previewCard = new Border
        {
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(14, 12, 14, 12),
            CornerRadius = new CornerRadius(13),
            Background = FindBrush("PanelBackgroundBrush"),
            BorderBrush = FindBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            MaxHeight = 120
        };

        var previewStack = new StackPanel();
        previewStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(sourceMessage.SenderId)
                ? "Forwarded message"
                : $"Forwarded from @{sourceMessage.SenderId}",
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = FindBrush("PrimaryBrush")
        });

        previewStack.Children.Add(new TextBlock
        {
            Text = decryptedContent.Trim(),
            FontSize = 13,
            Foreground = FindBrush("TextBrush"),
            TextWrapping = TextWrapping.Wrap,
            MaxHeight = 58,
            Margin = new Thickness(0, 6, 0, 0)
        });

        previewStack.Children.Add(new TextBlock
        {
            Text = "This message will be re-encrypted separately for every selected conversation.",
            FontSize = 10,
            Foreground = FindBrush("SecondaryTextBrush"),
            Margin = new Thickness(0, 8, 0, 0)
        });

        previewCard.Child = previewStack;
        Grid.SetRow(previewCard, 1);
        root.Children.Add(previewCard);

        var search = new TextBox
        {
            Height = 42,
            Margin = new Thickness(0, 14, 0, 8),
            Padding = new Thickness(14, 0, 12, 0),
            Text = string.Empty,
            ToolTip = "Search conversations"
        };

        var searchContainer = new Border
        {
            Height = 42,
            CornerRadius = new CornerRadius(12),
            Background = FindBrush("InputBackgroundBrush"),
            BorderBrush = FindBrush("BorderBrush"),
            BorderThickness = new Thickness(1)
        };

        var searchGrid = new Grid();
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        searchGrid.Children.Add(new TextBlock
        {
            Text = "⌕",
            FontSize = 19,
            Foreground = FindBrush("SecondaryTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        search.Margin = new Thickness(0);
        search.Background = Brushes.Transparent;
        search.BorderThickness = new Thickness(0);
        Grid.SetColumn(search, 1);
        searchGrid.Children.Add(search);
        searchContainer.Child = searchGrid;

        Grid.SetRow(searchContainer, 2);
        root.Children.Add(searchContainer);

        var listBorder = new Border
        {
            CornerRadius = new CornerRadius(13),
            Background = FindBrush("PanelBackgroundBrush"),
            BorderBrush = FindBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(5)
        };

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        var recipientsPanel = new StackPanel();
        scroll.Content = recipientsPanel;
        listBorder.Child = scroll;
        Grid.SetRow(listBorder, 3);
        root.Children.Add(listBorder);

        var selectedSummary = new TextBlock
        {
            Text = "0 conversations selected",
            FontSize = 11,
            Foreground = FindBrush("SecondaryTextBrush"),
            Margin = new Thickness(2, 9, 2, 8)
        };
        Grid.SetRow(selectedSummary, 4);
        root.Children.Add(selectedSummary);

        var commentBox = new TextBox
        {
            Height = 58,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalContentAlignment = VerticalAlignment.Top,
            Padding = new Thickness(12),
            ToolTip = "Optional comment to send with the forwarded message"
        };

        var commentContainer = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = FindBrush("InputBackgroundBrush"),
            BorderBrush = FindBrush("BorderBrush"),
            BorderThickness = new Thickness(1),
            Child = commentBox
        };

        var commentLabel = new TextBlock
        {
            Text = "Add a comment (optional)",
            FontSize = 10,
            Foreground = FindBrush("SecondaryTextBrush"),
            Margin = new Thickness(2, 0, 2, 6)
        };

        var commentStack = new StackPanel();
        commentStack.Children.Add(commentLabel);
        commentStack.Children.Add(commentContainer);
        Grid.SetRow(commentStack, 5);
        root.Children.Add(commentStack);

        var actions = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var status = new TextBlock
        {
            Text = "Choose at least one conversation.",
            FontSize = 11,
            Foreground = FindBrush("SecondaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal };

        var cancel = new Button
        {
            Content = "Cancel",
            Width = 96,
            Height = 40,
            Margin = new Thickness(0, 0, 9, 0),
            Style = FindStyle("SecondaryButtonStyle")
        };

        var forward = new Button
        {
            Content = "Forward",
            Width = 125,
            Height = 40,
            IsEnabled = false,
            Style = FindStyle("PrimaryButtonStyle")
        };

        buttons.Children.Add(cancel);
        buttons.Children.Add(forward);

        Grid.SetColumn(status, 0);
        actions.Children.Add(status);
        Grid.SetColumn(buttons, 1);
        actions.Children.Add(buttons);
        Grid.SetRow(actions, 6);
        root.Children.Add(actions);

        void UpdateSelectionUi()
        {
            var count = selectedIds.Count;
            selectedSummary.Text = count == 0
                ? "0 conversations selected"
                : $"{count} conversation{(count == 1 ? "" : "s")} selected";

            forward.Content = count == 0 ? "Forward" : $"Forward  •  {count}";
            forward.IsEnabled = count > 0;

            status.Text = count == 0
                ? "Choose at least one conversation."
                : "Ready to forward securely.";
        }

        void RenderRecipients()
        {
            recipientsPanel.Children.Clear();

            var query = search.Text.Trim();
            var visible = string.IsNullOrWhiteSpace(query)
                ? destinations
                : destinations.Where(x =>
                    x.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.Chat.Type.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    x.OtherUserId.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            if (visible.Count == 0)
            {
                recipientsPanel.Children.Add(new TextBlock
                {
                    Text = "No conversations match your search.",
                    FontSize = 12,
                    Foreground = FindBrush("SecondaryTextBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(20)
                });
                return;
            }

            foreach (var destination in visible)
            {
                var selectionIndicator = new Border
                {
                    Width = 22,
                    Height = 22,
                    CornerRadius = new CornerRadius(11),
                    BorderThickness = new Thickness(1.5),
                    BorderBrush = FindBrush("BorderBrush"),
                    Background = selectedIds.Contains(destination.Chat.Id)
                        ? FindBrush("PrimaryBrush")
                        : Brushes.Transparent,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var selectionText = new TextBlock
                {
                    Text = "✓",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = selectedIds.Contains(destination.Chat.Id)
                        ? Visibility.Visible
                        : Visibility.Collapsed
                };
                selectionIndicator.Child = selectionText;

                var avatar = new Border
                {
                    Width = 40,
                    Height = 40,
                    CornerRadius = new CornerRadius(14),
                    Background = FindBrush("PrimarySoftBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(10, 0, 11, 0)
                };
                avatar.Child = new TextBlock
                {
                    Text = destination.Initials,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = FindBrush("PrimaryBrush"),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(new TextBlock
                {
                    Text = destination.DisplayName,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = FindBrush("TextBrush"),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                info.Children.Add(new TextBlock
                {
                    Text = destination.Chat.IsGroup
                        ? $"Group  •  {destination.Chat.Name}"
                        : $"Private chat  •  @{destination.OtherUserId}",
                    FontSize = 10,
                    Foreground = FindBrush("SecondaryTextBrush"),
                    Margin = new Thickness(0, 3, 0, 0),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });

                var rowGrid = new Grid { MinHeight = 58 };
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                rowGrid.Children.Add(selectionIndicator);
                Grid.SetColumn(avatar, 1);
                rowGrid.Children.Add(avatar);
                Grid.SetColumn(info, 2);
                rowGrid.Children.Add(info);

                var row = new Border
                {
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 1, 0, 1),
                    CornerRadius = new CornerRadius(11),
                    Background = selectedIds.Contains(destination.Chat.Id)
                        ? FindBrush("PrimarySoftBrush")
                        : Brushes.Transparent,
                    Child = rowGrid
                };

                void SetSelected(bool value)
                {
                    if (value)
                        selectedIds.Add(destination.Chat.Id);
                    else
                        selectedIds.Remove(destination.Chat.Id);

                    row.Background = value
                        ? FindBrush("PrimarySoftBrush")
                        : Brushes.Transparent;

                    selectionIndicator.Background = value
                        ? FindBrush("PrimaryBrush")
                        : Brushes.Transparent;
                    selectionIndicator.BorderBrush = value
                        ? FindBrush("PrimaryBrush")
                        : FindBrush("BorderBrush");
                    selectionText.Visibility = value
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                    UpdateSelectionUi();
                }

                row.MouseLeftButtonUp += (_, args) =>
                {
                    SetSelected(!selectedIds.Contains(destination.Chat.Id));
                    args.Handled = true;
                };

                recipientsPanel.Children.Add(row);
            }
        }

        search.TextChanged += (_, _) => RenderRecipients();
        cancel.Click += (_, _) => dialog.DialogResult = false;
        forward.Click += (_, _) =>
        {
            if (selectedIds.Count > 0)
                dialog.DialogResult = true;
        };

        dialog.Content = root;
        dialog.Loaded += (_, _) =>
        {
            search.Focus();
            RenderRecipients();
            UpdateSelectionUi();
        };

        if (dialog.ShowDialog() != true)
            return null;

        var selected = destinations
            .Where(x => selectedIds.Contains(x.Chat.Id))
            .Select(x => x.Chat)
            .ToList();

        return new ForwardSelectionResult(selected, commentBox.Text.Trim());
    }

    private Style? FindStyle(string key) => FindResource(key) as Style;

    private Brush FindBrush(string key) => FindResource(key) as Brush ?? Brushes.Gray;

    private sealed class ForwardDestinationItem
    {
        public ChatListItem Chat { get; }

        public string DisplayName => string.IsNullOrWhiteSpace(Chat.DisplayName)
            ? (Chat.Chat.IsGroup ? Chat.Chat.Name : "Unknown conversation")
            : Chat.DisplayName;

        public string OtherUserId => Chat.Chat.OtherUserId(AuthState.UserId);

        public string Initials
        {
            get
            {
                var value = DisplayName.Trim();
                if (string.IsNullOrWhiteSpace(value)) return "?";
                var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2
                    ? $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant()
                    : value[..Math.Min(2, value.Length)].ToUpperInvariant();
            }
        }

        public ForwardDestinationItem(ChatListItem chat) => Chat = chat;
    }

    private sealed record ForwardSelectionResult(
        List<ChatListItem> Destinations,
        string Comment);
}
