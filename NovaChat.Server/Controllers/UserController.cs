using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using NovaChat.Server.DTOs;
using NovaChat.Server.Hubs;
using NovaChat.Server.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private const long MaxAvatarBytes = 5 * 1024 * 1024;
    private readonly UserService _userService;
    private readonly JwtService _jwtService;
    private readonly IWebHostEnvironment _environment;
    private readonly IHubContext<ChatHub> _hub;

    public UserController(UserService userService, JwtService jwtService, IWebHostEnvironment environment, IHubContext<ChatHub> hub)
    {
        _userService = userService;
        _jwtService = jwtService;
        _environment = environment;
        _hub = hub;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        var result = await _userService.RegisterAsync(dto);
        if (!result.Success) return Conflict(new { message = result.Message });
        return Ok(new { message = result.Message, user = result.User });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var user = await _userService.LoginAsync(dto);
        if (user == null) return Unauthorized(new { message = "Invalid username/phone number or password." });
        var token = _jwtService.GenerateToken(user);
        return Ok(new { message = "Login successful.", token, user = await _userService.GetUserByIdAsync(user.Id) });
    }

    [Authorize]
    [HttpGet("profile/me")]
    public async Task<IActionResult> GetMyProfile()
    {
        var currentUserId = CurrentUserId();
        if (currentUserId == null) return Unauthorized();
        var user = await _userService.GetUserByIdAsync(currentUserId, includePhoneNumber: true);
        return user == null ? NotFound(new { message = "User not found." }) : Ok(user);
    }

    [Authorize]
    [HttpGet("profile/{id}")]
    public async Task<IActionResult> GetPublicProfile(string id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        return user == null ? NotFound(new { message = "User not found." }) : Ok(user);
    }

    [Authorize]
    [HttpGet("profile/{id}/avatar")]
    public async Task<IActionResult> GetAvatar(string id)
    {
        var user = await _userService.GetUserByIdAsync(id);
        if (user == null || string.IsNullOrWhiteSpace(user.AvatarUrl)) return NotFound();

        var fileName = Path.GetFileName(user.AvatarUrl);
        if (string.IsNullOrWhiteSpace(fileName)) return NotFound();

        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var path = Path.Combine(root, "uploads", "avatars", fileName);
        if (!System.IO.File.Exists(path)) return NotFound();

        Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
        Response.Headers.Pragma = "no-cache";
        Response.Headers.Expires = "0";
        Response.Headers.ContentDisposition = "inline";
        return PhysicalFile(path, "image/jpeg", enableRangeProcessing: true);
    }

    [Authorize]
    [HttpGet("search")]
    public async Task<IActionResult> Search([FromQuery] string q)
    {
        var currentUserId = CurrentUserId();
        if (currentUserId == null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<UserResponseDto>());
        return Ok(await _userService.SearchUsersAsync(q, currentUserId));
    }

    [Authorize]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        if (!IsOwner() && !IsCurrentUser(id)) return Forbid();
        var user = await _userService.GetUserByIdAsync(id, includePhoneNumber: IsCurrentUser(id) || IsOwner());
        return user == null ? NotFound(new { message = "User not found." }) : Ok(user);
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(string id, UpdateUserDto dto)
    {
        if (!IsOwner() && !IsCurrentUser(id)) return Forbid();

        var result = await _userService.UpdateUserAsync(id, dto);
        if (!result.Success)
        {
            if (result.Message == "User not found.") return NotFound(new { message = result.Message });
            return Conflict(new { message = result.Message });
        }

        await _hub.Clients.All.SendAsync("ProfileUpdated", new
        {
            userId = id,
            username = result.User?.Username ?? string.Empty,
            displayName = result.User?.DisplayName ?? string.Empty,
            bio = result.User?.Bio ?? string.Empty,
            avatarUrl = result.User?.AvatarUrl
        });

        return Ok(new { message = result.Message, user = result.User });
    }

    [Authorize]
    [HttpPost("{id}/avatar")]
    [RequestSizeLimit(MaxAvatarBytes)]
    public async Task<IActionResult> UploadAvatar(string id, IFormFile file)
    {
        if (!IsCurrentUser(id)) return Forbid();
        if (file == null || file.Length == 0) return BadRequest(new { message = "Please select an image." });
        if (file.Length > MaxAvatarBytes) return BadRequest(new { message = "Profile picture must be 5 MB or smaller." });

        var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType, StringComparer.OrdinalIgnoreCase))
            return BadRequest(new { message = "Only JPG, PNG and WebP images are supported." });

        try
        {
            await using var input = file.OpenReadStream();
            using var image = await Image.LoadAsync(input);
            if (image.Width < 64 || image.Height < 64)
                return BadRequest(new { message = "Image must be at least 64x64 pixels." });

            var avatarsDirectory = Path.Combine(_environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot"), "uploads", "avatars");
            Directory.CreateDirectory(avatarsDirectory);
            var fileName = $"{Guid.NewGuid():N}.jpg";
            var fullPath = Path.Combine(avatarsDirectory, fileName);

            image.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(512, 512), Mode = ResizeMode.Crop }));
            await image.SaveAsJpegAsync(fullPath, new JpegEncoder { Quality = 88 });

            var oldProfile = await _userService.GetUserByIdAsync(id);
            var result = await _userService.SetAvatarAsync(id, $"/uploads/avatars/{fileName}");
            if (!result.Success) return NotFound(new { message = result.Message });
            DeleteStoredAvatar(oldProfile?.AvatarUrl);

            var refreshedUser = await _userService.GetUserByIdAsync(id, includePhoneNumber: true);
            await _hub.Clients.All.SendAsync("ProfileUpdated", new
            {
                userId = id,
                username = refreshedUser?.Username ?? string.Empty,
                displayName = refreshedUser?.DisplayName ?? string.Empty,
                bio = refreshedUser?.Bio ?? string.Empty,
                avatarUrl = refreshedUser?.AvatarUrl
            });
            return Ok(new { message = result.Message, user = refreshedUser });
        }
        catch (UnknownImageFormatException)
        {
            return BadRequest(new { message = "The uploaded file is not a valid image." });
        }
    }

    [Authorize]
    [HttpDelete("{id}/avatar")]
    public async Task<IActionResult> DeleteAvatar(string id)
    {
        if (!IsCurrentUser(id)) return Forbid();
        var result = await _userService.ClearAvatarAsync(id);
        if (!result.Success) return NotFound(new { message = result.Message });
        DeleteStoredAvatar(result.OldAvatarUrl);
        var user = await _userService.GetUserByIdAsync(id, includePhoneNumber: true);
        await _hub.Clients.All.SendAsync("ProfileUpdated", new
        {
            userId = id,
            username = user?.Username ?? string.Empty,
            displayName = user?.DisplayName ?? string.Empty,
            bio = user?.Bio ?? string.Empty,
            avatarUrl = (string?)null
        });
        return Ok(new { message = result.Message, user });
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        if (!IsOwner() && !IsCurrentUser(id)) return Forbid();
        var deleted = await _userService.DeleteUserAsync(id);
        return deleted ? Ok(new { message = "User deleted successfully." }) : NotFound(new { message = "User not found." });
    }

    [Authorize]
    [HttpPut("{id}/password")]
    public async Task<IActionResult> ChangePassword(string id, ChangePasswordDto dto)
    {
        if (!IsOwner() && !IsCurrentUser(id)) return Forbid();
        var result = await _userService.ChangePasswordAsync(id, dto);
        if (!result.Success)
            return result.Message == "User not found." ? NotFound(new { message = result.Message }) : BadRequest(new { message = result.Message });
        return Ok(new { message = result.Message });
    }

    private string? CurrentUserId() => User.FindFirstValue(ClaimTypes.NameIdentifier);
    private bool IsCurrentUser(string id) => string.Equals(CurrentUserId(), id, StringComparison.Ordinal);

    private bool IsOwner()
    {
        var ownerSetting = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Owner:Username"]
            ?? HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Owner:UserId"];

        var currentUsername = User.FindFirst("username")?.Value;
        if (!string.IsNullOrWhiteSpace(ownerSetting) &&
            !string.IsNullOrWhiteSpace(currentUsername) &&
            string.Equals(currentUsername, ownerSetting, StringComparison.OrdinalIgnoreCase))
            return true;

        var currentUserId = CurrentUserId();
        return !string.IsNullOrWhiteSpace(ownerSetting) &&
               !string.IsNullOrWhiteSpace(currentUserId) &&
               string.Equals(currentUserId, ownerSetting, StringComparison.Ordinal);
    }

    private void DeleteStoredAvatar(string? avatarUrl)
    {
        if (string.IsNullOrWhiteSpace(avatarUrl)) return;
        var fileName = Path.GetFileName(avatarUrl);
        if (string.IsNullOrWhiteSpace(fileName)) return;
        var root = _environment.WebRootPath ?? Path.Combine(_environment.ContentRootPath, "wwwroot");
        var path = Path.Combine(root, "uploads", "avatars", fileName);
        if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
    }
}