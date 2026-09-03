using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;
using System.Security.Claims;

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

    [HttpGet("test")]
    public IActionResult Test() => Ok(new { message = "Owner access granted." });

    [HttpGet("users")]
    public async Task<IActionResult> GetUsers()
    {
        var users = await _db.Users.AsNoTracking()
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.CreatedAt })
            .OrderBy(u => u.CreatedAt)
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("users/{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        var user = await _db.Users.AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.CreatedAt })
            .FirstOrDefaultAsync();

        return user == null
            ? NotFound(new { message = "User not found." })
            : Ok(user);
    }

    [HttpPut("users/{id}")]
    public async Task<IActionResult> UpdateUser(string id, UpdateUserDto dto)
    {
        var ownerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.Equals(ownerId, id, StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(dto.NewUserId) &&
            !string.Equals(dto.NewUserId.Trim(), id, StringComparison.Ordinal))
        {
            return BadRequest(new
            {
                message = "The Owner User ID is managed by Owner:UserId and cannot be changed here."
            });
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null)
            return NotFound(new { message = "User not found." });

        var newId = string.IsNullOrWhiteSpace(dto.NewUserId)
            ? id
            : dto.NewUserId.Trim();

        var displayName = dto.DisplayName.Trim();
        var email = dto.Email.Trim();
        var phone = NormalizePhone(dto.PhoneNumber);
        var bio = (dto.Bio ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(email))
            return BadRequest(new { message = "Display Name and Email are required." });

        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Id == newId && u.Id != id))
            return Conflict(new { message = "This User ID is already taken." });

        if (await _db.Users.AsNoTracking().AnyAsync(u => u.Email == email && u.Id != id))
            return Conflict(new { message = "This Email is already registered." });

        // A normal profile edit does not require any special handling.
        if (string.Equals(id, newId, StringComparison.Ordinal))
        {
            user.DisplayName = displayName;
            user.Email = email;
            user.PhoneNumber = phone;
            user.Bio = bio;

            await _db.SaveChangesAsync();

            return Ok(new
            {
                message = "User updated successfully.",
                user
            });
        }

        // User.Id is a primary key referenced by Chats, Messages and Contacts.
        // EF Core cannot safely change that key in-place. We therefore perform a
        // transactional replacement: create the new user, move every reference,
        // then remove the old user.
        await using var transaction = await _db.Database.BeginTransactionAsync();

        try
        {
            // PhoneNumber is UNIQUE in the EF model. If the replacement keeps the
            // same phone number, the old row must temporarily release it before the
            // replacement can be inserted. The same precaution is used for Email
            // because older databases may already have a unique email index.
            var oldEmail = user.Email;
            var oldPhone = user.PhoneNumber;

            var emailIsSame = string.Equals(oldEmail, email, StringComparison.OrdinalIgnoreCase);
            var phoneIsSame = string.Equals(oldPhone, phone, StringComparison.OrdinalIgnoreCase);

            if (emailIsSame)
                user.Email = $"__renaming_{Guid.NewGuid():N}@novachat.local";

            if (phoneIsSame)
                user.PhoneNumber = null;

            if (emailIsSame || phoneIsSame)
                await _db.SaveChangesAsync();

            var replacement = new User
            {
                Id = newId,
                DisplayName = displayName,
                Email = email,
                PhoneNumber = phone,
                PasswordHash = user.PasswordHash,
                Bio = bio,
                AvatarUrl = user.AvatarUrl,
                LastSeenAt = user.LastSeenAt,
                CreatedAt = user.CreatedAt
            };

            _db.Users.Add(replacement);
            await _db.SaveChangesAsync();

            // Move message ownership.
            var messages = await _db.Messages
                .Where(m => m.SenderId == id)
                .ToListAsync();

            foreach (var message in messages)
                message.SenderId = newId;

            // Move both sides of every private chat.
            var chats = await _db.Chats
                .Where(c => c.User1Id == id || c.User2Id == id)
                .ToListAsync();

            foreach (var chat in chats)
            {
                if (chat.User1Id == id)
                    chat.User1Id = newId;

                if (chat.User2Id == id)
                    chat.User2Id = newId;
            }

            // Move contact ownership and contact targets.
            var contacts = await _db.Contacts
                .Where(c => c.OwnerUserId == id || c.ContactUserId == id)
                .ToListAsync();

            foreach (var contact in contacts)
            {
                if (contact.OwnerUserId == id)
                    contact.OwnerUserId = newId;

                if (contact.ContactUserId == id)
                    contact.ContactUserId = newId;
            }

            await _db.SaveChangesAsync();

            // All foreign keys now point at the replacement row, so the old row
            // can be removed without violating the Restrict relationships.
            _db.Users.Remove(user);
            await _db.SaveChangesAsync();

            await transaction.CommitAsync();
            _db.ChangeTracker.Clear();

            var updatedUser = await _db.Users
                .AsNoTracking()
                .FirstAsync(u => u.Id == newId);

            return Ok(new
            {
                message = "User updated successfully. Please sign in again because your User ID changed.",
                user = updatedUser
            });
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync();
            _db.ChangeTracker.Clear();

            return Conflict(new
            {
                message = "The User ID could not be changed because the database rejected the update. No data was changed.",
                detail = ex.InnerException?.Message ?? ex.Message
            });
        }
        catch
        {
            await transaction.RollbackAsync();
            _db.ChangeTracker.Clear();
            throw;
        }
    }

    [HttpDelete("users/{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var ownerId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.Equals(ownerId, id, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new
            {
                message = "Owner cannot delete the Owner account."
            });
        }

        var user = await _db.Users.FindAsync(id);
        if (user == null)
            return NotFound(new { message = "User not found." });

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();

        return Ok(new { message = "User deleted successfully." });
    }

    [HttpGet("overview")]
    public async Task<IActionResult> GetOverview()
    {
        return Ok(new
        {
            status = "Online",
            users = await _db.Users.CountAsync(),
            serverTime = DateTime.UtcNow
        });
    }

    [HttpGet("settings")]
    public IActionResult GetSettings() => Ok(new
    {
        serverName = "NovaChat Server",
        ownerAccess = true,
        status = "Online"
    });

    private static string? NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Trim()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);
    }
}
