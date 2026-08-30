using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;
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

    public ChatController(ChatService chatService, IConfiguration configuration)
    {
        _chatService = chatService;
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> CreateChat(CreateChatDto dto)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();

        var chat = await _chatService.CreatePrivateChatAsync(currentUserId, dto.UserId);
        if (chat == null)
            return BadRequest(new { message = "Unable to create private chat." });

        return Ok(new
        {
            message = "Private chat created successfully.",
            chat = new
            {
                chat.Id,
                chat.User1Id,
                chat.User2Id,
                chat.CreatedAt
            }
        });
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
        var hasMore = firstMessage != null && await _chatService.HasOlderMessagesAsync(chatId, firstMessage.Id);

        return Ok(new ChatHistoryResponseDto
        {
            Messages = messages.Select(MapMessage).ToList(),
            HasMore = hasMore,
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
        if (message == null)
            return BadRequest(new { message = "Unable to send message." });

        return Ok(new { message = "Message sent successfully.", data = MapMessage(message) });
    }

    [HttpDelete("{chatId}")]
    public async Task<IActionResult> DeleteChat(int chatId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();

        if (!IsOwner() && !await CanAccessChat(chatId, currentUserId)) return Forbid();

        var deleted = await _chatService.DeleteChatAsync(chatId);
        if (!deleted) return NotFound(new { message = "Chat not found." });

        return Ok(new { message = "Chat deleted successfully." });
    }

    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(int messageId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null) return Unauthorized();

        if (!IsOwner())
        {
            var message = await _chatService.GetMessageByIdAsync(messageId);
            if (message == null) return NotFound(new { message = "Message not found." });
            if (!await CanAccessChat(message.ChatId, currentUserId)) return Forbid();
            if (message.SenderId != currentUserId) return Forbid();
        }

        var deleted = await _chatService.DeleteMessageAsync(messageId);
        if (!deleted) return NotFound(new { message = "Message not found." });

        return Ok(new { message = "Message deleted successfully." });
    }

    private ChatListDto MapChat(Chat chat, Message? lastMessage)
    {
        return new ChatListDto
        {
            Id = chat.Id,
            User1Id = chat.User1Id,
            User2Id = chat.User2Id,
            User1Name = chat.User1?.DisplayName ?? string.Empty,
            User2Name = chat.User2?.DisplayName ?? string.Empty,
            User1AvatarUrl = ToAbsoluteAvatarUrl(chat.User1?.AvatarUrl),
            User2AvatarUrl = ToAbsoluteAvatarUrl(chat.User2?.AvatarUrl),
            CreatedAt = chat.CreatedAt,
            LastMessage = lastMessage == null ? null : MapMessage(lastMessage)
        };
    }

    private string? ToAbsoluteAvatarUrl(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)) return null;
        if (Uri.TryCreate(avatarUrl, UriKind.Absolute, out _)) return avatarUrl;
        return $"{Request.Scheme}://{Request.Host}{(avatarUrl.StartsWith('/') ? avatarUrl : "/" + avatarUrl)}";
    }

    private static MessageDto MapMessage(Message message)
    {
        return new MessageDto
        {
            Id = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            SenderName = message.Sender?.DisplayName ?? string.Empty,
            Content = message.Content,
            SentAt = message.SentAt
        };
    }

    private string? GetCurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private bool IsOwner()
    {
        var ownerUserId = _configuration["Owner:UserId"];
        var currentUserId = GetCurrentUserId();
        return !string.IsNullOrWhiteSpace(ownerUserId) && currentUserId == ownerUserId;
    }

    private async Task<bool> CanAccessChat(int chatId, string userId)
    {
        if (IsOwner()) return true;
        return await _chatService.CanAccessChatAsync(chatId, userId);
    }
}