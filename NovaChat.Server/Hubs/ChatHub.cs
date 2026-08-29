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
    private readonly GroupService _groupService;

    public ChatHub(ChatService chatService, PresenceService presenceService, GroupService groupService)
    {
        _chatService = chatService;
        _presenceService = presenceService;
        _groupService = groupService;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) { Context.Abort(); return; }
        if (_presenceService.UserConnected(userId)) await Clients.All.SendAsync("UserOnline", userId);
        await Clients.Caller.SendAsync("PresenceSnapshot", _presenceService.GetOnlineUsers());
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userId) && _presenceService.UserDisconnected(userId)) await Clients.All.SendAsync("UserOffline", userId);
        await base.OnDisconnectedAsync(exception);
    }

    public Task<bool> IsUserOnline(string userId) => Task.FromResult(!string.IsNullOrWhiteSpace(userId) && _presenceService.IsOnline(userId));

    public async Task SendMessage(int chatId, string content)
    {
        var senderId = UserId();
        if (string.IsNullOrWhiteSpace(content)) throw new HubException("Message cannot be empty.");
        if (!await _chatService.CanAccessChatAsync(chatId, senderId)) throw new HubException("You do not have access to this chat.");
        var message = await _chatService.SendMessageAsync(chatId, senderId, content) ?? throw new HubException("Unable to send message.");
        var chat = await _chatService.GetChatByIdAsync(chatId) ?? throw new HubException("Chat not found.");
        await Clients.Users(chat.User1Id, chat.User2Id).SendAsync("ReceiveMessage", new { id = message.Id, chatId = message.ChatId, senderId = message.SenderId, senderName = message.Sender?.DisplayName ?? "", content = message.Content, sentAt = message.SentAt });
    }

    public async Task JoinChat(int chatId)
    {
        if (!await _chatService.CanAccessChatAsync(chatId, UserId())) throw new HubException("You do not have access to this chat.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat-{chatId}");
    }

    public Task LeaveChat(int chatId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"chat-{chatId}");

    public async Task JoinGroup(int groupId)
    {
        if (!await _groupService.IsMemberAsync(groupId, UserId())) throw new HubException("You are not a member of this group.");
        await Groups.AddToGroupAsync(Context.ConnectionId, $"group-{groupId}");
    }

    public Task LeaveGroup(int groupId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, $"group-{groupId}");

    public async Task SendGroupMessage(int groupId, string content)
    {
        if (string.IsNullOrWhiteSpace(content)) throw new HubException("Message cannot be empty.");
        var message = await _groupService.SendMessageAsync(groupId, UserId(), content) ?? throw new HubException("Unable to send group message.");
        await Clients.Group($"group-{groupId}").SendAsync("ReceiveGroupMessage", new { id = message.Id, groupId, senderId = message.SenderId, senderName = message.Sender?.DisplayName ?? "", content = message.Content, sentAt = message.SentAt, deletedForEveryone = false, deletedForMe = false });
    }

    public async Task DeleteGroupMessage(int messageId, bool forEveryone)
    {
        var result = await _groupService.DeleteMessageAsync(messageId, UserId(), forEveryone);
        if (!result.Success || result.Data == null) throw new HubException(result.Message);

        if (forEveryone)
        {
            await Clients.Group($"group-{result.Data.GroupId}").SendAsync("GroupMessageDeleted", new
            {
                id = result.Data.Id,
                groupId = result.Data.GroupId,
                senderId = result.Data.SenderId,
                content = result.Data.Content,
                deletedForEveryone = true,
                deletedForMe = false
            });
        }
        else
        {
            await Clients.Caller.SendAsync("GroupMessageDeleted", new
            {
                id = result.Data.Id,
                groupId = result.Data.GroupId,
                senderId = result.Data.SenderId,
                content = result.Data.Content,
                deletedForEveryone = false,
                deletedForMe = true
            });
        }
    }

    private string UserId() => Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new HubException("Unauthorized.");
}