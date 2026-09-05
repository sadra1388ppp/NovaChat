using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;
using System.Text.RegularExpressions;

namespace NovaChat.Server.Services;

public class UserService
{
    private static readonly SemaphoreSlim RegisterLock = new(1, 1);
    private static readonly Regex UsernameRegex = new("^[a-zA-Z0-9_.-]{3,32}$", RegexOptions.Compiled);
    private static readonly Regex PhoneRegex = new("^\\+?[0-9]{7,15}$", RegexOptions.Compiled);
    private readonly AppDbContext _context;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly PresenceService _presenceService;

    public UserService(AppDbContext context, IPasswordHasher<User> passwordHasher, PresenceService presenceService)
    { _context = context; _passwordHasher = passwordHasher; _presenceService = presenceService; }

    public async Task<RegisterResult> RegisterAsync(RegisterDto dto)
    {
        var username = dto.Username.Trim().ToLowerInvariant(); var email = dto.Email.Trim(); var displayName = dto.DisplayName.Trim();
        if (!UsernameRegex.IsMatch(username)) return Fail("Username must be 3 to 32 characters and may contain only letters, numbers, dot, underscore and hyphen.");
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(dto.Password)) return Fail("All registration fields are required.");
        if (!TryNormalizePhoneNumber(dto.PhoneNumber, out var phoneNumber)) return Fail("Enter a valid phone number using 7 to 15 digits.");
        await RegisterLock.WaitAsync();
        try
        {
            if (await _context.Users.AnyAsync(u => u.Username == username)) return Fail("This username is already taken.");
            if (await _context.Users.AnyAsync(u => u.Email == email)) return Fail("This Email is already registered.");
            if (await _context.Users.AnyAsync(u => u.PhoneNumber == phoneNumber)) return Fail("This phone number is already registered.");

            var user = new User
            {
                // Internal database identifier only. New users receive the next available sequential ID.
                Id = await GenerateNextUserIdAsync(),
                Username = username,
                DisplayName = displayName,
                Email = email,
                PhoneNumber = phoneNumber,
                CreatedAt = DateTime.UtcNow
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);
            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return new RegisterResult { Success = true, Message = "User registered successfully.", User = ToUserResponse(user) };
        }
        finally { RegisterLock.Release(); }
    }

    public async Task<User?> LoginAsync(LoginDto dto)
    {
        var login = dto.Login.Trim(); if (string.IsNullOrWhiteSpace(login)) return null;
        User? user = null;
        if (TryNormalizePhoneNumber(login, out var phoneNumber)) user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);
        if (user == null) user = await _context.Users.FirstOrDefaultAsync(u => u.Username == login.ToLowerInvariant());
        if (user == null) return null;
        var result = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.Password);
        return result == PasswordVerificationResult.Failed ? null : user;
    }

    public async Task<UserResponseDto?> GetUserByIdAsync(string id, bool includePhoneNumber = false)
    {
        if (!long.TryParse(id, out var userId)) return null;
        var user = await _context.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == userId);
        return user == null ? null : ToUserResponse(user, includePhoneNumber);
    }

    public async Task<List<UserResponseDto>> SearchUsersAsync(string query, string currentUserId)
    {
        query = query.Trim(); if (query.Length < 1) return [];
        long.TryParse(currentUserId, out var excludedId); var pattern = $"%{query}%";
        var users = await _context.Users.AsNoTracking()
            .Where(u => u.Id != excludedId && (EF.Functions.Like(u.Username, pattern) || EF.Functions.Like(u.DisplayName, pattern) || EF.Functions.Like(u.Email, pattern)))
            .OrderBy(u => u.DisplayName).Take(30).ToListAsync();
        return users.Select(u => ToUserResponse(u)).ToList();
    }

    public async Task<RegisterResult> UpdateUserAsync(string id, UpdateUserDto dto)
    {
        if (!long.TryParse(id, out var userId)) return Fail("User not found.");
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId); if (user == null) return Fail("User not found.");
        var newUsername = string.IsNullOrWhiteSpace(dto.NewUsername) ? user.Username : dto.NewUsername.Trim().ToLowerInvariant();
        if (!UsernameRegex.IsMatch(newUsername)) return Fail("Username must be 3 to 32 characters and may contain only letters, numbers, dot, underscore and hyphen.");
        dto.DisplayName = dto.DisplayName.Trim(); dto.Email = dto.Email.Trim(); dto.Bio = (dto.Bio ?? string.Empty).Trim();
        if (!TryNormalizePhoneNumber(dto.PhoneNumber, out var phoneNumber)) return Fail("Enter a valid phone number using 7 to 15 digits.");
        if (await _context.Users.AsNoTracking().AnyAsync(u => u.Username == newUsername && u.Id != userId)) return Fail("This username is already taken.");
        if (await _context.Users.AsNoTracking().AnyAsync(u => u.Email == dto.Email && u.Id != userId)) return Fail("This Email is already registered.");
        if (await _context.Users.AsNoTracking().AnyAsync(u => u.PhoneNumber == phoneNumber && u.Id != userId)) return Fail("This phone number is already registered.");
        user.Username = newUsername; user.DisplayName = dto.DisplayName; user.Email = dto.Email; user.PhoneNumber = phoneNumber; user.Bio = dto.Bio;
        await _context.SaveChangesAsync(); return new RegisterResult { Success = true, Message = "User updated successfully.", User = ToUserResponse(user, true) };
    }

    public async Task<(bool Success, string Message, UserResponseDto? User)> SetAvatarAsync(string id, string avatarUrl)
    {
        if (!long.TryParse(id, out var userId)) return (false, "User not found.", null);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId); if (user == null) return (false, "User not found.", null);
        user.AvatarUrl = avatarUrl; await _context.SaveChangesAsync(); return (true, "Profile picture updated successfully.", ToUserResponse(user, true));
    }

    public async Task<(bool Success, string Message, string? OldAvatarUrl)> ClearAvatarAsync(string id)
    {
        if (!long.TryParse(id, out var userId)) return (false, "User not found.", null);
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId); if (user == null) return (false, "User not found.", null);
        var old = user.AvatarUrl; user.AvatarUrl = null; await _context.SaveChangesAsync(); return (true, "Profile picture removed successfully.", old);
    }

    public async Task MarkLastSeenAsync(string id)
    {
        if (!long.TryParse(id, out var userId)) return;
        await _context.Users.Where(u => u.Id == userId).ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAt, DateTime.UtcNow));
    }

    public async Task<bool> DeleteUserAsync(string id)
    {
        if (!long.TryParse(id, out var userId)) return false;
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId); if (user == null) return false;
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            await _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Messages` WHERE `SenderId` = {userId}");
            await _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Chats` WHERE `User1Id` = {userId} OR `User2Id` = {userId}");
            await _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Contacts` WHERE `OwnerUserId` = {userId} OR `ContactUserId` = {userId}");
            var groupsExist = await TableExistsAsync("Groups"); var membersExist = await TableExistsAsync("GroupMembers");
            if (groupsExist && membersExist)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `GroupMembers` WHERE `GroupId` IN (SELECT `Id` FROM `Groups` WHERE `CreatorId` = {userId}) OR `UserId` = {userId}");
                await _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Groups` WHERE `CreatorId` = {userId}");
            }
            else if (membersExist) await _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `GroupMembers` WHERE `UserId` = {userId}");
            else if (groupsExist) await _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Groups` WHERE `CreatorId` = {userId}");
            await _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM `Users` WHERE `Id` = {userId}");
            await transaction.CommitAsync(); _context.ChangeTracker.Clear(); return true;
        }
        catch { await transaction.RollbackAsync(); throw; }
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(string id, ChangePasswordDto dto)
    {
        if (!long.TryParse(id, out var userId)) return (false, "User not found.");
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId); if (user == null) return (false, "User not found.");
        if (_passwordHasher.VerifyHashedPassword(user, user.PasswordHash, dto.CurrentPassword) == PasswordVerificationResult.Failed) return (false, "Current password is incorrect.");
        user.PasswordHash = _passwordHasher.HashPassword(user, dto.NewPassword); await _context.SaveChangesAsync(); return (true, "Password changed successfully.");
    }

    private async Task<long> GenerateNextUserIdAsync()
    {
        // IDs stay numeric and sequential: 1, 2, 3, ...
        // We deliberately reuse the first free ID so deleted users do not cause gaps.
        var existingIds = await _context.Users
            .AsNoTracking()
            .Select(u => u.Id)
            .ToListAsync();

        var usedIds = existingIds.ToHashSet();
        long nextId = 1;

        while (usedIds.Contains(nextId))
            nextId++;

        return nextId;
    }

    private async Task<bool> TableExistsAsync(string tableName) => await _context.Database.SqlQueryRaw<bool>("SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = DATABASE() AND table_name = {0}) AS `Value`", tableName).FirstAsync();
    private static RegisterResult Fail(string message) => new() { Success = false, Message = message };
    private UserResponseDto ToUserResponse(User user, bool includePhoneNumber = false) => new()
    {
        Id = user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Username = user.Username, DisplayName = user.DisplayName, Email = user.Email,
        PhoneNumber = includePhoneNumber ? user.PhoneNumber : null, Bio = user.Bio, AvatarUrl = user.AvatarUrl,
        IsOnline = _presenceService.IsOnline(user.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)), LastSeenAt = user.LastSeenAt, CreatedAt = user.CreatedAt
    };
    private static bool TryNormalizePhoneNumber(string? input, out string normalized)
    {
        normalized = string.Empty; if (string.IsNullOrWhiteSpace(input)) return false;
        normalized = input.Trim().Replace(" ", string.Empty).Replace("-", string.Empty).Replace("(", string.Empty).Replace(")", string.Empty); return PhoneRegex.IsMatch(normalized);
    }
}
