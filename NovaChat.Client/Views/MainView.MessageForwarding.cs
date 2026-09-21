using Microsoft.AspNetCore.SignalR.Client;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;
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
                    "Forwarding currently supports text messages. Media forwarding will be added separately so encrypted files are re-encrypted safely for the destination.",
                    "Forward Message",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var decrypted = await _e2ee.DecryptMessageAsync(message);
            if (string.IsNullOrWhiteSpace(decrypted.Content) ||
                decrypted.Content.StartsWith("[Encrypted message", StringComparison.Ordinal))
            {
                MessageBox.Show("This message could not be decrypted on this device.", "Forward Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var target = ShowForwardDestinationDialog();
            if (target == null)
                return;

            if (target.Chat.Id == message.ChatId)
            {
                MessageBox.Show("Choose a different conversation.", "Forward Message", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
            {
                MessageBox.Show("NovaChat is not connected to the server.", "Forward Message", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var forwardedText = $"↪ Forwarded from @{message.SenderId}\n\n{decrypted.Content}";
            var encrypted = await _e2ee.EncryptForChatAsync(target.Chat.Id, forwardedText, _apiService);
            await _hubConnection.InvokeAsync("SendMessage", target.Chat.Id, encrypted);
            await LoadChatsAsync();

            System.Media.SystemSounds.Asterisk.Play();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not forward the message.\n\n{ex.Message}", "Forward Message", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private ChatListItem? ShowForwardDestinationDialog()
    {
        var available = _chats
            .Where(x => x.Chat.Id != _currentChatId)
            .OrderBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (available.Count == 0)
        {
            MessageBox.Show("There are no other conversations available.", "Forward Message", MessageBoxButton.OK, MessageBoxImage.Information);
            return null;
        }

        var dialog = new Window
        {
            Title = "Forward Message",
            Width = 520,
            Height = 620,
            Owner = Window.GetWindow(this),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResizeWithGrip,
            MinWidth = 460,
            MinHeight = 500,
            ShowInTaskbar = false,
            Background = FindResource("AppBackgroundBrush") as Brush
        };

        var root = new Grid { Margin = new Thickness(22) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(new TextBlock
        {
            Text = "Forward message",
            FontSize = 21,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("TextBrush") as Brush
        });

        root.Children.Add(new TextBlock
        {
            Text = "Choose the conversation where this message should be sent.",
            FontSize = 12,
            Foreground = FindResource("SecondaryTextBrush") as Brush,
            Margin = new Thickness(0, 5, 0, 14)
        });

        var list = new ListBox
        {
            ItemsSource = available,
            BorderThickness = new Thickness(1),
            BorderBrush = FindResource("BorderBrush") as Brush,
            Background = FindResource("PanelBackgroundBrush") as Brush,
            Foreground = FindResource("TextBrush") as Brush,
            Padding = new Thickness(4)
        };

        list.ItemTemplate = new DataTemplate
        {
            VisualTree = BuildForwardDestinationTemplate()
        };

        Grid.SetRow(list, 2);
        root.Children.Add(list);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };

        var cancel = new Button
        {
            Content = "Cancel",
            Width = 100,
            Height = 38,
            Margin = new Thickness(0, 0, 9, 0),
            Style = FindResource("SecondaryButtonStyle") as Style
        };
        var forward = new Button
        {
            Content = "Forward",
            Width = 110,
            Height = 38,
            Style = FindResource("PrimaryButtonStyle") as Style,
            IsEnabled = false
        };

        list.SelectionChanged += (_, _) => forward.IsEnabled = list.SelectedItem is ChatListItem;
        cancel.Click += (_, _) => dialog.DialogResult = false;
        forward.Click += (_, _) =>
        {
            if (list.SelectedItem is ChatListItem)
                dialog.DialogResult = true;
        };

        actions.Children.Add(cancel);
        actions.Children.Add(forward);
        Grid.SetRow(actions, 3);
        root.Children.Add(actions);

        dialog.Content = root;
        dialog.Loaded += (_, _) => list.Focus();

        return dialog.ShowDialog() == true ? list.SelectedItem as ChatListItem : null;
    }

    private FrameworkElementFactory BuildForwardDestinationTemplate()
    {
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.PaddingProperty, new Thickness(10));
        border.SetValue(Border.MarginProperty, new Thickness(2));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));

        var grid = new FrameworkElementFactory(typeof(Grid));
        grid.SetValue(Grid.MinHeightProperty, 54d);

        var stack = new FrameworkElementFactory(typeof(StackPanel));
        stack.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);

        var name = new FrameworkElementFactory(typeof(TextBlock));
        name.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ChatListItem.DisplayName)));
        name.SetValue(TextBlock.FontSizeProperty, 14d);
        name.SetValue(TextBlock.FontWeightProperty, FontWeights.SemiBold);
        name.SetValue(TextBlock.ForegroundProperty, FindResource("TextBrush") as Brush);

        var subtitle = new FrameworkElementFactory(typeof(TextBlock));
        subtitle.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Chat.Type"));
        subtitle.SetValue(TextBlock.FontSizeProperty, 11d);
        subtitle.SetValue(TextBlock.MarginProperty, new Thickness(0, 3, 0, 0));
        subtitle.SetValue(TextBlock.ForegroundProperty, FindResource("SecondaryTextBrush") as Brush);

        stack.AppendChild(name);
        stack.AppendChild(subtitle);
        grid.AppendChild(stack);
        border.AppendChild(grid);
        return border;
    }
}
