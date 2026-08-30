using NovaChat.Client.Services;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private bool _conversationAvatarHooked;

    private void HookConversationAvatarRefresh()
    {
        if (_conversationAvatarHooked) return;
        _conversationAvatarHooked = true;
    }

    private Task RefreshConversationAvatarsAsync()
    {
        var api = _apiService;

        foreach (var item in _chats)
        {
            var userId = item.Chat.OtherUserId(AuthState.UserId);
            if (string.IsNullOrWhiteSpace(userId)) continue;

            var avatarPath = string.Equals(item.Chat.User1Id, userId, StringComparison.OrdinalIgnoreCase)
                ? item.Chat.User1AvatarUrl
                : item.Chat.User2AvatarUrl;

            item.AvatarUri = string.IsNullOrWhiteSpace(avatarPath)
                ? null
                : api.BuildAbsoluteUrl(avatarPath);
        }

        RefreshChatsList();
        return Task.CompletedTask;
    }

    private void InitializeConversationAvatarFix()
        => HookConversationAvatarRefresh();
}
