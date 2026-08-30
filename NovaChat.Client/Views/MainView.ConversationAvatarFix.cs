using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Windows;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private bool _conversationAvatarHooked;

    private void HookConversationAvatarRefresh()
    {
        if (_conversationAvatarHooked) return;
        _conversationAvatarHooked = true;
        ChatsList.ItemContainerGenerator.StatusChanged += ChatsList_ItemContainerGenerator_StatusChanged;
        Loaded += MainView_ConversationAvatarLoaded;
    }

    private async void MainView_ConversationAvatarLoaded(object sender, RoutedEventArgs e)
        => await RefreshConversationAvatarsAsync();

    private async void ChatsList_ItemContainerGenerator_StatusChanged(object? sender, EventArgs e)
    {
        if (ChatsList.ItemContainerGenerator.Status != System.Windows.Controls.Primitives.GeneratorStatus.ContainersGenerated) return;
        await Dispatcher.InvokeAsync(RefreshConversationAvatarsAsync, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async Task RefreshConversationAvatarsAsync()
    {
        var api = new ApiService();

        foreach (var item in _chats)
        {
            var userId = item.Chat.OtherUserId(AuthState.UserId);
            if (string.IsNullOrWhiteSpace(userId)) continue;

            try
            {
                var profile = await api.GetAsync<ProfileModel>($"api/User/profile/{Uri.EscapeDataString(userId)}");
                if (profile == null) continue;

                item.DisplayName = profile.DisplayName;
                item.IsOnline = profile.IsOnline;

                var version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                item.AvatarUri = string.IsNullOrWhiteSpace(profile.AvatarUrl)
                    ? null
                    : api.BuildAbsoluteUrl($"api/avatar/{Uri.EscapeDataString(userId)}?v={version}");
            }
            catch
            {
                // Keep the last known conversation state on transient failures.
            }
        }

        RefreshChatsList();
    }

    private void InitializeConversationAvatarFix()
        => HookConversationAvatarRefresh();
}
