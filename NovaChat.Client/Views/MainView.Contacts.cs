namespace NovaChat.Client.Views;

public partial class MainView
{
    public async Task OpenChatWithUserIdAsync(string userId)
    {
        if (string.IsNullOrWhiteSpace(userId) ||
            string.Equals(userId, Services.AuthState.UserId, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var result = await _apiService.PostAsync<
                Models.CreateChatRequest,
                Models.CreateChatResponse>(
                "api/Chat",
                new Models.CreateChatRequest { UserId = userId.Trim() });

            if (result?.Chat == null)
                return;

            await LoadChatsAsync();

            var chat = _chats.FirstOrDefault(x => x.Chat.Id == result.Chat.Id);
            if (chat != null)
                await OpenChatAsync(chat.Chat);
        }
        catch
        {
        }
    }
}