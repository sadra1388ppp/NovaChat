using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class RegisterView
{
    private ComboBox? _registrationMessagePrivacyBox;
    private CheckBox? _registrationAllowGroupAddsBox;
    private bool _registrationPrivacyInjected;

    static RegisterView()
    {
        EventManager.RegisterClassHandler(typeof(RegisterView), FrameworkElement.LoadedEvent, new RoutedEventHandler(RegisterViewPrivacyLoaded));
    }

    private static void RegisterViewPrivacyLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is RegisterView view)
            view.InjectRegistrationPrivacyControls();
    }

    private void InjectRegistrationPrivacyControls()
    {
        if (_registrationPrivacyInjected || PasswordBox.Parent is not StackPanel panel)
            return;

        var privacyPanel = new StackPanel { Margin = new Thickness(0, 2, 0, 18) };
        privacyPanel.Children.Add(new TextBlock
        {
            Text = "Privacy",
            FontSize = 12,
            FontWeight = FontWeights.SemiBold,
            Foreground = (Brush)FindResource("TextBrush"),
            Margin = new Thickness(0, 0, 0, 9)
        });

        var messageBorder = new Border
        {
            Padding = new Thickness(12),
            Background = (Brush)FindResource("InputBackgroundBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Margin = new Thickness(0, 0, 0, 9)
        };
        var messageGrid = new Grid();
        messageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        messageGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var messageStack = new StackPanel();
        messageStack.Children.Add(new TextBlock { Text = "Who can message me?", FontWeight = FontWeights.SemiBold, Foreground = (Brush)FindResource("TextBrush") });
        messageStack.Children.Add(new TextBlock { Text = "Everybody can start a chat, or require your approval first.", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 3, 8, 0), TextWrapping = TextWrapping.Wrap });
        messageGrid.Children.Add(messageStack);
        _registrationMessagePrivacyBox = new ComboBox
        {
            Width = 145,
            Height = 34,
            VerticalAlignment = VerticalAlignment.Center,
            ItemsSource = new[] { "Everybody", "Requests only" },
            SelectedIndex = 0
        };
        Grid.SetColumn(_registrationMessagePrivacyBox, 1);
        messageGrid.Children.Add(_registrationMessagePrivacyBox);
        messageBorder.Child = messageGrid;
        privacyPanel.Children.Add(messageBorder);

        var groupBorder = new Border
        {
            Padding = new Thickness(12),
            Background = (Brush)FindResource("InputBackgroundBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12)
        };
        _registrationAllowGroupAddsBox = new CheckBox
        {
            IsChecked = true,
            Foreground = (Brush)FindResource("TextBrush"),
            Content = new StackPanel
            {
                Children =
                {
                    new TextBlock { Text = "Allow other people to add me to groups", FontWeight = FontWeights.SemiBold },
                    new TextBlock { Text = "Turn this off to prevent direct group additions.", FontSize = 11, Foreground = (Brush)FindResource("SecondaryTextBrush"), Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap }
                }
            }
        };
        groupBorder.Child = _registrationAllowGroupAddsBox;
        privacyPanel.Children.Add(groupBorder);

        var index = panel.Children.IndexOf(PasswordBox);
        panel.Children.Insert(index, privacyPanel);
        _registrationPrivacyInjected = true;
    }

    private string GetRegistrationMessagePrivacy() =>
        string.Equals(_registrationMessagePrivacyBox?.SelectedItem?.ToString(), "Requests only", StringComparison.OrdinalIgnoreCase)
            ? "Requests"
            : "Everybody";

    private bool GetRegistrationAllowGroupAdds() => _registrationAllowGroupAddsBox?.IsChecked != false;
}
