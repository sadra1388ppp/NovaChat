using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;
using NovaChat.Server.Hubs;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "OwnerOnly")]
public class OwnerChatController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<ChatHub> _hub;

    public OwnerChatController(AppDbContext db, IHubContext<ChatHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    [HttpGet("{chatId:int}/members")]
    public async Task<IActionResult> GetMembers(int chatId)
    {
        var chat = await _db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat == null)
            return NotFound(new { message = "Chat not found." });

        if (chat.Type != (int)ChatType.Group)
        {
            var userIds = new[] { chat.User1Id, chat.User2Id }
                .Where(id => id.HasValue && id.Value > 0)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            var users = await _db.Users
                .AsNoTracking()
                .Where(u => userIds.Contains(u.Id))
                .Select(u => new OwnerMemberDto
                {
                    UserId = u.Id.ToString(),
                    Username = u.Username,
                    DisplayName = u.DisplayName,
                    Role = "MEMBER",
                    Email = u.Email,
                    PhoneNumber = u.PhoneNumber,
                    AvatarUrl = u.AvatarUrl
                })
                .OrderBy(x => x.DisplayName)
                .ToListAsync();

            return Ok(users);
        }

        var members = await _db.ChatMembers
            .AsNoTracking()
            .Where(m => m.ChatId == chatId)
            .Include(m => m.User)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.User.DisplayName)
            .ToListAsync();

        var result = members
            .Select(m => new OwnerMemberDto
            {
                UserId = m.UserId.ToString(),
                Username = m.User.Username,
                DisplayName = m.User.DisplayName,
                Role = ((ChatMemberRole)m.Role).ToString().ToUpperInvariant(),
                Email = m.User.Email,
                PhoneNumber = m.User.PhoneNumber,
                AvatarUrl = m.User.AvatarUrl
            })
            .OrderBy(m => m.Role == "OWNER" ? 0 : m.Role == "ADMIN" ? 1 : 2)
            .ThenBy(m => m.DisplayName)
            .ToList();

        return Ok(result);
    }

    [HttpGet("{chatId:int}/messages")]
    public async Task<IActionResult> GetMessages(int chatId, [FromQuery] int pageSize = 100)
    {
        var exists = await _db.Chats.AsNoTracking().AnyAsync(c => c.Id == chatId);
        if (!exists)
            return NotFound(new { message = "Chat not found." });

        pageSize = Math.Clamp(pageSize, 1, 200);
        var messages = await _db.Messages
            .AsNoTracking()
            .Include(m => m.Sender)
            .Where(m => m.ChatId == chatId && !m.DeletedForEveryone)
            .OrderBy(m => m.SentAt)
            .ThenBy(m => m.Id)
            .Take(pageSize)
            .ToListAsync();

        return Ok(new
        {
            messages = messages.Select(m => MessageDtoMapper.Map(m)).ToList(),
            count = messages.Count
        });
    }

    [HttpDelete("{chatId:int}")]
    public async Task<IActionResult> DeleteChat(int chatId)
    {
        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat == null)
            return NotFound(new { message = "Chat not found." });

        var recipients = await _db.ChatMembers
            .AsNoTracking()
            .Where(m => m.ChatId == chatId)
            .Select(m => m.UserId.ToString())
            .ToListAsync();

        if (chat.User1Id.HasValue && chat.User1Id.Value > 0)
            recipients.Add(chat.User1Id.Value.ToString());
        if (chat.User2Id.HasValue && chat.User2Id.Value > 0)
            recipients.Add(chat.User2Id.Value.ToString());

        var messages = await _db.Messages.Where(m => m.ChatId == chatId).ToListAsync();
        if (messages.Count > 0)
            _db.Messages.RemoveRange(messages);

        var members = await _db.ChatMembers.Where(m => m.ChatId == chatId).ToListAsync();
        if (members.Count > 0)
            _db.ChatMembers.RemoveRange(members);

        _db.Chats.Remove(chat);
        await _db.SaveChangesAsync();

        var distinctRecipients = recipients.Distinct().ToList();
        if (distinctRecipients.Count > 0)
        {
            await _hub.Clients.Users(distinctRecipients)
                .SendAsync("ChatDeleted", new { chatId, deletedBy = "owner" });
        }

        return Ok(new
        {
            message = chat.Type == (int)ChatType.Group
                ? "Group deleted successfully."
                : "Conversation deleted successfully."
        });
    }

    private sealed class OwnerMemberDto
    {
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public string? AvatarUrl { get; set; }
    }
}
