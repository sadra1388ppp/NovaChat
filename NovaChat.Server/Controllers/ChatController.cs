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
        if (currentUserId == null)
            return Unauthorized();

        if (dto == null || string.IsNullOrWhiteSpace(dto.UserId))
            return BadRequest(new { message = "User ID is required." });

        var otherUserId = dto.UserId.Trim();

        if (string.Equals(currentUserId, otherUserId, StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "You cannot create a chat with yourself." });

        var chat = await _chatService.CreatePrivateChatAsync(currentUserId, otherUserId);
        if (chat == null)
        {
            return NotFound(new
            {
                message = "The specified User ID was not found."
            });
        }

        return Ok(new
        {
            message = "Private chat is ready.",
            chat = new ChatListDto
            {
                Id = chat.Id,
                User1Id = chat.User1Id,
                User2Id = chat.User2Id,
                User1Name = chat.User1.DisplayName,
                User2Name = chat.User2.DisplayName,
                CreatedAt = chat.CreatedAt,
                LastMessage = null
            }
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetMyChats()
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
            return Unauthorized();

        var chats = await _chatService.GetUserChatsAsync(currentUserId);
        var result = new List<ChatListDto>();

        foreach (var chat in chats)
        {
            var lastMessage = await _chatService.GetLastMessageAsync(chat.Id);

            result.Add(new ChatListDto
            {
                Id = chat.Id,
                User1Id = chat.User1Id,
                User2Id = chat.User2Id,
                User1Name = chat.User1.DisplayName,
                User2Name = chat.User2.DisplayName,
                CreatedAt = chat.CreatedAt,
                LastMessage = lastMessage == null ? null : MapMessage(lastMessage)
            });
        }

        return Ok(result);
    }

    [HttpGet("all")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> GetAllChats()
    {
        var chats = await _chatService.GetAllChatsAsync();
        var result = new List<ChatListDto>();

        foreach (var chat in chats)
        {
            var lastMessage = await _chatService.GetLastMessageAsync(chat.Id);

            result.Add(new ChatListDto
            {
                Id = chat.Id,
                User1Id = chat.User1Id,
                User2Id = chat.User2Id,
                User1Name = chat.User1.DisplayName,
                User2Name = chat.User2.DisplayName,
                CreatedAt = chat.CreatedAt,
                LastMessage = lastMessage == null ? null : MapMessage(lastMessage)
            });
        }

        return Ok(result);
    }

    [HttpGet("{chatId}/messages")]
    public async Task<IActionResult> GetMessages(int chatId, [FromQuery] int? beforeMessageId = null, [FromQuery] int pageSize = 50)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
            return Unauthorized();

        if (!await CanAccessChat(chatId, currentUserId))
            return Forbid();

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
        if (currentUserId == null)
            return Unauthorized();

        if (!await CanAccessChat(chatId, currentUserId))
            return Forbid();

        var message = await _chatService.SendMessageAsync(chatId, currentUserId, dto.Content);
        if (message == null)
            return BadRequest(new { message = "Unable to send message." });

        return Ok(new
        {
            message = "Message sent successfully.",
            data = MapMessage(message)
        });
    }

    [HttpDelete("{chatId}")]
    public async Task<IActionResult> DeleteChat(int chatId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
            return Unauthorized();

        if (!IsOwner() && !await CanAccessChat(chatId, currentUserId))
            return Forbid();

        var deleted = await _chatService.DeleteChatAsync(chatId);
        if (!deleted)
            return NotFound(new { message = "Chat not found." });

        return Ok(new { message = "Chat deleted successfully." });
    }

    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(int messageId)
    {
        var currentUserId = GetCurrentUserId();
        if (currentUserId == null)
            return Unauthorized();

        if (!IsOwner())
        {
            var message = await _chatService.GetMessageByIdAsync(messageId);
            if (message == null)
                return NotFound(new { message = "Message not found." });

            if (!await CanAccessChat(message.ChatId, currentUserId) || message.SenderId != currentUserId)
                return Forbid();
        }

        var deleted = await _chatService.DeleteMessageAsync(messageId);
        if (!deleted)
            return NotFound(new { message = "Message not found." });

        return Ok(new { message = "Message deleted successfully." });
    }

    private static MessageDto MapMessage(Message message) => new()
    {
        Id = message.Id,
        ChatId = message.ChatId,
        SenderId = message.SenderId,
        SenderName = message.Sender?.DisplayName ?? string.Empty,
        Content = message.Content,
        SentAt = message.SentAt
    };

    private string? GetCurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);

    private bool IsOwner()
    {
        var ownerUserId = _configuration["Owner:UserId"];
        var currentUserId = GetCurrentUserId();
        return !string.IsNullOrWhiteSpace(ownerUserId) &&
               string.Equals(ownerUserId, currentUserId, StringComparison.Ordinal);
    }

    private async Task<bool> CanAccessChat(int chatId, string userId)
    {
        if (IsOwner())
            return true;

        return await _chatService.CanAccessChatAsync(chatId, userId);
    }
}
