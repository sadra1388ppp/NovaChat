using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/message-deletion")]
[Authorize]
public class MessageDeletionController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;

    public MessageDeletionController(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    [HttpDelete("private/{messageId:int}")]
    public async Task<IActionResult> DeletePrivate(int messageId, DeleteMessageDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var message = await _db.Messages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (message == null) return NotFound(new { message = "Message not found." });

        var canAccess = await _db.Chats.AnyAsync(c => c.Id == message.ChatId && (c.User1Id == userId || c.User2Id == userId));
        if (!canAccess && !IsOwner(userId)) return Forbid();

        var mode = dto?.Mode?.Trim().ToLowerInvariant() ?? "me";
        if (mode == "everyone")
        {
            if (message.SenderId != userId && !IsOwner(userId)) return Forbid();
            message.DeletedForEveryone = true;
            message.Content = string.Empty;
        }
        else if (mode == "me")
        {
            AddDeletedForUser(message, userId);
        }
        else
        {
            return BadRequest(new { message = "Mode must be 'me' or 'everyone'." });
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Message deleted successfully.", mode });
    }

    [HttpDelete("group/{messageId:int}")]
    public async Task<IActionResult> DeleteGroup(int messageId, DeleteMessageDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var message = await _db.GroupMessages.FirstOrDefaultAsync(m => m.Id == messageId);
        if (message == null) return NotFound(new { message = "Message not found." });

        var isMember = await _db.GroupMembers.AnyAsync(m => m.GroupId == message.GroupId && m.UserId == userId);
        if (!isMember && !IsOwner(userId)) return Forbid();

        var mode = dto?.Mode?.Trim().ToLowerInvariant() ?? "me";
        if (mode == "everyone")
        {
            if (message.SenderId != userId && !IsOwner(userId)) return Forbid();
            message.DeletedForEveryone = true;
            message.Content = string.Empty;
        }
        else if (mode == "me")
        {
            AddDeletedForUser(message, userId);
        }
        else
        {
            return BadRequest(new { message = "Mode must be 'me' or 'everyone'." });
        }

        await _db.SaveChangesAsync();
        return Ok(new { message = "Group message deleted successfully.", mode });
    }

    private bool IsOwner(string userId) =>
        !string.IsNullOrWhiteSpace(_configuration["Owner:UserId"]) &&
        string.Equals(_configuration["Owner:UserId"], userId, StringComparison.Ordinal);

    private static void AddDeletedForUser(Message message, string userId)
    {
        var values = Parse(message.DeletedForUserIds);
        if (!values.Contains(userId, StringComparer.Ordinal)) values.Add(userId);
        message.DeletedForUserIds = string.Join('|', values);
    }

    private static void AddDeletedForUser(GroupMessage message, string userId)
    {
        var values = Parse(message.DeletedForUserIds);
        if (!values.Contains(userId, StringComparer.Ordinal)) values.Add(userId);
        message.DeletedForUserIds = string.Join('|', values);
    }

    private static List<string> Parse(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();
}