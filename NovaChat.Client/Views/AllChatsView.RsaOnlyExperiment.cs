using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views;

public partial class AllChatsView
{
    private readonly RsaOnlyCryptoService _rsaOwnerCrypto = new();
    private bool _rsaOwnerHooked;
    private int? _rsaLastChatId;

    static AllChatsView()
    {
        EventManager.RegisterClassHandler(
            typeof(AllChatsView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnRsaOwnerLoaded));
    }

    private static async void OnRsaOwnerLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not AllChatsView view || view._rsaOwnerHooked)
            return;

        view._rsaOwnerHooked = true;
        view.Loaded -= OnRsaOwnerLoaded;
        view.Unloaded += OnRsaOwnerUnloaded;
        view.ChatsList.SelectionChanged += view.RsaOwnerSelectionChanged;

        if (view.ChatsList.SelectedItem is AdminChatItem item)
            await view.DecryptSelectedOwnerChatAsync(item);
    }

    private static void OnRsaOwnerUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is not AllChatsView view)
            return;

        view.ChatsList.SelectionChanged -= view.RsaOwnerSelectionChanged;
        view.Unloaded -= OnRsaOwnerUnloaded;
        view._rsaOwnerHooked = false;
        view._rsaLastChatId = null;
    }

    private async void RsaOwnerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ChatsList.SelectedItem is AdminChatItem item)
            await DecryptSelectedOwnerChatAsync(item);
    }

    private async Task DecryptSelectedOwnerChatAsync(AdminChatItem item)
    {
        if (_rsaLastChatId == item.Chat.Id)
            return;

        _rsaLastChatId = item.Chat.Id;

        try
        {
            await _rsaOwnerCrypto.InitializeAsync(_apiService);
            var history = await _apiService.GetAsync<OwnerMessagesResponse>(
                $"api/OwnerChat/{item.Chat.Id}/messages?pageSize=1000");

            var messages = history?.Messages ?? [];
            foreach (var message in messages)
                await _rsaOwnerCrypto.DecryptMessageAsync(message);

            MessagesList.ItemsSource = messages
                .Select(message => new AdminMessageItem(message))
                .ToList();

            NoMessagesText.Visibility = messages.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"RSA-only Owner chat decryption failed: {ex}");
        }
    }
}
