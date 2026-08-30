using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private bool _conversationAvatarHooked;

    private void HookConversationAvatarRefresh()
    {
        // Refresh is driven explicitly by LoadChatsAsync and ProfileUpdated.
        // No global Loaded/container handlers are used, because those caused
        // repeated UI rebuilds and avatar flicker.
        _conversationAvatarHooked = true;
    }

    private async Task RefreshConversationAvatarsAsync()
    {
        foreach (var item in _chats.ToArray())
        {
            var userId = item.Chat.OtherUserId(AuthState.UserId);
            if (string.IsNullOrWhiteSpace(userId)) continue;

            try
            {
                var profile = await _apiService.GetAsync<ProfileModel>(
                    $"api/User/profile/{Uri.EscapeDataString(userId)}");
                if (profile == null) continue;

                item.DisplayName = profile.DisplayName;
                item.IsOnline = profile.IsOnline;
                item.AvatarUri = string.IsNullOrWhiteSpace(profile.AvatarUrl)
                    ? null
                    : _apiService.BuildAbsoluteUrl(profile.AvatarUrl);
            }
            catch
            {
                // Keep the last known state on transient failures.
            }
        }

        RefreshChatsList();
    }

    private void InitializeConversationAvatarFix()
        => HookConversationAvatarRefresh();
}
