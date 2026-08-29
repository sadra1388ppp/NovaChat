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

    public ChatHub(
        ChatService chatService,
        PresenceService presenceService)
    {
        _chatService = chatService;
        _presenceService = presenceService;
    }

    public override async Task OnConnectedAsync()
    {
        var userId =
            Context.User?.FindFirstValue(
                ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            Context.Abort();
            return;
        }

        var becameOnline =
            _presenceService.UserConnected(userId);

        if (becameOnline)
        {
            await Clients.All.SendAsync(
                "UserOnline",
                userId);
        }

        await Clients.Caller.SendAsync(
            "PresenceSnapshot",
            _presenceService.GetOnlineUsers());

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(
        Exception? exception)
    {
        var userId =
            Context.User?.FindFirstValue(
                ClaimTypes.NameIdentifier);

        if (!string.IsNullOrWhiteSpace(userId))
        {
            var becameOffline =
                _presenceService.UserDisconnected(
                    userId);

            if (becameOffline)
            {
                await Clients.All.SendAsync(
                    "UserOffline",
                    userId);
            }
        }

        await base.OnDisconnectedAsync(
            exception);
    }

    public async Task<bool> IsUserOnline(
        string userId)
    {
        if (string.IsNullOrWhiteSpace(userId))
        {
            return false;
        }

        return _presenceService.IsOnline(
            userId);
    }

    public async Task SendMessage(
        int chatId,
        string content)
    {
        var senderId =
            Context.User?.FindFirstValue(
                ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(senderId))
        {
            throw new HubException(
                "Unauthorized.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new HubException(
                "Message cannot be empty.");
        }

        var canAccess =
            await _chatService.CanAccessChatAsync(
                chatId,
                senderId);

        if (!canAccess)
        {
            throw new HubException(
                "You do not have access to this chat.");
        }

        var message =
            await _chatService.SendMessageAsync(
                chatId,
                senderId,
                content);

        if (message == null)
        {
            throw new HubException(
                "Unable to send message.");
        }

        var chat =
            await _chatService.GetChatByIdAsync(
                chatId);

        if (chat == null)
        {
            throw new HubException(
                "Chat not found.");
        }

        var payload = new
        {
            id = message.Id,
            chatId = message.ChatId,
            senderId = message.SenderId,
            senderName =
                message.Sender?.DisplayName ?? "",
            content = message.Content,
            sentAt = message.SentAt
        };

        await Clients.Users(
                chat.User1Id,
                chat.User2Id)
            .SendAsync(
                "ReceiveMessage",
                payload);
    }

    public async Task JoinChat(
        int chatId)
    {
        var userId =
            Context.User?.FindFirstValue(
                ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new HubException(
                "Unauthorized.");
        }

        var canAccess =
            await _chatService.CanAccessChatAsync(
                chatId,
                userId);

        if (!canAccess)
        {
            throw new HubException(
                "You do not have access to this chat.");
        }

        await Groups.AddToGroupAsync(
            Context.ConnectionId,
            $"chat-{chatId}");
    }

    public async Task LeaveChat(
        int chatId)
    {
        await Groups.RemoveFromGroupAsync(
            Context.ConnectionId,
            $"chat-{chatId}");
    }
}