using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;
using NovaChat.Server.Hubs;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatController : ControllerBase
{
    private readonly ChatService _chatService;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<ChatHub> _hub;
    private readonly ILogger<ChatController> _logger;

    public ChatController(ChatService chatService, IConfiguration configuration, IHubContext<ChatHub> hub, ILogger<ChatController> logger)
    {
        _chatService = chatService;
        _configuration = configuration;
        _hub = hub;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateChat(CreateChatDto dto)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized();
        if (dto == null || string.IsNullOrWhiteSpace(dto.Username)) return BadRequest(new { message = "Username is required." });
        var otherUser = await _chatService.GetUserByUsernameAsync(dto.Username);
        if (otherUser == null) return NotFound(new { message = "User not found." });
        if (otherUser.Id == currentUserId) return BadRequest(new { message = "You cannot create a private chat with yourself." });
        try
        {
            var chat = await _chatService.CreatePrivateChatAsync(currentUserId, otherUser.Id);
            if (chat == null) return BadRequest(new { message = "The private chat could not be created." });
            var mapped = MapChat(chat, null);
            await _hub.Clients.Users(new[] { currentUserId.ToString(), otherUser.Id.ToString() }).SendAsync("ChatCreated", mapped);
            return Ok(new { message = "Private chat created successfully.", chat = mapped });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create private chat.");
            return Problem(statusCode: 500, title: "Private chat creation failed");
        }
    }

    [HttpPost("group")]
    public async Task<IActionResult> CreateGroup(CreateGroupChatDto dto)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (dto == null || string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Group name is required." });
        try
        {
            var chat = await _chatService.CreateGroupChatAsync(userId, dto.Name, dto.Usernames ?? []);
            if (chat == null) return BadRequest(new { message = "The group could not be created. Check the group name and usernames." });
            var mapped = MapChat(chat, null);
            await _hub.Clients.Users(RecipientIds(chat)).SendAsync("ChatCreated", mapped);
            return Ok(new { message = "Group created successfully.", chat = mapped });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create group chat.");
            return Problem(statusCode: 500, title: "Group creation failed");
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetMyChats()
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        return Ok((await _chatService.GetUserChatsAsync(userId)).Select(c => MapChat(c, c.Messages.FirstOrDefault())).ToList());
    }

    [HttpGet("{chatId}/members")]
    public async Task<IActionResult> GetMembers(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!await CanAccessChat(chatId, userId)) return Forbid();
        return Ok((await _chatService.GetMembersAsync(chatId)).Select(MapMember).ToList());
    }

    [HttpPost("{chatId}/members")]
    public async Task<IActionResult> AddMember(int chatId, AddGroupMemberDto dto)
    {
        if (!TryGetCurrentUserId(out var actorId)) return Unauthorized();
        if (dto == null || string.IsNullOrWhiteSpace(dto.Username)) return BadRequest(new { message = "Username is required." });
        var user = await _chatService.GetUserByUsernameAsync(dto.Username);
        if (user == null) return NotFound(new { message = "User not found." });
        if (!await _chatService.AddMemberAsync(chatId, actorId, user.Id)) return Forbid();
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null) return NotFound();
        var mapped = MapChat(chat, null);
        await _hub.Clients.Users(RecipientIds(chat)).SendAsync("GroupUpdated", mapped);
        return Ok(new { message = "Member added successfully.", chat = mapped });
    }

    [HttpDelete("{chatId}/members/{userId:long}")]
    public async Task<IActionResult> RemoveMember(int chatId, long userId)
    {
        if (!TryGetCurrentUserId(out var actorId)) return Unauthorized();
        if (!await _chatService.RemoveMemberAsync(chatId, actorId, userId)) return Forbid();
        await _hub.Clients.User(userId.ToString()).SendAsync("ChatMemberRemoved", new { chatId, userId = userId.ToString() });
        return Ok(new { message = "Member removed successfully." });
    }

    [HttpPost("{chatId}/leave")]
    public async Task<IActionResult> LeaveGroup(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!await _chatService.LeaveGroupAsync(chatId, userId)) return BadRequest(new { message = "Only non-owner members can leave a group." });
        await _hub.Clients.User(userId.ToString()).SendAsync("ChatMemberRemoved", new { chatId, userId = userId.ToString() });
        return Ok(new { message = "You left the group." });
    }

    [HttpPut("{chatId}/name")]
    public async Task<IActionResult> RenameGroup(int chatId, RenameGroupDto dto)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (dto == null || string.IsNullOrWhiteSpace(dto.Name)) return BadRequest(new { message = "Group name is required." });
        if (!await _chatService.RenameGroupAsync(chatId, userId, dto.Name)) return Forbid();
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null) return NotFound();
        var mapped = MapChat(chat, null);
        await _hub.Clients.Users(RecipientIds(chat)).SendAsync("GroupUpdated", mapped);
        return Ok(mapped);
    }

    [HttpGet("all")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> GetAllChats()
    {
        return Ok((await _chatService.GetAllChatsAsync()).Select(c => MapChat(c, c.Messages.FirstOrDefault())).ToList());
    }

    [HttpGet("{chatId}/messages")]
    public async Task<IActionResult> GetMessages(int chatId, [FromQuery] int? beforeMessageId = null, [FromQuery] int pageSize = 50)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!await CanAccessChat(chatId, userId)) return Forbid();
        pageSize = Math.Clamp(pageSize, 1, 100);
        var messages = await _chatService.GetMessagesAsync(chatId, beforeMessageId, pageSize);
        var first = messages.FirstOrDefault();
        return Ok(new ChatHistoryResponseDto
        {
            Messages = messages.Select(message => MessageDtoMapper.Map(message)).ToList(),
            HasMore = first != null && await _chatService.HasOlderMessagesAsync(chatId, first.Id),
            NextBeforeMessageId = first?.Id
        });
    }

    [HttpPost("{chatId}/messages")]
    public async Task<IActionResult> SendMessage(int chatId, SendMessageDto dto)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!await CanAccessChat(chatId, userId)) return Forbid();
        var message = await _chatService.SendMessageAsync(chatId, userId, dto.Content);
        if (message == null) return BadRequest(new { message = "Unable to send message." });
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat != null) await _hub.Clients.Users(RecipientIds(chat)).SendAsync("ReceiveMessage", MessageDtoMapper.Map(message));
        return Ok(new { message = "Message sent successfully.", data = MessageDtoMapper.Map(message) });
    }

    [HttpDelete("{chatId}")]
    public async Task<IActionResult> DeleteChat(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        if (!IsOwner() && !await _chatService.CanAccessChatAsync(chatId, userId)) return Forbid();
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null) return NotFound(new { message = "Chat not found." });
        if (!await _chatService.DeleteChatAsync(chatId)) return NotFound();
        await _hub.Clients.Users(RecipientIds(chat)).SendAsync("ChatDeleted", new { chatId = chat.Id, deletedBy = userId.ToString() });
        return Ok(new { message = "Chat deleted successfully." });
    }

    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(int messageId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        var message = await _chatService.GetMessageByIdAsync(messageId);
        if (message == null) return NotFound(new { message = "Message not found." });
        if (!IsOwner() && (!await _chatService.CanAccessChatAsync(message.ChatId, userId) || message.SenderId != userId)) return Forbid();
        var chat = await _chatService.GetChatByIdAsync(message.ChatId);
        if (chat == null || !await _chatService.DeleteMessageAsync(messageId)) return NotFound();
        await _hub.Clients.Users(RecipientIds(chat)).SendAsync("MessageDeleted", new { id = message.Id, chatId = message.ChatId, senderId = message.SenderId.ToString(), content = message.Content, sentAt = message.SentAt });
        return Ok(new { message = "Message deleted successfully." });
    }

    private ChatListDto MapChat(Chat chat, Message? lastMessage) => new()
    {
        Id = chat.Id,
        Type = chat.Type.ToString(),
        Name = chat.Type == ChatType.Group ? chat.Name : string.Empty,
        CreatedByUserId = chat.CreatedByUserId?.ToString() ?? string.Empty,
        User1Id = chat.User1Id?.ToString() ?? string.Empty,
        User2Id = chat.User2Id?.ToString() ?? string.Empty,
        User1Name = chat.User1?.DisplayName ?? string.Empty,
        User2Name = chat.User2?.DisplayName ?? string.Empty,
        User1AvatarUrl = ToAbsoluteAvatarUrl(chat.User1?.AvatarUrl),
        User2AvatarUrl = ToAbsoluteAvatarUrl(chat.User2?.AvatarUrl),
        CreatedAt = chat.CreatedAt,
        LastMessage = lastMessage == null ? null : MessageDtoMapper.Map(lastMessage)
    };

    private GroupMemberDto MapMember(ChatMember m) => new()
    {
        UserId = m.UserId.ToString(),
        Username = m.User.Username,
        DisplayName = m.User.DisplayName,
        AvatarUrl = ToAbsoluteAvatarUrl(m.User.AvatarUrl),
        Role = m.Role.ToString(),
        JoinedAt = m.JoinedAt
    };

    private IEnumerable<string> RecipientIds(Chat chat)
    {
        if (chat.Type == ChatType.Group)
        {
            return chat.Members.Select(member => member.UserId.ToString()).ToList();
        }

        var recipients = new List<string>();

        if (chat.User1Id.HasValue)
        {
            recipients.Add(chat.User1Id.Value.ToString());
        }

        if (chat.User2Id.HasValue)
        {
            recipients.Add(chat.User2Id.Value.ToString());
        }

        return recipients;
    }

    private string? ToAbsoluteAvatarUrl(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)) return null;
        if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out _)) return avatarUrl;
        return $"{Request.Scheme}://{Request.Host}{(avatarUrl.StartsWith('/') ? avatarUrl : "/" + avatarUrl)}";
    }

    private bool TryGetCurrentUserId(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;

    private bool IsOwner()
    {
        var ownerUsername = _configuration["Owner:Username"];
        var username = User.FindFirst("username")?.Value;
        if (!string.IsNullOrWhiteSpace(ownerUsername) && string.Equals(ownerUsername, username, StringComparison.OrdinalIgnoreCase)) return true;
        return long.TryParse(_configuration["Owner:UserId"], out var legacy) && long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var current) && legacy == current;
    }

    private Task<bool> CanAccessChat(int chatId, long userId) => IsOwner() ? Task.FromResult(true) : _chatService.CanAccessChatAsync(chatId, userId);
}