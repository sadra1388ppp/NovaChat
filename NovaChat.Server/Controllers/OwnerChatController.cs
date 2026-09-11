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

        var members = await _db.ChatMembers
            .AsNoTracking()
            .Where(m => m.ChatId == chatId)
            .Include(m => m.User)
            .OrderByDescending(m => m.Role)
            .ThenBy(m => m.User.DisplayName)
            .ToListAsync();

        var result = members
            .Select(m => new OwnerMemberDto
            {
                UserId = m.UserId.ToString(),
                Username = m.User.Username,
                DisplayName = m.User.DisplayName,
                Role = m.Role.ToString().ToUpperInvariant(),
                Email = m.User.Email,
                PhoneNumber = m.User.PhoneNumber,
                AvatarUrl = m.User.AvatarUrl
            })
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
            .Distinct()
            .ToListAsync();

        _db.Chats.Remove(chat);
        await _db.SaveChangesAsync();

        if (recipients.Count > 0)
        {
            await _hub.Clients.Users(recipients)
                .SendAsync("ChatDeleted", new { chatId, deletedBy = "owner" });
        }

        return Ok(new
        {
            message = chat.Type == ChatType.Group
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
