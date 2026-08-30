using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;
using NovaChat.Server.Hubs;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/message-deletion")]
[Authorize]
public class MessageDeletionController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly IHubContext<ChatHub> _hub;
    private readonly IWebHostEnvironment _environment;

    public MessageDeletionController(AppDbContext db, IConfiguration configuration, IHubContext<ChatHub> hub, IWebHostEnvironment environment)
    {
        _db = db;
        _configuration = configuration;
        _hub = hub;
        _environment = environment;
    }

    [HttpDelete("private/{messageId:int}")]
    public async Task<IActionResult> DeletePrivate(int messageId, DeleteMessageDto dto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId)) return Unauthorized();

        var message = await _db.Messages.AsTracking().FirstOrDefaultAsync(m => m.Id == messageId);
        if (message == null) return NotFound(new { message = "Message not found." });

        var chat = await _db.Chats.AsNoTracking().FirstOrDefaultAsync(c =>
            c.Id == message.ChatId && (c.User1Id == userId || c.User2Id == userId));

        if (chat == null && !IsOwner(userId)) return Forbid();

        var mode = dto?.Mode?.Trim().ToLowerInvariant() ?? "me";

        if (mode == "everyone")
        {
            if (!string.Equals(message.SenderId, userId, StringComparison.OrdinalIgnoreCase) && !IsOwner(userId))
                return Forbid();
            if (chat == null) return NotFound(new { message = "Chat not found." });

            var deletedPayload = new
            {
                id = message.Id,
                chatId = message.ChatId,
                senderId = message.SenderId,
                content = message.Content,
                sentAt = message.SentAt
            };

            string? mediaPath = null;
            if (MediaMessageEnvelope.TryParse(message.Content, out var media) && media != null)
            {
                var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
                mediaPath = Path.Combine(root, "uploads", "chat", media.StorageName.Replace('/', Path.DirectorySeparatorChar));
            }

            message.DeletedForEveryone = true;
            message.Content = string.Empty;
            await _db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(mediaPath))
            {
                try
                {
                    if (System.IO.File.Exists(mediaPath)) System.IO.File.Delete(mediaPath);
                }
                catch { }
            }

            await _hub.Clients.Users(chat.User1Id, chat.User2Id).SendAsync("MessageDeleted", deletedPayload);
        }
        else if (mode == "me")
        {
            AddDeletedForUser(message, userId);
            await _db.SaveChangesAsync();
        }
        else
        {
            return BadRequest(new { message = "Mode must be 'me' or 'everyone'." });
        }

        return Ok(new { message = "Message deleted successfully.", mode });
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

    private static List<string> Parse(string value) =>
        string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();
}
