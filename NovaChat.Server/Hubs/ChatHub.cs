using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using NovaChat.Server.DTOs;
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
    { _chatService = chatService; _presenceService = presenceService; _userService = userService; _configuration = configuration; }

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
        { await _userService.MarkLastSeenAsync(userId); await Clients.All.SendAsync("UserOffline", userId); }
        await base.OnDisconnectedAsync(exception);
    }

    public Task<List<string>> GetOnlineUsers() => Task.FromResult(_presenceService.GetOnlineUsers().ToList());
    public Task<bool> IsUserOnline(string userId) => Task.FromResult(!string.IsNullOrWhiteSpace(userId) && _presenceService.IsOnline(userId));

    public async Task SendMessage(int chatId, string content)
    {
        if (!TryGetCurrentUserId(out var userId)) throw new HubException("Unauthorized.");
        if (string.IsNullOrWhiteSpace(content)) throw new HubException("Message cannot be empty.");
        if (!await _chatService.CanAccessChatAsync(chatId, userId)) throw new HubException("You do not have access to this chat.");
        var message = await _chatService.SendMessageAsync(chatId, userId, content);
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (message == null || chat == null) throw new HubException("Unable to send message.");
        await Clients.Users(chat.User1Id.ToString(), chat.User2Id.ToString()).SendAsync("ReceiveMessage", MessageDtoMapper.Map(message));
    }

    public async Task DeleteMessage(int messageId)
    {
        if (!TryGetCurrentUserId(out var userId)) throw new HubException("Unauthorized.");
        var message = await _chatService.GetMessageByIdAsync(messageId); if (message == null) throw new HubException("Message not found.");
        if (!IsOwner() && (message.SenderId != userId || !await _chatService.CanAccessChatAsync(message.ChatId, userId))) throw new HubException("You do not have permission to delete this message.");
        var chat = await _chatService.GetChatByIdAsync(message.ChatId);
        if (chat == null || !await _chatService.DeleteMessageAsync(messageId)) throw new HubException("Unable to delete message.");
        await Clients.Users(chat.User1Id.ToString(), chat.User2Id.ToString()).SendAsync("MessageDeleted", new { id = message.Id, chatId = message.ChatId, senderId = message.SenderId.ToString(), content = message.Content, sentAt = message.SentAt });
    }

    public async Task JoinChat(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) throw new HubException("Unauthorized.");
        if (!await _chatService.CanAccessChatAsync(chatId, userId) && !IsOwner()) throw new HubException("You do not have access to this chat.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat-{chatId}");
    }

    public Task LeaveChat(int chatId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat-{chatId}");

    private string? CurrentUserId() => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool TryGetCurrentUserId(out long userId) => long.TryParse(CurrentUserId(), out userId) && userId > 0;
    private bool IsOwner()
    {
        var ownerUsername = _configuration["Owner:Username"];
        var username = Context.User?.FindFirst("username")?.Value;
        if (!string.IsNullOrWhiteSpace(ownerUsername) && string.Equals(ownerUsername, username, StringComparison.OrdinalIgnoreCase)) return true;
        return long.TryParse(_configuration["Owner:UserId"], out var ownerId) && long.TryParse(CurrentUserId(), out var currentId) && ownerId == currentId;
    }
}
