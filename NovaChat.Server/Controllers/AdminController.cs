using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "OwnerOnly")]
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;

    public AdminController(AppDbContext db)
    {
        _db = db;
    }

    // =========================
    // TEST OWNER ACCESS
    // =========================

    [HttpGet("test")]
    public IActionResult Test()
    {
        return Ok(new
        {
            message = "Owner access granted."
        });
    }

    // =========================
    // USERS
    // =========================

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _db.Users
            .AsNoTracking()
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.Email,
                u.CreatedAt
            })
            .OrderBy(u => u.CreatedAt)
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new
            {
                u.Id,
                u.DisplayName,
                u.Email,
                u.CreatedAt
            })
            .FirstOrDefaultAsync();

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        return Ok(user);
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var ownerId = HttpContext.User.FindFirst(
            System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.Equals(ownerId, id, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Owner cannot delete the Owner account."
            });
        }

        var user = await _db.Users.FindAsync(id);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        _db.Users.Remove(user);

        await _db.SaveChangesAsync();

        return Ok(new
        {
            message = "User deleted successfully."
        });
    }

    // =========================
    // SERVER OVERVIEW
    // =========================

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview()
    {
        var userCount = await _db.Users.CountAsync();

        return Ok(new
        {
            status = "Online",
            users = userCount,
            serverTime = DateTime.UtcNow
        });
    }

    // =========================
    // ADMIN SETTINGS
    // =========================

    [HttpGet("settings")]
    public IActionResult GetSettings()
    {
        return Ok(new
        {
            serverName = "NovaChat Server",
            ownerAccess = true,
            status = "Online"
        });
    }
}