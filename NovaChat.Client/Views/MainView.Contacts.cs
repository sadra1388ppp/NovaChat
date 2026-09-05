namespace NovaChat.Client.Views;

public partial class MainView
{
    public async Task OpenChatWithUsernameAsync(string username)
    {
        if (string.IsNullOrWhiteSpace(username) ||
            string.Equals(username.Trim(), NovaChat.Client.Services.AuthState.Username, StringComparison.OrdinalIgnoreCase))
            return;

        try
        {
            var result = await _apiService.PostAsync<
                NovaChat.Client.Models.CreateChatRequest,
                NovaChat.Client.Models.CreateChatResponse>(
                "api/Chat",
                new NovaChat.Client.Models.CreateChatRequest { Username = username.Trim() });

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
