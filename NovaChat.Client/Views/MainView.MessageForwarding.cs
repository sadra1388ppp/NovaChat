using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private MessageModel? _forwardMessage;
    private string _forwardMessageContent = string.Empty;
    private readonly HashSet<int> _forwardRecipientIds = [];
    private bool _forwardRecipientPickerOpen;

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

            StartForwardMode(message, decrypted.Content);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Could not prepare the message for forwarding.\n\n{ex.Message}",
                "Forward Message",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void StartForwardMode(MessageModel message, string decryptedContent)
    {
        _forwardMessage = message;
        _forwardMessageContent = decryptedContent.Trim();
        _forwardRecipientIds.Clear();
        _forwardRecipientPickerOpen = true;

        ForwardPanel.Visibility = Visibility.Visible;
        ForwardRecipientPickerPanel.Visibility = Visibility.Visible;
        MessageTextBox.Clear();
        MessageTextBox.ToolTip = "Add a comment (optional)";
        SendButton.Content = "Forward  ➤";

        ForwardSourceText.Text = string.IsNullOrWhiteSpace(message.SenderId)
            ? "Forwarded message"
            : $"@{message.SenderId}";

        ForwardSourceContentText.Text = _forwardMessageContent;
        ForwardModeHintText.Text = "Choose one or more conversations";
        ForwardRecipientCountText.Text = "0 selected";

        RenderForwardRecipientChips();
        RenderForwardRecipientPicker();
        UpdateForwardUi();

        Dispatcher.BeginInvoke(() =>
        {
            MessageTextBox.Focus();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void CancelForwardMode()
    {
        _forwardMessage = null;
        _forwardMessageContent = string.Empty;
        _forwardRecipientIds.Clear();
        _forwardRecipientPickerOpen = false;

        ForwardPanel.Visibility = Visibility.Collapsed;
        ForwardRecipientPickerPanel.Visibility = Visibility.Collapsed;
        MessageTextBox.Clear();
        MessageTextBox.ToolTip = "Write a message";
        SendButton.Content = "Send  ➤";

        ForwardRecipientPanelToggleButton.Content = "＋ Add recipients";
        ForwardModeHintText.Text = string.Empty;
        ForwardRecipientCountText.Text = "0 selected";
        ForwardRecipientsPanel.Children.Clear();
        ForwardRecipientPickerListPanel.Children.Clear();
        ForwardRecipientSearchTextBox.Clear();
    }

    private void ForwardCancelButton_Click(object sender, RoutedEventArgs e) => CancelForwardMode();

    private void ForwardRecipientPanelToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_forwardMessage == null)
            return;

        _forwardRecipientPickerOpen = !_forwardRecipientPickerOpen;
        ForwardRecipientPickerPanel.Visibility = _forwardRecipientPickerOpen
            ? Visibility.Visible
            : Visibility.Collapsed;

        ForwardRecipientPanelToggleButton.Content = _forwardRecipientPickerOpen
            ? "− Hide recipients"
            : "＋ Add recipients";

        if (_forwardRecipientPickerOpen)
        {
            RenderForwardRecipientPicker();
            ForwardRecipientSearchTextBox.Focus();
        }
    }

    private async Task ForwardPendingMessageAsync()
    {
        if (_forwardMessage == null || string.IsNullOrWhiteSpace(_forwardMessageContent))
            return;

        if (_forwardRecipientIds.Count == 0)
        {
            ForwardModeHintText.Text = "Select at least one conversation.";
            return;
        }

        if (_hubConnection?.State != HubConnectionState.Connected)
        {
            MessageBox.Show(
                "NovaChat is not connected to the server.",
                "Forward Message",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var destinations = _chats
            .Where(x => _forwardRecipientIds.Contains(x.Chat.Id))
            .ToList();

        if (destinations.Count == 0)
        {
            ForwardModeHintText.Text = "No valid recipient is selected.";
            UpdateForwardUi();
            return;
        }

        var comment = MessageTextBox.Text.Trim();
        var forwardedText = BuildForwardedMessage(_forwardMessage, _forwardMessageContent, comment);

        SendButton.IsEnabled = false;
        ForwardRecipientPanelToggleButton.IsEnabled = false;
        ForwardCancelButton.IsEnabled = false;
        ForwardModeHintText.Text = "Forwarding securely...";

        var successCount = 0;
        var failedDestinations = new List<string>();

        try
        {
            foreach (var destination in destinations)
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
                CancelForwardMode();

                if (successCount > 1)
                {
                    MessageBox.Show(
                        $"Message forwarded to {successCount} conversations.",
                        "Forward Message",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            else
            {
                ForwardRecipientPanelToggleButton.IsEnabled = true;
                ForwardCancelButton.IsEnabled = true;
                SendButton.IsEnabled = true;
                ForwardModeHintText.Text =
                    $"Forwarded to {successCount} of {destinations.Count}. Some conversations could not be reached.";
            }
        }
        catch (Exception ex)
        {
            ForwardRecipientPanelToggleButton.IsEnabled = true;
            ForwardCancelButton.IsEnabled = true;
            SendButton.IsEnabled = true;
            ForwardModeHintText.Text = "Forwarding failed. Please try again.";
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
        var senderName = string.IsNullOrWhiteSpace(sourceMessage.SenderId)
            ? "Unknown user"
            : $"@{sourceMessage.SenderId}";

        var forwarded = $"↪ Forwarded from {senderName}\n\n{decryptedContent.Trim()}";

        return string.IsNullOrWhiteSpace(comment)
            ? forwarded
            : $"{comment.Trim()}\n\n{forwarded}";
    }

    private void RenderForwardRecipientChips()
    {
        ForwardRecipientsPanel.Children.Clear();

        foreach (var chat in _chats
                     .Where(x => _forwardRecipientIds.Contains(x.Chat.Id))
                     .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            var chip = new Border
            {
                Background = FindBrush("PrimarySoftBrush"),
                BorderBrush = FindBrush("BorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(13),
                Margin = new Thickness(0, 0, 7, 0),
                Padding = new Thickness(9, 5, 6, 5)
            };

            var chipGrid = new Grid();
            chipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            chipGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var name = new TextBlock
            {
                Text = chat.DisplayName,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                MaxWidth = 125,
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var remove = new Button
            {
                Content = "×",
                Width = 22,
                Height = 22,
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(0),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = FindBrush("SecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "Remove recipient"
            };

            var chatId = chat.Chat.Id;
            remove.Click += (_, _) =>
            {
                _forwardRecipientIds.Remove(chatId);
                RenderForwardRecipientChips();
                RenderForwardRecipientPicker();
                UpdateForwardUi();
            };

            chipGrid.Children.Add(name);
            Grid.SetColumn(remove, 1);
            chipGrid.Children.Add(remove);
            chip.Child = chipGrid;

            ForwardRecipientsPanel.Children.Add(chip);
        }

        if (_forwardRecipientIds.Count == 0)
        {
            ForwardRecipientsPanel.Children.Add(new TextBlock
            {
                Text = "No recipients selected",
                FontSize = 11,
                Foreground = FindBrush("SecondaryTextBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(4, 2, 0, 2)
            });
        }
    }

    private void RenderForwardRecipientPicker()
    {
        ForwardRecipientPickerListPanel.Children.Clear();

        var query = ForwardRecipientSearchTextBox.Text.Trim();

        var visible = _chats
            .Where(x =>
                string.IsNullOrWhiteSpace(query) ||
                x.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.Chat.Type.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                x.OtherUserId.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (visible.Count == 0)
        {
            ForwardRecipientPickerListPanel.Children.Add(new TextBlock
            {
                Text = "No conversations found.",
                FontSize = 11,
                Foreground = FindBrush("SecondaryTextBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(15)
            });
            return;
        }

        foreach (var chat in visible)
        {
            var isSelected = _forwardRecipientIds.Contains(chat.Chat.Id);

            var avatar = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(11),
                Background = FindBrush("PrimarySoftBrush"),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 9, 0)
            };
            avatar.Child = new TextBlock
            {
                Text = chat.Initials,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = FindBrush("PrimaryBrush"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var details = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            details.Children.Add(new TextBlock
            {
                Text = chat.DisplayName,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindBrush("TextBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            details.Children.Add(new TextBlock
            {
                Text = chat.Chat.IsGroup
                    ? $"Group  •  {chat.Chat.Name}"
                    : $"@{chat.OtherUserId}",
                FontSize = 10,
                Foreground = FindBrush("SecondaryTextBrush"),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            var selection = new Border
            {
                Width = 22,
                Height = 22,
                CornerRadius = new CornerRadius(11),
                BorderThickness = new Thickness(1),
                BorderBrush = isSelected ? FindBrush("PrimaryBrush") : FindBrush("BorderBrush"),
                Background = isSelected ? FindBrush("PrimaryBrush") : Brushes.Transparent,
                VerticalAlignment = VerticalAlignment.Center
            };
            selection.Child = new TextBlock
            {
                Text = "✓",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed
            };

            var rowGrid = new Grid { MinHeight = 50 };
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rowGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            rowGrid.Children.Add(avatar);
            Grid.SetColumn(avatar, 0);
            rowGrid.Children.Add(details);
            Grid.SetColumn(details, 1);
            rowGrid.Children.Add(selection);
            Grid.SetColumn(selection, 3);

            var row = new Border
            {
                Background = isSelected ? FindBrush("PrimarySoftBrush") : Brushes.Transparent,
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(5, 1, 7, 1),
                Margin = new Thickness(1),
                Child = rowGrid
            };

            row.MouseLeftButtonUp += (_, args) =>
            {
                var id = chat.Chat.Id;
                if (_forwardRecipientIds.Contains(id))
                    _forwardRecipientIds.Remove(id);
                else
                    _forwardRecipientIds.Add(id);

                RenderForwardRecipientChips();
                RenderForwardRecipientPicker();
                UpdateForwardUi();
                args.Handled = true;
            };

            ForwardRecipientPickerListPanel.Children.Add(row);
        }
    }

    private void UpdateForwardUi()
    {
        var count = _forwardRecipientIds.Count;

        ForwardRecipientCountText.Text = count == 0
            ? "0 selected"
            : $"{count} selected";

        ForwardModeHintText.Text = count == 0
            ? "Choose one or more conversations"
            : $"Ready to forward to {count} conversation{(count == 1 ? string.Empty : "s")}.";

        SendButton.IsEnabled = count > 0 &&
                               _forwardMessage != null &&
                               _hubConnection?.State == HubConnectionState.Connected;

        ForwardRecipientPanelToggleButton.Content = _forwardRecipientPickerOpen
            ? "− Hide recipients"
            : "＋ Add recipients";
    }

    private Style? FindStyle(string key) => FindResource(key) as Style;

    private Brush FindBrush(string key) => FindResource(key) as Brush ?? Brushes.Gray;
}
