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

    public MessageDeletionController(
        AppDbContext db,
        IConfiguration configuration,
        IHubContext<ChatHub> hub,
        IWebHostEnvironment environment)
    {
        _db = db;
        _configuration = configuration;
        _hub = hub;
        _environment = environment;
    }

    // Unified endpoint used by private chats and groups.
    // mode = "me"       -> hide only for the current user.
    // mode = "everyone" -> permanently mark the message deleted for everyone;
    //                      only the sender (or the configured Owner) may do this.
    [HttpDelete("{messageId:int}")]
    public Task<IActionResult> Delete(int messageId, DeleteMessageDto dto)
        => DeleteCoreAsync(messageId, dto);

    // Kept for backwards compatibility with older client builds.
    [HttpDelete("private/{messageId:int}")]
    public Task<IActionResult> DeletePrivate(int messageId, DeleteMessageDto dto)
        => DeleteCoreAsync(messageId, dto);

    private async Task<IActionResult> DeleteCoreAsync(int messageId, DeleteMessageDto? dto)
    {
        if (!TryGetCurrentUserId(out var userId))
            return Unauthorized();

        var message = await _db.Messages
            .AsTracking()
            .FirstOrDefaultAsync(m => m.Id == messageId);

        if (message == null)
            return NotFound(new { message = "Message not found." });

        var chat = await _db.Chats
            .AsNoTracking()
            .Include(c => c.Members)
            .FirstOrDefaultAsync(c => c.Id == message.ChatId);

        if (chat == null)
            return NotFound(new { message = "Chat not found." });

        var owner = IsOwner(userId);
        var isMember = chat.Type == ChatType.Group
            ? chat.Members.Any(m => m.UserId == userId)
            : chat.User1Id == userId || chat.User2Id == userId;

        if (!owner && !isMember)
            return Forbid();

        var mode = dto?.Mode?.Trim().ToLowerInvariant() ?? "me";
        if (mode is not ("me" or "everyone"))
            return BadRequest(new { message = "Mode must be 'me' or 'everyone'." });

        if (mode == "everyone")
        {
            if (!owner && message.SenderId != userId)
                return Forbid();

            var deletedPayload = new
            {
                id = message.Id,
                chatId = message.ChatId,
                senderId = message.SenderId.ToString(),
                content = message.Content,
                sentAt = message.SentAt
            };

            string? mediaPath = null;
            if (MediaMessageEnvelope.TryParse(message.Content, out var media) && media != null)
            {
                var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
                mediaPath = Path.Combine(
                    root,
                    "uploads",
                    "chat",
                    media.StorageName.Replace('/', Path.DirectorySeparatorChar));
            }

            message.DeletedForEveryone = true;
            message.Content = string.Empty;
            await _db.SaveChangesAsync();

            if (!string.IsNullOrWhiteSpace(mediaPath))
            {
                try
                {
                    if (System.IO.File.Exists(mediaPath))
                        System.IO.File.Delete(mediaPath);
                }
                catch
                {
                    // The database state is already correct; a stale media file
                    // must not make the delete request fail.
                }
            }

            var recipients = GetRecipientIds(chat);
            if (recipients.Count > 0)
            {
                await _hub.Clients
                    .Users(recipients)
                    .SendAsync("MessageDeleted", deletedPayload);
            }
        }
        else
        {
            AddDeletedForUser(message, userId.ToString());
            await _db.SaveChangesAsync();
        }

        return Ok(new { message = "Message deleted successfully.", mode });
    }

    private List<string> GetRecipientIds(Chat chat)
    {
        if (chat.Type == ChatType.Group)
            return chat.Members.Select(m => m.UserId.ToString()).Distinct().ToList();

        return new[] { chat.User1Id, chat.User2Id }
            .Where(id => id.HasValue && id.Value > 0)
            .Select(id => id!.Value.ToString())
            .Distinct()
            .ToList();
    }

    private bool IsOwner(long userId)
    {
        var ownerUsername = _configuration["Owner:Username"];
        var currentUsername = User.FindFirst("username")?.Value;

        if (!string.IsNullOrWhiteSpace(ownerUsername) &&
            string.Equals(ownerUsername, currentUsername, StringComparison.OrdinalIgnoreCase))
            return true;

        return long.TryParse(_configuration["Owner:UserId"], out var configuredOwnerId) &&
               configuredOwnerId == userId;
    }

    private static void AddDeletedForUser(Message message, string userId)
    {
        var values = ParseDeletedForUsers(message.DeletedForUserIds);
        if (!values.Contains(userId, StringComparer.Ordinal))
            values.Add(userId);
        message.DeletedForUserIds = string.Join('|', values);
    }

    private static List<string> ParseDeletedForUsers(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.Ordinal)
                .ToList();

    private bool TryGetCurrentUserId(out long userId)
        => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;
}
