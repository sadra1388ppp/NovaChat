using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.Entities;
using NovaChat.Server.Hubs;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class GroupManagementController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<ChatHub> _hub;

    public GroupManagementController(AppDbContext db, IHubContext<ChatHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    [HttpPut("{chatId:int}/members/{userId:long}/role")]
    public async Task<IActionResult> SetMemberRole(int chatId, long userId, [FromBody] SetGroupMemberRoleRequest request)
    {
        if (!TryGetCurrentUserId(out var actorId)) return Unauthorized();

        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.Id == chatId && c.Type == (int)ChatType.Group);
        if (chat == null) return NotFound(new { message = "Group not found." });

        var actor = await _db.ChatMembers.FirstOrDefaultAsync(m => m.ChatId == chatId && m.UserId == actorId);
        var target = await _db.ChatMembers.FirstOrDefaultAsync(m => m.ChatId == chatId && m.UserId == userId);
        if (actor == null || actor.Role != (int)ChatMemberRole.Owner) return Forbid();
        if (target == null) return NotFound(new { message = "Member not found." });
        if (target.Role == (int)ChatMemberRole.Owner) return BadRequest(new { message = "The group owner cannot be changed to another role." });

        target.Role = request?.IsAdmin == true ? (int)ChatMemberRole.Admin : (int)ChatMemberRole.Member;
        await _db.SaveChangesAsync();

        var recipients = await _db.ChatMembers.Where(m => m.ChatId == chatId).Select(m => m.UserId.ToString()).ToListAsync();
        await _hub.Clients.Users(recipients).SendAsync("GroupUpdated", new { Id = chat.Id, Type = "Group", Name = chat.Name, AvatarUrl = chat.AvatarUrl, CreatedByUserId = chat.CreatedByUserId?.ToString() ?? string.Empty });

        return Ok(new { message = target.Role == (int)ChatMemberRole.Admin ? "Member promoted to admin." : "Admin privileges removed." });
    }

    [HttpPost("{chatId:int}/leave")]
    public async Task<IActionResult> LeaveGroup(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        var chat = await _db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chatId && c.Type == (int)ChatType.Group);
        if (chat == null) return NotFound(new { message = "Group not found." });

        var member = await _db.ChatMembers.FirstOrDefaultAsync(m => m.ChatId == chatId && m.UserId == userId);
        if (member == null) return Forbid();
        if (member.Role == (int)ChatMemberRole.Owner) return BadRequest(new { message = "The owner cannot leave the group. Delete the group from Group Info instead." });

        _db.ChatMembers.Remove(member);
        await _db.SaveChangesAsync();

        var recipients = await _db.ChatMembers.Where(m => m.ChatId == chatId).Select(m => m.UserId.ToString()).ToListAsync();
        await _hub.Clients.Users(recipients.Append(userId.ToString()).Distinct()).SendAsync("ChatMemberRemoved", new { chatId, userId = userId.ToString() });

        return Ok(new { message = "You left the group." });
    }

    [HttpDelete("{chatId:int}")]
    public async Task<IActionResult> DeleteGroup(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        var chat = await _db.Chats.FirstOrDefaultAsync(c => c.Id == chatId && c.Type == (int)ChatType.Group);
        if (chat == null) return NotFound(new { message = "Group not found." });

        var owner = await _db.ChatMembers.FirstOrDefaultAsync(m => m.ChatId == chatId && m.UserId == userId && m.Role == (int)ChatMemberRole.Owner);
        if (owner == null) return Forbid();

        var recipients = await _db.ChatMembers.Where(m => m.ChatId == chatId).Select(m => m.UserId.ToString()).ToListAsync();
        _db.Chats.Remove(chat);
        await _db.SaveChangesAsync();

        await _hub.Clients.Users(recipients).SendAsync("ChatDeleted", new { chatId, deletedBy = userId.ToString() });
        return Ok(new { message = "Group deleted successfully." });
    }

    public sealed class SetGroupMemberRoleRequest
    {
        public bool IsAdmin { get; set; }
    }

    private bool TryGetCurrentUserId(out long userId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
}
