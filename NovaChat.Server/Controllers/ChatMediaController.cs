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
    private const long MaxImageBytes = 10 * 1024 * 1024;
    private const long MaxFileBytes = 25 * 1024 * 1024;
    private const long MaxVoiceBytes = 10 * 1024 * 1024;

    private static readonly HashSet<string> AllowedImages = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".webp", ".gif" };

    private static readonly HashSet<string> AllowedFiles = new(StringComparer.OrdinalIgnoreCase)
    { ".pdf", ".txt", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".zip", ".rar", ".7z", ".csv", ".json", ".mp4", ".mov", ".mkv", ".webm" };

    private readonly ChatService _chatService;
    private readonly IWebHostEnvironment _environment;
    private readonly IHubContext<NovaChat.Server.Hubs.ChatHub> _hub;

    public ChatMediaController(ChatService chatService, IWebHostEnvironment environment, IHubContext<NovaChat.Server.Hubs.ChatHub> hub)
    {
        _chatService = chatService;
        _environment = environment;
        _hub = hub;
    }

    [HttpPost("{chatId}")]
    [RequestSizeLimit(MaxFileBytes)]
    public async Task<IActionResult> Upload(int chatId, IFormFile file, [FromQuery] string type = "file", [FromQuery] double? durationSeconds = null)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();
        var chat = await _chatService.GetChatByIdAsync(chatId);
        if (chat == null || (chat.User1Id != userId && chat.User2Id != userId)) return Forbid();
        if (file == null || file.Length == 0) return BadRequest(new { message = "Please select a file." });

        type = type.Trim().ToLowerInvariant();
        if (type is not ("image" or "file" or "voice")) return BadRequest(new { message = "Invalid media type." });

        var extension = Path.GetExtension(file.FileName);
        if (type == "image" && !AllowedImages.Contains(extension)) return BadRequest(new { message = "Unsupported image type." });
        if (type == "file" && !AllowedFiles.Contains(extension)) return BadRequest(new { message = "Unsupported file type." });
        if (type == "voice" && !string.Equals(file.ContentType, "audio/wav", StringComparison.OrdinalIgnoreCase) && !string.Equals(file.ContentType, "audio/x-wav", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Voice messages must be WAV audio." });

        var maxBytes = type switch { "image" => MaxImageBytes, "voice" => MaxVoiceBytes, _ => MaxFileBytes };
        if (file.Length > maxBytes) return BadRequest(new { message = $"This {type} is too large." });

        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var folder = Path.Combine(root, "uploads", "chat", type);
        Directory.CreateDirectory(folder);
        var storageName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var path = Path.Combine(folder, storageName);
        await using (var stream = System.IO.File.Create(path)) await file.CopyToAsync(stream);

        var envelope = new MediaMessageEnvelope
        {
            Type = type,
            StorageName = $"{type}/{storageName}",
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Size = file.Length,
            DurationSeconds = type == "voice" && durationSeconds.HasValue ? Math.Max(0, durationSeconds.Value) : null
        };

        var message = await _chatService.SendMessageAsync(chatId, userId, envelope.Serialize());
        if (message == null)
        {
            System.IO.File.Delete(path);
            return BadRequest(new { message = "Unable to create media message." });
        }

        var dto = MessageDtoMapper.Map(message);
        await _hub.Clients.Users(chat.User1Id, chat.User2Id).SendAsync("ReceiveMessage", dto);
        return Ok(new { message = "Media sent successfully.", data = dto });
    }

    [HttpGet("{messageId}")]
    public async Task<IActionResult> Get(int messageId)
    {
        var userId = CurrentUserId();
        if (userId == null) return Unauthorized();
        var message = await _chatService.GetMessageByIdAsync(messageId);
        if (message == null || !MediaMessageEnvelope.TryParse(message.Content, out var media) || media == null) return NotFound();
        if (!await _chatService.CanAccessChatAsync(message.ChatId, userId)) return Forbid();

        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var path = Path.Combine(root, "uploads", "chat", media.StorageName.Replace('/', Path.DirectorySeparatorChar));
        if (!System.IO.File.Exists(path)) return NotFound();
        Response.Headers.ContentDisposition = $"inline; filename=\"{Uri.EscapeDataString(media.FileName)}\"";
        Response.Headers.CacheControl = "private, max-age=3600";
        return PhysicalFile(path, media.ContentType, enableRangeProcessing: true);
    }

    private string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
}
