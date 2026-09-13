using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static bool _chatRequestUiRegistered;
    private DispatcherTimer? _chatRequestTimer;
    private readonly HashSet<long> _notifiedChatRequestIds = [];
    private bool _chatRequestBusy;

    private static void RegisterChatRequestHandlers()
    {
        if (_chatRequestUiRegistered) return;
        _chatRequestUiRegistered = true;
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(OnMainViewChatButtonClicked));
        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnMainViewChatRequestsLoaded));
    }

    private static async void OnMainViewChatButtonClicked(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not Button button) return;
        var content = button.Content?.ToString();
        if (content is not ("＋   New conversation" or "＋   Start a new chat")) return;
        if (FindAncestor<MainView>(button) is not MainView view) return;

        e.Handled = true;
        await view.OpenChatRequestFlowAsync();
    }

    private static async void OnMainViewChatRequestsLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainView view || view._chatRequestTimer != null) return;
        view._chatRequestTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        view._chatRequestTimer.Tick += async (_, _) => await view.PollIncomingChatRequestsAsync();
        view._chatRequestTimer.Start();
        await view.PollIncomingChatRequestsAsync();
    }

    private async Task OpenChatRequestFlowAsync()
    {
        if (_chatRequestBusy || !AuthState.IsAuthenticated) return;
        var dialog = new Window
        {
            Title = "New Conversation",
            Width = 430,
            Height = 250,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = Window.GetWindow(this),
            ResizeMode = ResizeMode.NoResize,
            Background = (Brush)FindResource("PanelBackgroundBrush")
        };
        var root = new StackPanel { Margin = new Thickness(22) };
        root.Children.Add(new TextBlock { Text = "Start a conversation", FontSize = 21, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("TextBrush") });
        root.Children.Add(new TextBlock { Text = "Enter a username. Their privacy settings determine whether the chat starts immediately or needs approval.", FontSize = 12, Foreground = (Brush)FindResource("SecondaryTextBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 16) });
        var box = new TextBox { Height = 42, Padding = new Thickness(11), ToolTip = "Username" };
        root.Children.Add(box);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 85, Height = 38, Margin = new Thickness(0, 0, 8, 0), Style = (Style)FindResource("SecondaryButtonStyle") };
        var start = new Button { Content = "Continue", Width = 95, Height = 38, Style = (Style)FindResource("PrimaryButtonStyle") };
        cancel.Click += (_, _) => dialog.Close();
        start.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(start); root.Children.Add(actions);
        box.KeyDown += (_, args) => { if (args.Key == System.Windows.Input.Key.Enter) start.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); };
        dialog.Content = root;
        dialog.Loaded += (_, _) => box.Focus();
        if (dialog.ShowDialog() != true) return;

        var username = box.Text.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.Equals(username, AuthState.Username, StringComparison.OrdinalIgnoreCase)) return;

        _chatRequestBusy = true;
        try
        {
            var result = await _apiService.PostAsync<CreateChatRequest, CreateChatResponse>("api/Chat", new CreateChatRequest { Username = username });
            if (result == null)
            {
                MessageBox.Show("The server returned no response.", "New Conversation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (result.RequestPending)
            {
                MessageBox.Show("Your chat request has been sent. You can start messaging after the other person accepts it.", "Chat Request Sent", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (result.Chat == null)
            {
                MessageBox.Show(string.IsNullOrWhiteSpace(result.Message) ? "The conversation could not be created." : result.Message, "New Conversation", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            await LoadChatsAsync();
            var item = _chats.FirstOrDefault(x => x.Chat.Id == result.Chat.Id);
            if (item != null) await OpenChatAsync(item.Chat);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not start the conversation.\n\n{ex.Message}", "New Conversation", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _chatRequestBusy = false;
        }
    }

    private async Task PollIncomingChatRequestsAsync()
    {
        if (_chatRequestBusy || !AuthState.IsAuthenticated) return;
        try
        {
            var requests = await _apiService.GetAsync<List<IncomingChatRequestModel>>("api/ChatRequests/incoming");
            foreach (var request in requests ?? [])
            {
                if (_notifiedChatRequestIds.Contains(request.Id)) continue;
                _notifiedChatRequestIds.Add(request.Id);
                await ShowIncomingChatRequestAsync(request);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Chat request polling failed: {ex.Message}");
        }
    }

    private async Task ShowIncomingChatRequestAsync(IncomingChatRequestModel request)
    {
        var requester = string.IsNullOrWhiteSpace(request.RequesterDisplayName)
            ? $"@{request.RequesterUsername}"
            : $"{request.RequesterDisplayName} (@{request.RequesterUsername})";
        var result = MessageBox.Show(
            $"{requester} wants to start a private conversation with you.\n\nAccept to create the chat, or Reject to decline it.",
            "New Chat Request",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes);

        try
        {
            if (result == MessageBoxResult.Yes)
            {
                await _apiService.PostAsync<object, RequestActionResponse>($"api/ChatRequests/{request.Id}/accept", new { });
                MessageBox.Show("Chat request accepted. The new conversation is now available.", "Chat Request", MessageBoxButton.OK, MessageBoxImage.Information);
                await LoadChatsAsync();
            }
            else
            {
                await _apiService.PostAsync<object, RequestActionResponse>($"api/ChatRequests/{request.Id}/reject", new { });
            }
        }
        catch (Exception ex)
        {
            _notifiedChatRequestIds.Remove(request.Id);
            MessageBox.Show($"Could not process the chat request.\n\n{ex.Message}", "Chat Request", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private sealed class IncomingChatRequestModel
    {
        public long Id { get; set; }
        public string RequesterUsername { get; set; } = string.Empty;
        public string RequesterDisplayName { get; set; } = string.Empty;
    }

    private sealed class RequestActionResponse
    {
        public string Message { get; set; } = string.Empty;
    }
}
