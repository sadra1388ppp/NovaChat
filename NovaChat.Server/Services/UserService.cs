using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public class UserService
{
    private readonly AppDbContext _context;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly PresenceService _presenceService;

    public UserService(AppDbContext context, IPasswordHasher<User> passwordHasher, PresenceService presenceService)
    {
        _context = context;
        _passwordHasher = passwordHasher;
        _presenceService = presenceService;
    }

    public async Task<RegisterResult> RegisterAsync(RegisterDto dto)
    {
        dto.Id = dto.Id.Trim(); dto.Email = dto.Email.Trim(); dto.DisplayName = dto.DisplayName.Trim();
        if (await _context.Users.AnyAsync(u => u.Id == dto.Id)) return new RegisterResult { Success = false, Message = "This User ID is already taken." };
        if (await _context.Users.AnyAsync(u => u.Email == dto.Email)) return new RegisterResult { Success = false, Message = "This Email is already registered." };
        var user = new User { Id = dto.Id, DisplayName = dto.DisplayName, Email = dto.Email, CreatedAt = DateTime.UtcNow };
        user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);
        _context.Users.Add(user); await _context.SaveChangesAsync();
        return new RegisterResult { Success = true, Message = "User registered successfully.", User = ToUserResponse(user) };
    }

    public async Task<User?> LoginAsync(LoginDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == dto.Id);
        if (user == null) return null;
        var passwordResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
        return passwordResult == PasswordVerificationResult.Failed ? null : user;
    }

    public async Task<UserResponseDto?> GetUserByIdAsync(string id)
    {
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
        return user == null ? null : ToUserResponse(user);
    }

    public async Task<List<UserResponseDto>> SearchUsersAsync(string query, string currentUserId)
    {
        query = query.Trim(); if (query.Length < 1) return [];
        var users = await _context.Users.AsNoTracking().Where(u => u.Id != currentUserId &&
            (EF.Functions.ILike(u.Id, $"%{query}%") || EF.Functions.ILike(u.DisplayName, $"%{query}%") || EF.Functions.ILike(u.Email, $"%{query}%")))
            .OrderBy(u => u.DisplayName).Take(30).ToListAsync();
        return users.Select(ToUserResponse).ToList();
    }

    public async Task<RegisterResult> UpdateUserAsync(string id, UpdateUserDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return new RegisterResult { Success = false, Message = "User not found." };

        var newId = string.IsNullOrWhiteSpace(dto.NewUserId) ? id : dto.NewUserId.Trim();
        dto.DisplayName = dto.DisplayName.Trim();
        dto.Email = dto.Email.Trim();
        dto.Bio = (dto.Bio ?? string.Empty).Trim();

        if (await _context.Users.AsNoTracking().AnyAsync(u => u.Id == newId && u.Id != id))
            return new RegisterResult { Success = false, Message = "This User ID is already taken." };
        if (await _context.Users.AsNoTracking().AnyAsync(u => u.Email == dto.Email && u.Id != id))
            return new RegisterResult { Success = false, Message = "This Email is already registered." };

        if (!string.Equals(id, newId, StringComparison.Ordinal))
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO \"Users\" (\"Id\", \"DisplayName\", \"Email\", \"PasswordHash\", \"Bio\", \"AvatarUrl\", \"LastSeenAt\", \"CreatedAt\") SELECT {newId}, \"DisplayName\", \"Email\", \"PasswordHash\", \"Bio\", \"AvatarUrl\", \"LastSeenAt\", \"CreatedAt\" FROM \"Users\" WHERE \"Id\" = {id}");
                await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Users\" SET \"DisplayName\" = {dto.DisplayName}, \"Email\" = {dto.Email}, \"Bio\" = {dto.Bio} WHERE \"Id\" = {newId}");

                await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Messages\" SET \"SenderId\" = {newId} WHERE \"SenderId\" = {id}");
                await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Chats\" SET \"User1Id\" = {newId} WHERE \"User1Id\" = {id}");
                await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Chats\" SET \"User2Id\" = {newId} WHERE \"User2Id\" = {id}");
                await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Contacts\" SET \"OwnerUserId\" = {newId} WHERE \"OwnerUserId\" = {id}");
                await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Contacts\" SET \"ContactUserId\" = {newId} WHERE \"ContactUserId\" = {id}");

                // Groups.CreatorId references Users.Id with RESTRICT, so it must be moved before deleting the old user.
                await _context.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"Groups\" SET \"CreatorId\" = {newId} WHERE \"CreatorId\" = {id}");

                // Some group schemas also keep the creator/member relation under a separate table.
                // Only update tables that are known to exist in this database schema.
                await _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM \"Users\" WHERE \"Id\" = {id}");
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            _context.ChangeTracker.Clear();
            var updatedUser = await _context.Users.AsNoTracking().FirstAsync(u => u.Id == newId);
            return new RegisterResult
            {
                Success = true,
                Message = "User updated successfully. Please sign in again because your User ID changed.",
                User = ToUserResponse(updatedUser)
            };
        }

        user.DisplayName = dto.DisplayName;
        user.Email = dto.Email;
        user.Bio = dto.Bio;
        await _context.SaveChangesAsync();
        return new RegisterResult { Success = true, Message = "User updated successfully.", User = ToUserResponse(user) };
    }

    public async Task<(bool Success, string Message, UserResponseDto? User)> SetAvatarAsync(string id, string avatarUrl)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return (false, "User not found.", null);
        user.AvatarUrl = avatarUrl; await _context.SaveChangesAsync();
        return (true, "Profile picture updated successfully.", ToUserResponse(user));
    }

    public async Task<(bool Success, string Message, string? OldAvatarUrl)> ClearAvatarAsync(string id)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return (false, "User not found.", null);
        var old = user.AvatarUrl; user.AvatarUrl = null; await _context.SaveChangesAsync();
        return (true, "Profile picture removed successfully.", old);
    }

    public async Task MarkLastSeenAsync(string id) => await _context.Users.Where(u => u.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(u => u.LastSeenAt, DateTime.UtcNow));

    public async Task<bool> DeleteUserAsync(string id)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return false;
        _context.Users.Remove(user); await _context.SaveChangesAsync(); return true;
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(string id, ChangePasswordDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return (false, "User not found.");
        var passwordResult = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.CurrentPassword);
        if (passwordResult == PasswordVerificationResult.Failed) return (false, "Current password is incorrect.");
        user.PasswordHash = _passwordHasher.HashPassword(user, dto.NewPassword); await _context.SaveChangesAsync();
        return (true, "Password changed successfully.");
    }

    private UserResponseDto ToUserResponse(User user) => new()
    {
        Id = user.Id, DisplayName = user.DisplayName, Email = user.Email, Bio = user.Bio, AvatarUrl = user.AvatarUrl,
        IsOnline = _presenceService.IsOnline(user.Id), LastSeenAt = user.LastSeenAt, CreatedAt = user.CreatedAt
    };
}