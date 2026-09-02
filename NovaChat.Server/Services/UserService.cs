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
    private static readonly Regex PhoneRegex = new(@"^\+?[0-9]{7,15}$", RegexOptions.Compiled);

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
        dto.Id = dto.Id.Trim();
        dto.Email = dto.Email.Trim();
        dto.DisplayName = dto.DisplayName.Trim();

        if (!TryNormalizePhoneNumber(dto.PhoneNumber, out var phoneNumber))
        {
            return new RegisterResult
            {
                Success = false,
                Message = "Enter a valid phone number using 7 to 15 digits."
            };
        }

        dto.PhoneNumber = phoneNumber;

        if (string.IsNullOrWhiteSpace(dto.Id) ||
            string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.DisplayName) ||
            string.IsNullOrWhiteSpace(dto.Password))
        {
            return new RegisterResult
            {
                Success = false,
                Message = "All registration fields are required."
            };
        }

        await RegisterLock.WaitAsync();

        try
        {
            if (await _context.Users.AnyAsync(u => u.Id == dto.Id))
            {
                return new RegisterResult
                {
                    Success = false,
                    Message = "This User ID is already taken."
                };
            }

            if (await _context.Users.AnyAsync(u => u.Email == dto.Email))
            {
                return new RegisterResult
                {
                    Success = false,
                    Message = "This Email is already registered."
                };
            }

            if (await _context.Users.AnyAsync(u => u.PhoneNumber == dto.PhoneNumber))
            {
                return new RegisterResult
                {
                    Success = false,
                    Message = "This phone number is already registered."
                };
            }

            var user = new User
            {
                Id = dto.Id,
                DisplayName = dto.DisplayName,
                Email = dto.Email,
                PhoneNumber = dto.PhoneNumber,
                CreatedAt = DateTime.UtcNow
            };

            user.PasswordHash = _passwordHasher.HashPassword(user, dto.Password);

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            return new RegisterResult
            {
                Success = true,
                Message = "User registered successfully.",
                User = ToUserResponse(user)
            };
        }
        finally
        {
            RegisterLock.Release();
        }
    }

    public async Task<User?> LoginAsync(LoginDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == dto.Id);
        if (user == null) return null;

        var passwordResult = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            dto.Password);

        return passwordResult == PasswordVerificationResult.Failed ? null : user;
    }

    public async Task<UserResponseDto?> GetUserByIdAsync(string id, bool includePhoneNumber = false)
    {
        var user = await _context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id);

        return user == null ? null : ToUserResponse(user, includePhoneNumber);
    }

    public async Task<List<UserResponseDto>> SearchUsersAsync(string query, string currentUserId)
    {
        query = query.Trim();
        if (query.Length < 1) return [];

        var pattern = $"%{query}%";

        var users = await _context.Users
            .AsNoTracking()
            .Where(u => u.Id != currentUserId &&
                (EF.Functions.Like(u.Id, pattern) ||
                 EF.Functions.Like(u.DisplayName, pattern) ||
                 EF.Functions.Like(u.Email, pattern)))
            .OrderBy(u => u.DisplayName)
            .Take(30)
            .ToListAsync();

        return users.Select(ToUserResponse).ToList();
    }

    public async Task<RegisterResult> UpdateUserAsync(string id, UpdateUserDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return new RegisterResult
            {
                Success = false,
                Message = "User not found."
            };
        }

        var newId = string.IsNullOrWhiteSpace(dto.NewUserId)
            ? id
            : dto.NewUserId.Trim();

        dto.DisplayName = dto.DisplayName.Trim();
        dto.Email = dto.Email.Trim();
        dto.Bio = (dto.Bio ?? string.Empty).Trim();

        if (!TryNormalizePhoneNumber(dto.PhoneNumber, out var phoneNumber))
        {
            return new RegisterResult
            {
                Success = false,
                Message = "Enter a valid phone number using 7 to 15 digits."
            };
        }

        if (await _context.Users.AsNoTracking().AnyAsync(u => u.Id == newId && u.Id != id))
        {
            return new RegisterResult
            {
                Success = false,
                Message = "This User ID is already taken."
            };
        }

        if (await _context.Users.AsNoTracking().AnyAsync(u => u.Email == dto.Email && u.Id != id))
        {
            return new RegisterResult
            {
                Success = false,
                Message = "This Email is already registered."
            };
        }

        if (await _context.Users.AsNoTracking().AnyAsync(u => u.PhoneNumber == phoneNumber && u.Id != id))
        {
            return new RegisterResult
            {
                Success = false,
                Message = "This phone number is already registered."
            };
        }

        if (!string.Equals(id, newId, StringComparison.Ordinal))
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    INSERT INTO `Users`
                    (
                        `Id`,
                        `DisplayName`,
                        `Email`,
                        `PhoneNumber`,
                        `PasswordHash`,
                        `Bio`,
                        `AvatarUrl`,
                        `LastSeenAt`,
                        `CreatedAt`
                    )
                    SELECT
                        {newId},
                        `DisplayName`,
                        `Email`,
                        {phoneNumber},
                        `PasswordHash`,
                        `Bio`,
                        `AvatarUrl`,
                        `LastSeenAt`,
                        `CreatedAt`
                    FROM `Users`
                    WHERE `Id` = {id}
                    """);

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE `Users`
                    SET
                        `DisplayName` = {dto.DisplayName},
                        `Email` = {dto.Email},
                        `PhoneNumber` = {phoneNumber},
                        `Bio` = {dto.Bio}
                    WHERE `Id` = {newId}
                    """);

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE `Messages`
                    SET `SenderId` = {newId}
                    WHERE `SenderId` = {id}
                    """);

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE `Chats`
                    SET `User1Id` = {newId}
                    WHERE `User1Id` = {id}
                    """);

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE `Chats`
                    SET `User2Id` = {newId}
                    WHERE `User2Id` = {id}
                    """);

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE `Contacts`
                    SET `OwnerUserId` = {newId}
                    WHERE `OwnerUserId` = {id}
                    """);

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    UPDATE `Contacts`
                    SET `ContactUserId` = {newId}
                    WHERE `ContactUserId` = {id}
                    """);

                if (await TableExistsAsync("GroupMembers"))
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        UPDATE `GroupMembers`
                        SET `UserId` = {newId}
                        WHERE `UserId` = {id}
                        """);
                }

                if (await TableExistsAsync("Groups"))
                {
                    await _context.Database.ExecuteSqlInterpolatedAsync(
                        $"""
                        UPDATE `Groups`
                        SET `CreatorId` = {newId}
                        WHERE `CreatorId` = {id}
                        """);
                }

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM `Users`
                    WHERE `Id` = {id}
                    """);

                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }

            _context.ChangeTracker.Clear();

            var updatedUser = await _context.Users
                .AsNoTracking()
                .FirstAsync(u => u.Id == newId);

            return new RegisterResult
            {
                Success = true,
                Message = "User updated successfully. Please sign in again because your User ID changed.",
                User = ToUserResponse(updatedUser, includePhoneNumber: true)
            };
        }

        user.DisplayName = dto.DisplayName;
        user.Email = dto.Email;
        user.PhoneNumber = phoneNumber;
        user.Bio = dto.Bio;

        await _context.SaveChangesAsync();

        return new RegisterResult
        {
            Success = true,
            Message = "User updated successfully.",
            User = ToUserResponse(user, includePhoneNumber: true)
        };
    }

    public async Task<(bool Success, string Message, UserResponseDto? User)> SetAvatarAsync(string id, string avatarUrl)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return (false, "User not found.", null);

        user.AvatarUrl = avatarUrl;
        await _context.SaveChangesAsync();

        return (true, "Profile picture updated successfully.", ToUserResponse(user, includePhoneNumber: true));
    }

    public async Task<(bool Success, string Message, string? OldAvatarUrl)> ClearAvatarAsync(string id)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return (false, "User not found.", null);

        var old = user.AvatarUrl;
        user.AvatarUrl = null;
        await _context.SaveChangesAsync();

        return (true, "Profile picture removed successfully.", old);
    }

    public async Task MarkLastSeenAsync(string id) =>
        await _context.Users
            .Where(u => u.Id == id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(u => u.LastSeenAt, DateTime.UtcNow));

    public async Task<bool> DeleteUserAsync(string id)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return false;

        await using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM `Messages`
                WHERE `SenderId` = {id}
                """);

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM `Chats`
                WHERE `User1Id` = {id}
                   OR `User2Id` = {id}
                """);

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM `Contacts`
                WHERE `OwnerUserId` = {id}
                   OR `ContactUserId` = {id}
                """);

            var groupsExist = await TableExistsAsync("Groups");
            var groupMembersExist = await TableExistsAsync("GroupMembers");

            if (groupsExist && groupMembersExist)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM `GroupMembers`
                    WHERE `GroupId` IN
                    (
                        SELECT `Id`
                        FROM `Groups`
                        WHERE `CreatorId` = {id}
                    )
                    OR `UserId` = {id}
                    """);

                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM `Groups`
                    WHERE `CreatorId` = {id}
                    """);
            }
            else if (groupMembersExist)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM `GroupMembers`
                    WHERE `UserId` = {id}
                    """);
            }
            else if (groupsExist)
            {
                await _context.Database.ExecuteSqlInterpolatedAsync(
                    $"""
                    DELETE FROM `Groups`
                    WHERE `CreatorId` = {id}
                    """);
            }

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                DELETE FROM `Users`
                WHERE `Id` = {id}
                """);

            await transaction.CommitAsync();
            _context.ChangeTracker.Clear();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(string id, ChangePasswordDto dto)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
        if (user == null) return (false, "User not found.");

        var passwordResult = _passwordHasher.VerifyHashedPassword(
            user,
            user.PasswordHash,
            dto.CurrentPassword);

        if (passwordResult == PasswordVerificationResult.Failed)
            return (false, "Current password is incorrect.");

        user.PasswordHash = _passwordHasher.HashPassword(user, dto.NewPassword);
        await _context.SaveChangesAsync();

        return (true, "Password changed successfully.");
    }

    private async Task<bool> TableExistsAsync(string tableName)
    {
        return await _context.Database
            .SqlQueryRaw<bool>(
                """
                SELECT EXISTS
                (
                    SELECT 1
                    FROM information_schema.tables
                    WHERE table_schema = DATABASE()
                      AND table_name = {0}
                ) AS `Value`
                """,
                tableName)
            .FirstAsync();
    }

    private UserResponseDto ToUserResponse(User user, bool includePhoneNumber = false)
    {
        return new UserResponseDto
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            PhoneNumber = includePhoneNumber ? user.PhoneNumber : null,
            Bio = user.Bio,
            AvatarUrl = user.AvatarUrl,
            IsOnline = _presenceService.IsOnline(user.Id),
            LastSeenAt = user.LastSeenAt,
            CreatedAt = user.CreatedAt
        };
    }

    private static bool TryNormalizePhoneNumber(string? input, out string normalized)
    {
        normalized = string.Empty;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        normalized = input.Trim()
            .Replace(" ", string.Empty)
            .Replace("-", string.Empty)
            .Replace("(", string.Empty)
            .Replace(")", string.Empty);

        return PhoneRegex.IsMatch(normalized);
    }
}
