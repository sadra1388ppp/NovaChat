using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaChat.Server.DTOs;
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

    public ChatController(
        ChatService chatService,
        IConfiguration configuration)
    {
        _chatService = chatService;
        _configuration = configuration;
    }

    [HttpPost]
    public async Task<IActionResult> CreateChat(
        CreateChatDto dto)
    {
        var currentUserId = GetCurrentUserId();

        if (currentUserId == null)
            return Unauthorized();

        var chat =
            await _chatService.CreatePrivateChatAsync(
                currentUserId,
                dto.UserId);

        if (chat == null)
        {
            return BadRequest(new
            {
                message = "Unable to create private chat."
            });
        }

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

        if (currentUserId == null)
            return Unauthorized();

        var chats =
            await _chatService.GetUserChatsAsync(
                currentUserId);

        return Ok(chats.Select(c => new
        {
            c.Id,
            c.User1Id,
            c.User2Id,
            c.CreatedAt,
            User1Name = c.User1.DisplayName,
            User2Name = c.User2.DisplayName
        }));
    }

    [HttpGet("all")]
    [Authorize(Policy = "OwnerOnly")]
    public async Task<IActionResult> GetAllChats()
    {
        var chats =
            await _chatService.GetAllChatsAsync();

        return Ok(chats.Select(c => new
        {
            c.Id,
            c.User1Id,
            c.User2Id,
            c.CreatedAt,
            User1Name = c.User1.DisplayName,
            User2Name = c.User2.DisplayName
        }));
    }

    [HttpGet("{chatId}/messages")]
    public async Task<IActionResult> GetMessages(
        int chatId)
    {
        var currentUserId = GetCurrentUserId();

        if (currentUserId == null)
            return Unauthorized();

        if (!await CanAccessChat(
                chatId,
                currentUserId))
        {
            return Forbid();
        }

        var messages =
            await _chatService.GetMessagesAsync(
                chatId);

        return Ok(messages.Select(m => new
        {
            m.Id,
            m.ChatId,
            m.SenderId,
            SenderName = m.Sender.DisplayName,
            m.Content,
            m.SentAt
        }));
    }

    [HttpPost("{chatId}/messages")]
    public async Task<IActionResult> SendMessage(
        int chatId,
        SendMessageDto dto)
    {
        var currentUserId = GetCurrentUserId();

        if (currentUserId == null)
            return Unauthorized();

        if (!await CanAccessChat(
                chatId,
                currentUserId))
        {
            return Forbid();
        }

        var message =
            await _chatService.SendMessageAsync(
                chatId,
                currentUserId,
                dto.Content);

        if (message == null)
        {
            return BadRequest(new
            {
                message = "Unable to send message."
            });
        }

        return Ok(new
        {
            message = "Message sent successfully.",

            data = new
            {
                message.Id,
                message.ChatId,
                message.SenderId,
                SenderName = message.Sender.DisplayName,
                message.Content,
                message.SentAt
            }
        });
    }

    [HttpDelete("{chatId}")]
    public async Task<IActionResult> DeleteChat(
        int chatId)
    {
        var currentUserId = GetCurrentUserId();

        if (currentUserId == null)
            return Unauthorized();

        var isOwner = IsOwner();

        if (!isOwner &&
            !await CanAccessChat(
                chatId,
                currentUserId))
        {
            return Forbid();
        }

        var deleted =
            await _chatService.DeleteChatAsync(
                chatId);

        if (!deleted)
        {
            return NotFound(new
            {
                message = "Chat not found."
            });
        }

        return Ok(new
        {
            message = "Chat deleted successfully."
        });
    }

    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(
        int messageId)
    {
        var currentUserId = GetCurrentUserId();

        if (currentUserId == null)
            return Unauthorized();

        if (!IsOwner())
        {
            var chatId =
                await GetChatIdFromMessage(
                    messageId);

            if (chatId == 0 ||
                !await CanAccessChat(
                    chatId,
                    currentUserId))
            {
                return Forbid();
            }

            var messages =
                await _chatService.GetMessagesAsync(
                    chatId);

            var message =
                messages.FirstOrDefault(
                    m => m.Id == messageId);

            if (message == null ||
                message.SenderId != currentUserId)
            {
                return Forbid();
            }
        }

        var deleted =
            await _chatService.DeleteMessageAsync(
                messageId);

        if (!deleted)
        {
            return NotFound(new
            {
                message = "Message not found."
            });
        }

        return Ok(new
        {
            message = "Message deleted successfully."
        });
    }

    private string? GetCurrentUserId()
    {
        return User.FindFirstValue(
            ClaimTypes.NameIdentifier);
    }

    private bool IsOwner()
    {
        var ownerUserId =
            _configuration["Owner:UserId"];

        var currentUserId =
            GetCurrentUserId();

        return !string.IsNullOrWhiteSpace(ownerUserId)
               &&
               currentUserId == ownerUserId;
    }

    private async Task<bool> CanAccessChat(
        int chatId,
        string userId)
    {
        if (IsOwner())
            return true;

        return await _chatService
            .CanAccessChatAsync(
                chatId,
                userId);
    }

    private async Task<int> GetChatIdFromMessage(
        int messageId)
    {
        return await _chatService
            .GetMessageChatIdAsync(
                messageId) ?? 0;
    }
}