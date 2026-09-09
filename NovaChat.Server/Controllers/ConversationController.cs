using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.Entities;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/[controller]")]
public class ConversationController : ControllerBase
{
    private readonly AppDbContext _db;

    public ConversationController(AppDbContext db) => _db = db;

    [HttpDelete("{chatId:int}")]
    public async Task<IActionResult> HidePrivateConversation(int chatId)
    {
        if (!TryGetCurrentUserId(out var userId)) return Unauthorized();

        var chat = await _db.Chats.AsNoTracking().FirstOrDefaultAsync(c => c.Id == chatId);
        if (chat == null) return NotFound(new { message = "Conversation not found." });
        if (chat.Type != (int)ChatType.Private) return BadRequest(new { message = "Group chats must be left or deleted from Group Info." });

        var member = await _db.ChatMembers.FirstOrDefaultAsync(m => m.ChatId == chatId && m.UserId == userId);
        if (member == null) return Forbid();

        _db.ChatMembers.Remove(member);
        await _db.SaveChangesAsync();

        return Ok(new { message = "Conversation removed from your chat list." });
    }

    private bool TryGetCurrentUserId(out long userId) =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
}
