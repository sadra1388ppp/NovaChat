using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ChatService _chatService;
    private readonly PresenceService _presenceService;
    private readonly UserService _userService;
    private readonly IConfiguration _configuration;

    public ChatHub(ChatService chatService, PresenceService presenceService, UserService userService, IConfiguration configuration)
    {
        _chatService = chatService;
        _presenceService = presenceService;
        _userService = userService;
        _configuration = configuration;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) { Context.Abort(); return; }
        if (_presenceService.UserConnected(userId)) await Clients.All.SendAsync("UserOnline", userId);
        await Clients.Caller.SendAsync("PresenceSnapshot", _presenceService.GetOnlineUsers());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = CurrentUserId();
        if (!string.IsNullOrWhiteSpace(userId) && _presenceService.UserDisconnected(userId))
        {
            await _userService.MarkLastSeenAsync(userId);
            await Clients.All.SendAsync("UserOffline", userId);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public Task<List<string>> GetOnlineUsers() => Task.FromResult(_presenceService.GetOnlineUsers().ToList());
    public Task<bool> IsUserOnline(string userId) => Task.FromResult(!string.IsNullOrWhiteSpace(userId) && _presenceService.IsOnline(userId));

    public async Task SendMessage(int chatId, string content)
    {
        var senderId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(senderId)) throw new HubException("Unauthorized.");
        if (string.IsNullOrWhiteSpace(content)) throw new HubException("Message cannot be empty.");
        if (!await _chatService.CanAccessChatAsync(chatId, senderId)) throw new HubException("You do not have access to this chat.");

        var message = await _chatService.SendMessageAsync(chatId, senderId, content);
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (message == null || chat == null) throw new HubException("Unable to send message.");

        await Clients.Users(chat.User1Id, chat.User2Id).SendAsync("ReceiveMessage", new
        {
            id = message.Id,
            chatId = message.ChatId,
            senderId = message.SenderId,
            senderName = message.Sender?.DisplayName ?? "",
            content = message.Content,
            sentAt = message.SentAt
        });
    }

    public async Task DeleteMessage(int messageId)
    {
        var userId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) throw new HubException("Unauthorized.");

        var message = await _chatService.GetMessageByIdAsync(messageId);
        if (message == null) throw new HubException("Message not found.");

        var isOwner = string.Equals(_configuration["Owner:UserId"], userId, StringComparison.OrdinalIgnoreCase);
        if (!isOwner && !string.Equals(message.SenderId, userId, StringComparison.OrdinalIgnoreCase))
            throw new HubException("You can only delete your own messages.");
        if (!isOwner && !await _chatService.CanAccessChatAsync(message.ChatId, userId))
            throw new HubException("You do not have access to this chat.");

        var chat = await _chatService.GetChatByIdAsync(message.ChatId);
        if (chat == null || !await _chatService.DeleteMessageAsync(messageId)) throw new HubException("Unable to delete message.");

        await Clients.Users(chat.User1Id, chat.User2Id).SendAsync("MessageDeleted", new
        {
            id = message.Id,
            chatId = message.ChatId,
            senderId = message.SenderId,
            content = message.Content,
            sentAt = message.SentAt
        });
    }

    public async Task JoinChat(int chatId)
    {
        var userId = CurrentUserId();
        if (string.IsNullOrWhiteSpace(userId)) throw new HubException("Unauthorized.");
        if (!await _chatService.CanAccessChatAsync(chatId, userId)) throw new HubException("You do not have access to this chat.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat-{chatId}");
    }

    public Task LeaveChat(int chatId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat-{chatId}");

    private string? CurrentUserId() => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
}
