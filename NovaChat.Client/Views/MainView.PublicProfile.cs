using NovaChat.Client.Models;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class MainView
{
    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        ChatUserNameText.MouseLeftButtonUp += ChatUserNameText_MouseLeftButtonUp;
        ChatUserNameText.Cursor = Cursors.Hand;
        InitializeConversationAvatarFix();
    }

    private async void ChatUserNameText_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentOtherUserId)) return;
        e.Handled = true;

        try
        {
            var profile = await _apiService.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(_currentOtherUserId)}");
            if (profile == null)
            {
                MessageBox.Show("This user's profile could not be loaded.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ShowPublicProfile(profile);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not load this profile.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void ShowPublicProfile(ProfileModel profile)
    {
        var window = new Window
        {
            Title = $"{profile.DisplayName} · Profile",
            Width = 390,
            Height = 470,
            MinWidth = 350,
            MinHeight = 430,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("PanelBackgroundBrush")
        };

        var root = new StackPanel { Margin = new Thickness(28) };
        var avatarGrid = new Grid { Width = 112, Height = 112, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 18) };
        avatarGrid.Children.Add(new Ellipse { Fill = (Brush)FindResource("SelectedChatBrush") });

        var initials = new TextBlock
        {
            Text = GetPublicProfileInitials(profile.DisplayName, profile.Id),
            Foreground = (Brush)FindResource("PrimaryBrush"),
            FontSize = 38,
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        avatarGrid.Children.Add(initials);

        if (!string.IsNullOrWhiteSpace(profile.AvatarUrl))
        {
            try
            {
                var image = new Image
                {
                    Width = 112,
                    Height = 112,
                    Stretch = Stretch.UniformToFill,
                    Clip = new EllipseGeometry(new Point(56, 56), 56, 56),
                    Source = new BitmapImage(new Uri(_apiService.BuildAbsoluteUrl(profile.AvatarUrl) + $"?v={DateTime.UtcNow.Ticks}"))
                };
                avatarGrid.Children.Add(image);
                initials.Visibility = Visibility.Collapsed;
            }
            catch { }
        }

        root.Children.Add(avatarGrid);
        root.Children.Add(new TextBlock { Text = profile.DisplayName, FontSize = 24, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center });
        root.Children.Add(new TextBlock { Text = $"@{profile.Id}", FontSize = 13, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 14) });

        var status = profile.IsOnline ? "● Online" : "● Offline";
        root.Children.Add(new TextBlock { Text = status, FontSize = 12, Foreground = profile.IsOnline ? Brushes.LimeGreen : (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 18) });

        if (!string.IsNullOrWhiteSpace(profile.Bio))
            root.Children.Add(new TextBlock { Text = profile.Bio, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = (Brush)FindResource("TextBrush"), HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, Margin = new Thickness(10, 0, 10, 18) });

        root.Children.Add(new TextBlock { Text = profile.IsOnline ? "Active now" : (profile.LastSeenAt.HasValue ? $"Last seen {FormatPublicLastSeen(profile.LastSeenAt.Value)}" : "Last seen not available"), FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        root.Children.Add(new TextBlock { Text = $"Joined {profile.CreatedAt.ToLocalTime():dd MMM yyyy}", FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 5, 0, 20) });

        var closeButton = new Button { Content = "Close", Width = 100, Height = 36, HorizontalAlignment = HorizontalAlignment.Center, Background = (Brush)FindResource("PrimaryBrush"), Foreground = Brushes.White, BorderThickness = new Thickness(0) };
        closeButton.Click += (_, _) => window.Close();
        root.Children.Add(closeButton);
        window.Content = root;
        window.ShowDialog();
    }

    private static string GetPublicProfileInitials(string displayName, string id)
    {
        var value = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
        if (string.IsNullOrWhiteSpace(value)) return "?";
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant() : value[..Math.Min(2, value.Length)].ToUpperInvariant();
    }

    private static string FormatPublicLastSeen(DateTime value)
    {
        var local = value.ToLocalTime();
        return local.Date == DateTime.Now.Date ? $"today at {local:HH:mm}" : local.ToString("dd MMM yyyy HH:mm");
    }
}
