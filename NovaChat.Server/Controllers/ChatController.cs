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
    private readonly ChatService _chatService; private readonly IConfiguration _configuration; private readonly IHubContext<ChatHub> _hub; private readonly ILogger<ChatController> _logger;
    public ChatController(ChatService chatService, IConfiguration configuration, IHubContext<ChatHub> hub, ILogger<ChatController> logger) { _chatService = chatService; _configuration = configuration; _hub = hub; _logger = logger; }

    [HttpPost]
    public async Task<IActionResult> CreateChat(CreateChatDto dto)
    {
        if (!TryGetCurrentUserId(out var currentUserId)) return Unauthorized(new { message = "Authentication is required." });
        if (dto == null || string.IsNullOrWhiteSpace(dto.Username)) return BadRequest(new { message = "Username is required." });
        var username = dto.Username.Trim().ToLowerInvariant(); var otherUser = await _chatService.GetUserByUsernameAsync(username);
        if (otherUser == null) return NotFound(new { message = $"Username '{username}' was not found." });
        if (otherUser.Id == currentUserId) return BadRequest(new { message = "You cannot create a private chat with yourself." });
        try
        {
            var chat = await _chatService.CreatePrivateChatAsync(currentUserId, otherUser.Id); if (chat == null) return BadRequest(new { message = "The private chat could not be created." });
            var user1 = chat.User1Id.ToString(System.Globalization.CultureInfo.InvariantCulture); var user2 = chat.User2Id.ToString(System.Globalization.CultureInfo.InvariantCulture); var current = currentUserId.ToString(System.Globalization.CultureInfo.InvariantCulture);
            await _hub.Clients.Users(user1, user2).SendAsync("ChatCreated", new { id = chat.Id, user1Id = user1, user2Id = user2, createdAt = chat.CreatedAt, createdBy = current });
            return Ok(new { message = "Private chat created successfully.", chat = new { chat.Id, User1Id = user1, User2Id = user2, chat.CreatedAt } });
        }
        catch (Exception ex) { _logger.LogError(ex, "Failed to create private chat between {CurrentUserId} and {Username}.", currentUserId, username); return Problem(statusCode: 500, title: "Private chat creation failed", detail: "The server could not create the private chat."); }
    }

    [HttpGet]
    public async Task<IActionResult> GetMyChats()
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        var chats = await _chatService.GetUserChatsAsync(userId); return Ok(chats.Select(chat => MapChat(chat, chat.Messages.FirstOrDefault())).ToList());
    }

    [HttpGet("all")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> GetAllChats()
    {
        var chats = await _chatService.GetAllChatsAsync(); return Ok(chats.Select(chat => MapChat(chat, chat.Messages.FirstOrDefault())).ToList());
    }

    [HttpGet("{chatId}/messages")]
    public async Task<IActionResult> GetMessages(int chatId, [FromQuery] int? beforeMessageId = null, [FromQuery] int pageSize = 50)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized(); if (!await CanAccessChat(chatId, userId)) return Forbid();
        pageSize = Math.Clamp(pageSize, 1, 100); var messages = await _chatService.GetMessagesAsync(chatId, beforeMessageId, pageSize); var first = messages.FirstOrDefault();
        return Ok(new ChatHistoryResponseDto { Messages = messages.Select(m => MessageDtoMapper.Map(m)).ToList(), HasMore = first != null && await _chatService.HasOlderMessagesAsync(chatId, first.Id), NextBeforeMessageId = first?.Id });
    }

    [HttpPost("{chatId}/messages")]
    public async Task<IActionResult> SendMessage(int chatId, SendMessageDto dto)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized(); if (!await CanAccessChat(chatId, userId)) return Forbid();
        var message = await _chatService.SendMessageAsync(chatId, userId, dto.Content); if (message == null) return BadRequest(new { message = "Unable to send message." });
        var chat = await _chatService.GetChatByIdAsync(chatId); if (chat != null) await _hub.Clients.Users(chat.User1Id.ToString(), chat.User2Id.ToString()).SendAsync("ReceiveMessage", MessageDtoMapper.Map(message));
        return Ok(new { message = "Message sent successfully.", data = MessageDtoMapper.Map(message) });
    }

    [HttpDelete("{chatId}")]
    public async Task<IActionResult> DeleteChat(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized(); if (chatId <= 0) return BadRequest(new { message = "Invalid chat ID." });
        if (!IsOwner() && !await _chatService.CanAccessChatAsync(chatId, userId)) return Forbid();
        var chat = await _chatService.GetChatByIdAsync(chatId); if (chat == null) return NotFound(new { message = "Chat not found." });
        if (!await _chatService.DeleteChatAsync(chatId)) return NotFound(new { message = "Chat not found." });
        await _hub.Clients.Users(chat.User1Id.ToString(), chat.User2Id.ToString()).SendAsync("ChatDeleted", new { chatId = chat.Id, deletedBy = userId.ToString() }); return Ok(new { message = "Chat deleted successfully." });
    }

    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(int messageId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();
        var message = await _chatService.GetMessageByIdAsync(messageId); if (message == null) return NotFound(new { message = "Message not found." });
        if (!IsOwner()) { if (!await _chatService.CanAccessChatAsync(message.ChatId, userId)) return Forbid(); if (message.SenderId != userId) return Forbid(); }
        var chat = await _chatService.GetChatByIdAsync(message.ChatId); if (chat == null || !await _chatService.DeleteMessageAsync(messageId)) return NotFound(new { message = "Message not found." });
        await _hub.Clients.Users(chat.User1Id.ToString(), chat.User2Id.ToString()).SendAsync("MessageDeleted", new { id = message.Id, chatId = message.ChatId, senderId = message.SenderId.ToString(), content = message.Content, sentAt = message.SentAt }); return Ok(new { message = "Message deleted successfully." });
    }

    private ChatListDto MapChat(Chat chat, Message? lastMessage) => new()
    {
        Id = chat.Id, User1Id = chat.User1Id.ToString(), User2Id = chat.User2Id.ToString(), User1Name = chat.User1?.DisplayName ?? string.Empty, User2Name = chat.User2?.DisplayName ?? string.Empty,
        User1AvatarUrl = ToAbsoluteAvatarUrl(chat.User1?.AvatarUrl), User2AvatarUrl = ToAbsoluteAvatarUrl(chat.User2?.AvatarUrl), CreatedAt = chat.CreatedAt, LastMessage = lastMessage == null ? null : MessageDtoMapper.Map(lastMessage)
    };
    private string? ToAbsoluteAvatarUrl(string? avatarUrl) { if (string.IsNullOrWhiteSpace(avatarUrl)) return null; if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out _)) return avatarUrl; return $"{Request.Scheme}://{Request.Host}{(avatarUrl.StartsWith('/') ? avatarUrl : "/" + avatarUrl)}"; }
    private bool TryGetCurrentUserId(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
    private bool IsOwner() { var ownerUsername = _configuration["Owner:Username"]; var username = User.FindFirst("username")?.Value; if (!string.IsNullOrWhiteSpace(ownerUsername) && string.Equals(ownerUsername, username, StringComparison.OrdinalIgnoreCase)) return true; return long.TryParse(_configuration["Owner:UserId"], out var legacy) && long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var current) && legacy == current; }
    private Task<bool> CanAccessChat(int chatId, long userId) => IsOwner() ? Task.FromResult(true) : _chatService.CanAccessChatAsync(chatId, userId);
}
