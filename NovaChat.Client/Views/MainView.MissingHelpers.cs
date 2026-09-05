using NovaChat.Client.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private void AddMessageToUi(MessageModel message, bool insertAtTop = false)
    {
        var mine = string.Equals(message.SenderId, AuthState.UserId, StringComparison.OrdinalIgnoreCase);

        var border = new Border
        {
            Tag = message.Id,
            Background = mine
                ? (Brush)FindResource("PrimaryBrush")
                : (Brush)FindResource("PanelBackgroundBrush"),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(12),
            HorizontalAlignment = mine ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            MaxWidth = 450,
            Margin = new Thickness(0, 0, 0, 12)
        };

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = message.Content,
            TextWrapping = TextWrapping.Wrap,
            Foreground = mine ? Brushes.White : (Brush)FindResource("TextBrush")
        });
        panel.Children.Add(new TextBlock
        {
            Text = message.SentAt.ToLocalTime().ToString("HH:mm"),
            FontSize = 10,
            Margin = new Thickness(0, 5, 0, 0),
            Foreground = mine ? Brushes.White : (Brush)FindResource("SecondaryTextBrush"),
            HorizontalAlignment = HorizontalAlignment.Right
        });

        border.Child = panel;

        if (message.MessageType is "image" or "file" or "voice")
            border.Loaded += (_, _) => _ = RenderMediaBubbleAsync(border, message.Id);

        if (insertAtTop)
            MessagesPanel.Children.Insert(Math.Min(1, MessagesPanel.Children.Count), border);
        else
            MessagesPanel.Children.Add(border);
    }

    private async Task ScrollMessagesToBottomAsync()
    {
        await Task.Delay(50);
        await Dispatcher.InvokeAsync(() => MessagesScrollViewer.ScrollToEnd(),
            System.Windows.Threading.DispatcherPriority.Loaded);
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
            var profile = await _apiService.GetAsync<ProfileModel>(
                $"api/User/profile/{Uri.EscapeDataString(_currentOtherUserId)}");

            if (profile == null)
                return;

            ChatUserNameText.Text = profile.DisplayName;

            if (string.IsNullOrWhiteSpace(profile.AvatarUrl))
            {
                ChatHeaderAvatarImage.Source = null;
                ChatHeaderAvatarImage.Visibility = Visibility.Collapsed;
                ChatAvatarInitialsText.Text = GetInitials(profile.DisplayName);
                ChatAvatarInitialsText.Visibility = Visibility.Visible;
                return;
            }

            var avatar = await LoadConversationAvatarAsync(
                _apiService.BuildAbsoluteUrl(profile.AvatarUrl));

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
        catch
        {
            // Keep the existing header state on transient profile/avatar failures.
        }
    }

    private static string GetInitials(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "?";
        var parts = displayName.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0][..1].ToUpperInvariant();
        return string.Concat(parts[0][0], parts[^1][0]).ToUpperInvariant();
    }

    private void ProfileButton_Click(object sender, RoutedEventArgs e)
        => ProfileRequested?.Invoke();

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
        => SettingsRequested?.Invoke();
}
