using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;
using NovaChat.Server.Hubs;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "OwnerOnly")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<ChatHub> _hub;

    public AdminController(AppDbContext db, IHubContext<ChatHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    [HttpGet("test")]
    public IActionResult Test() => Ok(new { message = "Owner access granted." });

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers() => Ok(await _db.Users.AsNoTracking().Select(u => new { u.Id, u.Username, u.DisplayName, u.Email, u.CreatedAt }).OrderBy(u => u.CreatedAt).ToListAsync());

    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        if (!long.TryParse(id, out var userId)) return BadRequest(new { message = "Invalid user ID." });
        var user = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Id, u.Username, u.DisplayName, u.Email, u.CreatedAt }).FirstOrDefaultAsync();
        return user == null ? NotFound(new { message = "User not found." }) : Ok(user);
    }

    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(string id, UpdateUserDto dto)
    {
        if (!long.TryParse(id, out var userId)) return BadRequest(new { message = "Invalid user ID." });
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId); if (user == null) return NotFound(new { message = "User not found." });
        var username = string.IsNullOrWhiteSpace(dto.NewUsername) ? user.Username : dto.NewUsername.Trim().ToLowerInvariant();
        var displayName = dto.DisplayName.Trim(); var email = dto.Email.Trim(); var phone = NormalizePhone(dto.PhoneNumber); var bio = (dto.Bio ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(email)) return BadRequest(new { message = "Display Name and Email are required." });
        if (string.IsNullOrWhiteSpace(username)) return BadRequest(new { message = "Username is required." });
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Username == username && u.Id != userId)) return Conflict(new { message = "This Username is already taken." });
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Email == email && u.Id != userId)) return Conflict(new { message = "This Email is already registered." });
        if (!string.IsNullOrWhiteSpace(phone) && await _db.Users.AsNoTracking().AnyAsync(u => u.PhoneNumber == phone && u.Id != userId)) return Conflict(new { message = "This Phone Number is already registered." });
        user.Username = username; user.DisplayName = displayName; user.Email = email; user.PhoneNumber = phone; user.Bio = bio; await _db.SaveChangesAsync();
        return Ok(new { message = "User updated successfully.", user = new { user.Id, user.Username, user.DisplayName, user.Email, user.PhoneNumber, user.Bio, user.AvatarUrl, user.CreatedAt } });
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        if (!long.TryParse(id, out var userId)) return BadRequest(new { message = "Invalid user ID." });
        if (long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId) && ownerId == userId) return BadRequest(new { message = "Owner cannot delete the Owner account." });
        var user = await _db.Users.FindAsync(userId); if (user == null) return NotFound(new { message = "User not found." });
        _db.Users.Remove(user); await _db.SaveChangesAsync(); return Ok(new { message = "User deleted successfully." });
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview() => Ok(new { status = "Online", users = await _db.Users.CountAsync(), serverTime = DateTime.UtcNow });

    [HttpGet("settings")]
    public IActionResult GetSettings() => Ok(new { serverName = "NovaChat Server", ownerAccess = true, status = "Online" });

    [HttpGet("users/{id}/chats")]
    public async Task<IActionResult> GetUserChats(string id)
    {
        if (!long.TryParse(id, out var userId) || userId <= 0) return BadRequest(new { message = "Invalid user ID." });
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.Id == userId)) return NotFound(new { message = "User not found." });

        var chats = await _db.Chats.AsNoTracking()
            .Include(c => c.User1)
            .Include(c => c.User2)
            .Where(c => c.User1Id == userId || c.User2Id == userId)
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .ToListAsync();

        var result = new List<AdminChatDto>(chats.Count);
        foreach (var chat in chats)
        {
            var last = await _db.Messages.AsNoTracking()
                .Include(m => m.Sender)
                .Where(m => m.ChatId == chat.Id)
                .OrderByDescending(m => m.SentAt)
                .ThenByDescending(m => m.Id)
                .FirstOrDefaultAsync();

            var count = await _db.Messages.AsNoTracking().CountAsync(m => m.ChatId == chat.Id);
            var other = chat.User1Id == userId ? chat.User2 : chat.User1;

            result.Add(new AdminChatDto
            {
                Id = chat.Id,
                OtherUserId = other.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                OtherUsername = other.Username,
                OtherDisplayName = other.DisplayName,
                OtherAvatarUrl = other.AvatarUrl,
                CreatedAt = chat.CreatedAt,
                MessageCount = count,
                LastMessage = last == null ? null : MessageDtoMapper.Map(last)
            });
        }

        return Ok(result.OrderByDescending(x => x.LastMessage?.SentAt ?? x.CreatedAt).ThenByDescending(x => x.Id).ToList());
    }

    [HttpGet("chats/{chatId}/messages")]
    public async Task<IActionResult> GetAdminChatMessages(int chatId)
    {
        if (chatId <= 0) return BadRequest(new { message = "Invalid chat ID." });
        var exists = await _db.Chats.AsNoTracking().AnyAsync(c => c.Id == chatId);
        if (!exists) return NotFound(new { message = "Chat not found." });

        var messages = await _db.Messages.AsNoTracking()
            .Include(m => m.Sender)
            .Where(m => m.ChatId == chatId)
            .OrderBy(m => m.SentAt)
            .ThenBy(m => m.Id)
            .ToListAsync();

        return Ok(messages.Select(MessageDtoMapper.Map).ToList());
    }

    [HttpPost("chats/{chatId}/messages")]
    public async Task<IActionResult> SendMessageAsUser(int chatId, [FromBody] AdminSendMessageDto dto)
    {
        if (chatId <= 0) return BadRequest(new { message = "Invalid chat ID." });
        if (dto == null || string.IsNullOrWhiteSpace(dto.Content)) return BadRequest(new { message = "Message content is required." });
        if (dto.Content.Length > 4000) return BadRequest(new { message = "Message is too long." });
        if (dto.SenderUserId <= 0) return BadRequest(new { message = "Invalid sender user ID." });

        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat == null) return NotFound(new { message = "Chat not found." });
        if (chat.User1Id != dto.SenderUserId && chat.User2Id != dto.SenderUserId) return BadRequest(new { message = "The selected user is not a member of this chat." });

        var sender = await _db.Users.FirstOrDefaultAsync(u => u.Id == dto.SenderUserId);
        if (sender == null) return NotFound(new { message = "Sender user not found." });

        var message = new Message
        {
            ChatId = chatId,
            SenderId = sender.Id,
            Content = dto.Content.Trim(),
            SentAt = DateTime.UtcNow
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync();
        await _db.Entry(message).Reference(m => m.Sender).LoadAsync();

        var mapped = MessageDtoMapper.Map(message);
        await _hub.Clients.Users(chat.User1Id.ToString(), chat.User2Id.ToString()).SendAsync("ReceiveMessage", mapped);
        return Ok(new { message = "Message sent as the selected user.", data = mapped });
    }

    [HttpPut("messages/{messageId}")]
    public async Task<IActionResult> EditMessage(int messageId, [FromBody] AdminEditMessageDto dto)
    {
        if (messageId <= 0) return BadRequest(new { message = "Invalid message ID." });
        if (dto == null || string.IsNullOrWhiteSpace(dto.Content)) return BadRequest(new { message = "Message content is required." });
        if (dto.Content.Length > 4000) return BadRequest(new { message = "Message is too long." });

        var message = await _db.Messages.Include(m => m.Sender).FirstOrDefaultAsync(m => m.Id == messageId);
        if (message == null) return NotFound(new { message = "Message not found." });
        if (message.DeletedForEveryone) return BadRequest(new { message = "Deleted messages cannot be edited." });

        message.Content = dto.Content.Trim();
        await _db.SaveChangesAsync();

        var mapped = MessageDtoMapper.Map(message);
        var chat = await _db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == message.ChatId);
        if (chat != null)
            await _hub.Clients.Users(chat.User1Id.ToString(), chat.User2Id.ToString()).SendAsync("MessageEdited", mapped);

        return Ok(new { message = "Message edited successfully.", data = mapped });
    }

    [HttpDelete("messages/{messageId}")]
    public async Task<IActionResult> DeleteMessage(int messageId)
    {
        if (messageId <= 0) return BadRequest(new { message = "Invalid message ID." });
        var message = await _db.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (message == null) return NotFound(new { message = "Message not found." });

        var chat = await _db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == message.ChatId);
        if (chat == null) return NotFound(new { message = "Chat not found." });

        var payload = new
        {
            id = message.Id,
            chatId = message.ChatId,
            senderId = message.SenderId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            content = message.Content,
            sentAt = message.SentAt
        };

        _db.Messages.Remove(message);
        await _db.SaveChangesAsync();
        await _hub.Clients.Users(chat.User1Id.ToString(), chat.User2Id.ToString()).SendAsync("MessageDeleted", payload);

        return Ok(new { message = "Message deleted successfully." });
    }

    private static string? NormalizePhone(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("(", string.Empty).Replace(")", string.Empty);

    public sealed class AdminChatDto
    {
        public int Id { get; set; }
        public string OtherUserId { get; set; } = string.Empty;
        public string OtherUsername { get; set; } = string.Empty;
        public string OtherDisplayName { get; set; } = string.Empty;
        public string? OtherAvatarUrl { get; set; }
        public DateTime CreatedAt { get; set; }
        public int MessageCount { get; set; }
        public MessageDto? LastMessage { get; set; }
    }

    public sealed class AdminSendMessageDto
    {
        public long SenderUserId { get; set; }
        public string Content { get; set; } = string.Empty;
    }

    public sealed class AdminEditMessageDto
    {
        public string Content { get; set; } = string.Empty;
    }
}
