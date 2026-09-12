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
    private readonly AppDbContext _db; private readonly IHubContext<ChatHub> _hub;
    public OwnerChatController(AppDbContext db, IHubContext<ChatHub> hub) { _db = db; _hub = hub; }
    [HttpGet("{chatId:int}/members")]
    public async Task<IActionResult> GetMembers(int chatId)
    {
        var chat = await _db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chatId); if (chat == null) return NotFound(new { message = "Chat not found." });
        var usernames = ParseMembers(chat.Members); if (usernames.Count == 0) return Ok(Array.Empty<OwnerMemberDto>());
        var users = await _db.Users.AsNoTracking().Where(u => usernames.Contains(u.Username)).ToListAsync();
        var creatorUsername = chat.CreatedByUserId.HasValue ? await _db.Users.AsNoTracking().Where(u => u.Id == chat.CreatedByUserId.Value).Select(u => u.Username).FirstOrDefaultAsync() : null;
        var result = users.Select(user => new OwnerMemberDto { UserId = user.Id.ToString(), Username = user.Username, DisplayName = user.DisplayName, Role = string.Equals(user.Username, creatorUsername, StringComparison.OrdinalIgnoreCase) ? "OWNER" : "MEMBER", Email = user.Email, PhoneNumber = user.PhoneNumber, AvatarUrl = user.AvatarUrl }).OrderBy(m => m.Role == "OWNER" ? 0 : 1).ThenBy(m => m.DisplayName).ToList(); return Ok(result);
    }
    [HttpGet("{chatId:int}/messages")]
    public async Task<IActionResult> GetMessages(int chatId, [FromQuery] int pageSize = 100)
    {
        var exists = await _db.Chats.AsNoTracking().AnyAsync(c => c.Id == chatId); if (!exists) return NotFound(new { message = "Chat not found." }); pageSize = Math.Clamp(pageSize, 1, 200);
        var messages = await _db.Messages.AsNoTracking().Where(m => m.ChatId == chatId && !m.DeletedForEveryone).OrderBy(m => m.SentAt).ThenBy(m => m.Id).Take(pageSize).ToListAsync();
        var result = messages.Select(m => new
        {
            m.Id,
            m.ChatId,
            SenderId = m.SenderId,
            SentAt = m.SentAt,
            Content = "[Encrypted message — content unavailable to administrators]",
            Encrypted = true
        }).ToList();
        return Ok(new { messages = result, count = result.Count });
    }
    [HttpDelete("{chatId:int}")]
    public async Task<IActionResult> DeleteChat(int chatId)
    {
        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.Id == chatId); if (chat == null) return NotFound(new { message = "Chat not found." });
        var usernames = ParseMembers(chat.Members); var recipients = await _db.Users.AsNoTracking().Where(u => usernames.Contains(u.Username)).Select(u => u.Id.ToString()).ToListAsync();
        var messages = await _db.Messages.Where(m => m.ChatId == chatId).ToListAsync(); if (messages.Count > 0) _db.Messages.RemoveRange(messages); _db.Chats.Remove(chat); await _db.SaveChangesAsync();
        var distinctRecipients = recipients.Distinct().ToList(); if (distinctRecipients.Count > 0) await _hub.Clients.Users(distinctRecipients).SendAsync("ChatDeleted", new { chatId, deletedBy = "owner" });
        return Ok(new { message = chat.Type == ChatType.Group ? "Group deleted successfully." : "Conversation deleted successfully." });
    }
    private static List<string> ParseMembers(string? members) => string.IsNullOrWhiteSpace(members) ? [] : members.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    private sealed class OwnerMemberDto { public string UserId { get; set; } = string.Empty; public string Username { get; set; } = string.Empty; public string DisplayName { get; set; } = string.Empty; public string Role { get; set; } = string.Empty; public string Email { get; set; } = string.Empty; public string? PhoneNumber { get; set; } public string? AvatarUrl { get; set; } }
}
