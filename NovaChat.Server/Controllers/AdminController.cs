using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "OwnerOnly")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    public AdminController(AppDbContext db) => _db = db;

    [HttpGet("test")] public IActionResult Test() => Ok(new { message = "Owner access granted." });

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers() => Ok(await _db.Users.AsNoTracking().Select(u => new { u.Id, u.Username, u.DisplayName, u.Email, u.CreatedAt }).OrderBy(u => u.CreatedAt).ToListAsync());

    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        if (!long.TryParse(id, out var userId)) return BadRequest(new { message = "Invalid user ID." });
        var user = await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => new { u.Id, u.Username, u.DisplayName, u.Email, u.CreatedAt }).FirstOrDefaultAsync();
        return user == null ? NotFound(new { message = "User not found." }) : Ok(user);
    }

    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(string id, UpdateUserDto dto)
    {
        if (!long.TryParse(id, out var userId)) return BadRequest(new { message = "Invalid user ID." });
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId); if (user == null) return NotFound(new { message = "User not found." });
        var username = string.IsNullOrWhiteSpace(dto.NewUsername) ? user.Username : dto.NewUsername.Trim().ToLowerInvariant();
        var displayName = dto.DisplayName.Trim(); var email = dto.Email.Trim(); var phone = NormalizePhone(dto.PhoneNumber); var bio = (dto.Bio ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(email)) return BadRequest(new { message = "Display Name and Email are required." });
        if (string.IsNullOrWhiteSpace(username)) return BadRequest(new { message = "Username is required." });
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Username == username && u.Id != userId)) return Conflict(new { message = "This Username is already taken." });
        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Email == email && u.Id != userId)) return Conflict(new { message = "This Email is already registered." });
        if (!string.IsNullOrWhiteSpace(phone) && await _db.Users.AsNoTracking().AnyAsync(u => u.PhoneNumber == phone && u.Id != userId)) return Conflict(new { message = "This Phone Number is already registered." });
        user.Username = username; user.DisplayName = displayName; user.Email = email; user.PhoneNumber = phone; user.Bio = bio; await _db.SaveChangesAsync();
        return Ok(new { message = "User updated successfully.", user = new { user.Id, user.Username, user.DisplayName, user.Email, user.PhoneNumber, user.Bio, user.AvatarUrl, user.CreatedAt } });
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        if (!long.TryParse(id, out var userId)) return BadRequest(new { message = "Invalid user ID." });
        if (long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var ownerId) && ownerId == userId) return BadRequest(new { message = "Owner cannot delete the Owner account." });
        var user = await _db.Users.FindAsync(userId); if (user == null) return NotFound(new { message = "User not found." });
        _db.Users.Remove(user); await _db.SaveChangesAsync(); return Ok(new { message = "User deleted successfully." });
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview() => Ok(new { status = "Online", users = await _db.Users.CountAsync(), serverTime = DateTime.UtcNow });
    [HttpGet("settings")] public IActionResult GetSettings() => Ok(new { serverName = "NovaChat Server", ownerAccess = true, status = "Online" });

    private static string? NormalizePhone(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("(", string.Empty).Replace(")", string.Empty);
}
