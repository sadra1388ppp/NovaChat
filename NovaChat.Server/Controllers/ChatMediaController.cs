using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NovaChat.Server.DTOs;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ChatMediaController : ControllerBase
{
    private const long MaxEncryptedMediaBytes = 26 * 1024 * 1024;
    private readonly ChatService _chatService;
    private readonly E2eeDeviceService _e2eeDeviceService;
    private readonly IWebHostEnvironment _environment;
    private readonly IHubContext<NovaChat.Server.Hubs.ChatHub> _hub;

    public ChatMediaController(
        ChatService chatService,
        E2eeDeviceService e2eeDeviceService,
        IWebHostEnvironment environment,
        IHubContext<NovaChat.Server.Hubs.ChatHub> hub)
    {
        _chatService = chatService;
        _e2eeDeviceService = e2eeDeviceService;
        _environment = environment;
        _hub = hub;
    }

    [HttpPost("{chatId}")]
    [RequestSizeLimit(MaxEncryptedMediaBytes)]
    public async Task<IActionResult> Upload(
        int chatId,
        IFormFile file,
        [FromForm] string blobId,
        [FromForm] string envelope)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null || !await _chatService.CanAccessChatAsync(chatId, userId.Value))
            return Forbid();

        if (file == null || file.Length == 0)
            return BadRequest(new { message = "Encrypted media is missing." });
        if (file.Length > MaxEncryptedMediaBytes)
            return BadRequest(new { message = "Encrypted media is too large." });
        if (string.IsNullOrWhiteSpace(blobId) || !Guid.TryParseExact(blobId, "N", out _))
            return BadRequest(new { message = "Invalid encrypted media identifier." });
        if (!E2eeMediaMessageEnvelope.TryParse(envelope, out var parsedEnvelope) || parsedEnvelope == null)
            return BadRequest(new { message = "Invalid end-to-end encrypted media envelope." });
        if (!string.Equals(parsedEnvelope.BlobId, blobId, StringComparison.Ordinal))
            return BadRequest(new { message = "Encrypted media identifier mismatch." });

        var devices = await _e2eeDeviceService.GetChatDevicesAsync(chatId, userId.Value);
        var activeDeviceIds = devices.Select(d => d.DeviceId).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
        if (activeDeviceIds.Count == 0)
            return BadRequest(new { message = "No trusted encryption devices are registered for this conversation. Ask every participant to open NovaChat once." });

        var envelopeDeviceIds = parsedEnvelope.Keys.Keys.ToHashSet(StringComparer.Ordinal);
        if (!activeDeviceIds.SetEquals(envelopeDeviceIds))
            return BadRequest(new { message = "The encrypted media recipient list is out of date. Open NovaChat on every conversation device and try again." });

        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var folder = Path.Combine(root, "uploads", "chat", "e2ee");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{blobId}.enc");

        try
        {
            await using (var stream = System.IO.File.Create(path))
                await file.CopyToAsync(stream);

            var message = await _chatService.SendMessageAsync(chatId, userId.Value, envelope);
            if (message == null)
            {
                System.IO.File.Delete(path);
                return BadRequest(new { message = "Unable to create encrypted media message." });
            }

            var dto = MessageDtoMapper.Map(message);
            var recipients = devices.Select(d => d.UserId.ToString()).Distinct(StringComparer.Ordinal).ToArray();
            await _hub.Clients.Users(recipients).SendAsync("ReceiveMessage", dto);
            return Ok(new { message = "Encrypted media sent successfully.", data = dto });
        }
        catch
        {
            try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); } catch { }
            throw;
        }
    }

    [HttpGet("{messageId}/envelope")]
    public async Task<IActionResult> GetEnvelope(int messageId)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var message = await _chatService.GetMessageByIdAsync(messageId);
        if (message == null || !await _chatService.CanAccessChatAsync(message.ChatId, userId.Value))
            return Forbid();
        if (!E2eeMediaMessageEnvelope.TryParse(message.Content, out _))
            return NotFound();

        return Ok(new { envelope = message.Content });
    }

    [HttpGet("{messageId}")]
    public async Task<IActionResult> Get(int messageId)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();

        var message = await _chatService.GetMessageByIdAsync(messageId);
        if (message == null || !await _chatService.CanAccessChatAsync(message.ChatId, userId.Value))
            return Forbid();

        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");

        if (E2eeMediaMessageEnvelope.TryParse(message.Content, out var encrypted) && encrypted != null)
        {
            var path = Path.Combine(root, "uploads", "chat", "e2ee", $"{encrypted.BlobId}.enc");
            if (!System.IO.File.Exists(path)) return NotFound();
            Response.Headers.CacheControl = "private, max-age=3600";
            return PhysicalFile(path, "application/octet-stream", enableRangeProcessing: true);
        }

        if (MediaMessageEnvelope.TryParse(message.Content, out var media) && media != null)
        {
            var relative = media.StorageName.Replace('/', Path.DirectorySeparatorChar);
            var uploadRoot = Path.GetFullPath(Path.Combine(root, "uploads", "chat"));
            var path = Path.GetFullPath(Path.Combine(uploadRoot, relative));
            var rootWithSeparator = uploadRoot.EndsWith(Path.DirectorySeparatorChar)
                ? uploadRoot
                : uploadRoot + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) || !System.IO.File.Exists(path))
                return NotFound();

            Response.Headers.ContentDisposition = $"inline; filename=\"{Uri.EscapeDataString(media.FileName)}\"";
            Response.Headers.CacheControl = "private, max-age=3600";
            return PhysicalFile(path, media.ContentType, enableRangeProcessing: true);
        }

        return NotFound();
    }

    private long? CurrentUserId() =>
        long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) && userId > 0
            ? userId
            : null;
}
