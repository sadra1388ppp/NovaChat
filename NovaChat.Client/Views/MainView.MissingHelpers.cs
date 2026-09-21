using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private void AddMessageToUi(MessageModel message, bool insertAtTop = false)
    {
        var mine = string.Equals(message.SenderId, AuthState.Username, StringComparison.OrdinalIgnoreCase);
        var isGroup = _currentChatId.HasValue && _chats.FirstOrDefault(x => x.Chat.Id == _currentChatId.Value)?.Chat.IsGroup == true;
        var border = new Border
        {
            Tag = message.Id,
            Background = mine ? (Brush)FindResource("PrimaryBrush") : (Brush)FindResource("PanelBackgroundBrush"),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 450,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var panel = new StackPanel();
        if (isGroup)
        {
            panel.Children.Add(new TextBlock
            {
                Text = mine ? "You" : (string.IsNullOrWhiteSpace(message.SenderName) ? message.SenderId : message.SenderName),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = mine ? Brushes.White : (Brush)FindResource("PrimaryBrush"),
                Margin = new Thickness(0, 0, 0, 5),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
        }

        if (TryParseForwardedMessage(message.Content, out var forwarded))
        {
            AddForwardedMessageContent(panel, forwarded, mine);
        }
        else
        {
            panel.Children.Add(new TextBlock
            {
                Tag = "message-content",
                Text = message.Content,
                TextWrapping = TextWrapping.Wrap,
                Foreground = mine ? Brushes.White : (Brush)FindResource("TextBrush")
            });
        }

        var meta = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 5, 0, 0)
        };
        meta.Children.Add(new TextBlock
        {
            Text = message.SentAt.ToLocalTime().ToString("HH:mm"),
            FontSize = 10,
            Foreground = mine ? Brushes.White : (Brush)FindResource("SecondaryTextBrush")
        });
        if (message.IsEdited)
        {
            meta.Children.Add(new TextBlock
            {
                Tag = "edited",
                Text = "edited",
                FontSize = 9,
                Margin = new Thickness(6, 0, 0, 0),
                Foreground = mine ? Brushes.White : (Brush)FindResource("SecondaryTextBrush")
            });
        }

        if (mine)
        {
            meta.Children.Add(new TextBlock
            {
                Tag = "receipt",
                Text = "✓",
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(5, 0, 0, 0),
                Foreground = Brushes.White
            });
        }

        panel.Children.Add(meta);
        border.Child = panel;

        if (insertAtTop)
            MessagesPanel.Children.Insert(Math.Min(1, MessagesPanel.Children.Count), border);
        else
            MessagesPanel.Children.Add(border);

        if (message.MessageType is "image" or "file" or "voice")
        {
            _ = Dispatcher.InvokeAsync(
                async () => await RenderMediaBubbleFromMessageAsync(border, message),
                System.Windows.Threading.DispatcherPriority.Loaded);
        }

        if (mine)
            Dispatcher.InvokeAsync(UpdateMessageReceiptsUi, System.Windows.Threading.DispatcherPriority.Loaded);
    }


    private static bool TryParseForwardedMessage(
        string? content,
        out (string Comment, string Sender, string Message) forwarded)
    {
        forwarded = default;

        if (string.IsNullOrWhiteSpace(content))
            return false;

        const string marker = "↪ Forwarded from ";
        var markerIndex = content.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return false;

        var headerEnd = content.IndexOf("\n\n", markerIndex, StringComparison.Ordinal);
        if (headerEnd < 0)
            return false;

        var header = content[markerIndex..headerEnd].Trim();
        var sender = header[marker.Length..].Trim();
        if (string.IsNullOrWhiteSpace(sender))
            return false;

        var messageStart = headerEnd + 2;
        var forwardedText = content[messageStart..].Trim();
        if (string.IsNullOrWhiteSpace(forwardedText))
            return false;

        var comment = content[..markerIndex].Trim();

        forwarded = (comment, sender, forwardedText);
        return true;
    }

    private void AddForwardedMessageContent(
        Panel parent,
        (string Comment, string Sender, string Message) forwarded,
        bool mine)
    {
        if (!string.IsNullOrWhiteSpace(forwarded.Comment))
        {
            parent.Children.Add(new TextBlock
            {
                Tag = "forward-comment",
                Text = forwarded.Comment,
                TextWrapping = TextWrapping.Wrap,
                Foreground = mine ? Brushes.White : (Brush)FindResource("TextBrush"),
                Margin = new Thickness(0, 0, 0, 8)
            });
        }

        var contentGrid = new Grid();

        var accent = new Border
        {
            Width = 3,
            CornerRadius = new CornerRadius(2),
            Background = mine
                ? Brushes.White
                : (Brush)FindResource("PrimaryBrush"),
            Opacity = mine ? 0.9 : 1,
            Margin = new Thickness(0, 1, 10, 1),
            VerticalAlignment = VerticalAlignment.Stretch
        };

        var forwardedStack = new StackPanel();

        var senderRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        senderRow.Children.Add(new TextBlock
        {
            Text = "↗",
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = mine
                ? Brushes.White
                : (Brush)FindResource("PrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        });

        senderRow.Children.Add(new TextBlock
        {
            Text = "Forwarded from ",
            FontSize = 10,
            Foreground = mine
                ? Brushes.White
                : (Brush)FindResource("SecondaryTextBrush"),
            VerticalAlignment = VerticalAlignment.Center
        });

        senderRow.Children.Add(new TextBlock
        {
            Text = forwarded.Sender,
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            Foreground = mine
                ? Brushes.White
                : (Brush)FindResource("PrimaryBrush"),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 220
        });

        forwardedStack.Children.Add(senderRow);

        forwardedStack.Children.Add(new TextBlock
        {
            Tag = "message-content",
            Text = forwarded.Message,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            Foreground = mine
                ? Brushes.White
                : (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 3, 0, 0)
        });

        Grid.SetColumn(accent, 0);
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        contentGrid.Children.Add(accent);

        Grid.SetColumn(forwardedStack, 1);
        contentGrid.Children.Add(forwardedStack);

        parent.Children.Add(contentGrid);
    }

    private async Task ScrollMessagesToBottomAsync()
    {
        await Task.Delay(50);
        await Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async Task RefreshCurrentUserAvatarAsync()
    {
        if (string.IsNullOrWhiteSpace(_currentOtherUserId))
        {
            ChatHeaderAvatarImage.Source = null;
            ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
            ChatAvatarInitialsText.Visibility = Visibility.Visible;
            return;
        }

        try
        {
            var profile = await _apiService.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(_currentOtherUserId)}");
            if (profile == null) return;

            ChatUserNameText.Text = profile.DisplayName;
            if (string.IsNullOrWhiteSpace(profile.AvatarUrl))
            {
                ChatHeaderAvatarImage.Source = null;
                ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
                ChatAvatarInitialsText.Text = GetInitials(profile.DisplayName);
                ChatAvatarInitialsText.Visibility = Visibility.Visible;
                return;
            }

            var avatar = await LoadConversationAvatarAsync(_apiService.BuildAbsoluteUrl(profile.AvatarUrl));
            if (avatar != null)
            {
                ChatHeaderAvatarImage.Source = avatar;
                ChatHeaderAvatarImage.Visibility = Visibility.Visible;
                ChatAvatarInitialsText.Visibility = Visibility.Collapsed;
            }
            else
            {
                ChatHeaderAvatarImage.Source = null;
                ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
                ChatAvatarInitialsText.Text = GetInitials(profile.DisplayName);
                ChatAvatarInitialsText.Visibility = Visibility.Visible;
            }
        }
        catch { }
    }

    private static string GetInitials(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "?";
        var parts = displayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
        return string.Concat(parts[0][0], parts[^1][0]).ToUpperInvariant();
    }

    private void ProfileButton_Click(object sender, RoutedEventArgs e) => ProfileRequested?.Invoke();
    private void SettingsButton_Click(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}
