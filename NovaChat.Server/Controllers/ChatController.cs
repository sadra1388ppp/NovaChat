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

    public ChatController(ChatService chatService, IConfiguration configuration, IHubContext<ChatHub> hub)
    {
        _chatService = chatService;
        _configuration = configuration;
        _hub = hub;
    }

    [HttpPost]
    public async Task<IActionResult> CreateChat(CreateChatDto dto)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();
        if (dto == null || string.IsNullOrWhiteSpace(dto.UserId)) return BadRequest(new { message = "User ID is required." });
        var chat = await _chatService.CreatePrivateChatAsync(currentUserId, dto.UserId.Trim());
        if (chat == null) return BadRequest(new { message = "Unable to create private chat." });
        await _hub.Clients.Users(chat.User1Id, chat.User2Id).SendAsync("ChatCreated", new { id = chat.Id, user1Id = chat.User1Id, user2Id = chat.User2Id, createdAt = chat.CreatedAt, createdBy = currentUserId });
        return Ok(new { message = "Private chat created successfully.", chat = new { chat.Id, chat.User1Id, chat.User2Id, chat.CreatedAt } });
    }

    [HttpGet]
    public async Task<IActionResult> GetMyChats()
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();
        var chats = await _chatService.GetUserChatsAsync(currentUserId);
        return Ok(chats.Select(chat => MapChat(chat, chat.Messages.FirstOrDefault())).ToList());
    }

    [HttpGet("all")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> GetAllChats()
    {
        var chats = await _chatService.GetAllChatsAsync();
        return Ok(chats.Select(chat => MapChat(chat, chat.Messages.FirstOrDefault())).ToList());
    }

    [HttpGet("{chatId}/messages")]
    public async Task<IActionResult> GetMessages(int chatId, [FromQuery] int? beforeMessageId = null, [FromQuery] int pageSize = 50)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();
        if (!await CanAccessChat(chatId, currentUserId)) return Forbid();
        pageSize = Math.Clamp(pageSize, 1, 100);
        var messages = await _chatService.GetMessagesAsync(chatId, beforeMessageId, pageSize);
        var firstMessage = messages.FirstOrDefault();
        return Ok(new ChatHistoryResponseDto
        {
            Messages = messages.Select(MessageDtoMapper.Map).ToList(),
            HasMore = firstMessage != null && await _chatService.HasOlderMessagesAsync(chatId, firstMessage.Id),
            NextBeforeMessageId = firstMessage?.Id
        });
    }

    [HttpPost("{chatId}/messages")]
    public async Task<IActionResult> SendMessage(int chatId, SendMessageDto dto)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();
        if (!await CanAccessChat(chatId, currentUserId)) return Forbid();
        var message = await _chatService.SendMessageAsync(chatId, currentUserId, dto.Content);
        if (message == null) return BadRequest(new { message = "Unable to send message." });
        var chat = await _chatService.GetChatByIdAsync(chatId);
        var mapped = MessageDtoMapper.Map(message);
        if (chat != null) await _hub.Clients.Users(chat.User1Id, chat.User2Id).SendAsync("ReceiveMessage", mapped);
        return Ok(new { message = "Message sent successfully.", data = mapped });
    }

    [HttpDelete("{chatId}")]
    public async Task<IActionResult> DeleteChat(int chatId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();
        if (chatId <= 0) return BadRequest(new { message = "Invalid chat ID." });
        if (!IsOwner() && !await CanAccessChat(chatId, currentUserId)) return Forbid();
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null) return NotFound(new { message = "Chat not found." });
        if (!await _chatService.DeleteChatAsync(chatId)) return NotFound(new { message = "Chat not found." });
        await _hub.Clients.Users(chat.User1Id, chat.User2Id).SendAsync("ChatDeleted", new { chatId = chat.Id, deletedBy = currentUserId });
        return Ok(new { message = "Chat deleted successfully." });
    }

    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(int messageId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();
        var message = await _chatService.GetMessageByIdAsync(messageId);
        if (message == null) return NotFound(new { message = "Message not found." });
        if (!IsOwner())
        {
            if (!await CanAccessChat(message.ChatId, currentUserId)) return Forbid();
            if (!string.Equals(message.SenderId, currentUserId, StringComparison.OrdinalIgnoreCase)) return Forbid();
        }
        var chat = await _chatService.GetChatByIdAsync(message.ChatId);
        if (chat == null || !await _chatService.DeleteMessageAsync(messageId)) return NotFound(new { message = "Message not found." });
        await _hub.Clients.Users(chat.User1Id, chat.User2Id).SendAsync("MessageDeleted", new { id = message.Id, chatId = message.ChatId, senderId = message.SenderId, content = message.Content, sentAt = message.SentAt });
        return Ok(new { message = "Message deleted successfully." });
    }

    private ChatListDto MapChat(Chat chat, Message? lastMessage) => new()
    {
        Id = chat.Id,
        User1Id = chat.User1Id,
        User2Id = chat.User2Id,
        User1Name = chat.User1?.DisplayName ?? string.Empty,
        User2Name = chat.User2?.DisplayName ?? string.Empty,
        User1AvatarUrl = ToAbsoluteAvatarUrl(chat.User1?.AvatarUrl),
        User2AvatarUrl = ToAbsoluteAvatarUrl(chat.User2?.AvatarUrl),
        CreatedAt = chat.CreatedAt,
        LastMessage = lastMessage == null ? null : MessageDtoMapper.Map(lastMessage)
    };

    private string? ToAbsoluteAvatarUrl(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)) return null;
        if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out _)) return avatarUrl;
        return $"{Request.Scheme}://{Request.Host}{(avatarUrl.StartsWith('/') ? avatarUrl : "/" + avatarUrl)}";
    }

    private string? GetCurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsOwner() => !string.IsNullOrWhiteSpace(_configuration["Owner:UserId"]) && string.Equals(_configuration["Owner:UserId"], GetCurrentUserId(), StringComparison.OrdinalIgnoreCase);
    private async Task<bool> CanAccessChat(int chatId, string userId) => IsOwner() || await _chatService.CanAccessChatAsync(chatId, userId);
}
