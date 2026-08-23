using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.DTOs;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public class UserService
{
    private readonly AppDbContext _context;

    public UserService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<RegisterResult> RegisterAsync(RegisterDto dto)
    {
        var existingUserId = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == dto.Id);

        if (existingUserId != null)
        {
            return new RegisterResult
            {
                Success = false,
                Message = "This User ID is already taken."
            };
        }

        var existingEmail = await _context.Users
            .FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (existingEmail != null)
        {
            return new RegisterResult
            {
                Success = false,
                Message = "This Email is already registered."
            };
        }

        var user = new User
        {
            Id = dto.Id,
            DisplayName = dto.DisplayName,
            Email = dto.Email,
            Password = dto.Password,
            CreatedAt = DateTime.UtcNow
        };

        _context.Users.Add(user);

        await _context.SaveChangesAsync();

        return new RegisterResult
        {
            Success = true,
            Message = "User registered successfully.",
            User = ToUserResponse(user)
        };
    }

    public async Task<User?> LoginAsync(LoginDto dto)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == dto.Id);

        if (user == null)
        {
            return null;
        }

        if (user.Password != dto.Password)
        {
            return null;
        }

        return user;
    }

    public async Task<UserResponseDto?> GetUserByIdAsync(string id)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return null;
        }

        return ToUserResponse(user);
    }

    public async Task<RegisterResult> UpdateUserAsync(
        string id,
        UpdateUserDto dto)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return new RegisterResult
            {
                Success = false,
                Message = "User not found."
            };
        }

        var existingEmail = await _context.Users
            .FirstOrDefaultAsync(u =>
                u.Email == dto.Email &&
                u.Id != id);

        if (existingEmail != null)
        {
            return new RegisterResult
            {
                Success = false,
                Message = "This Email is already registered."
            };
        }

        user.DisplayName = dto.DisplayName;
        user.Email = dto.Email;

        await _context.SaveChangesAsync();

        return new RegisterResult
        {
            Success = true,
            Message = "User updated successfully.",
            User = ToUserResponse(user)
        };
    }

    public async Task<bool> DeleteUserAsync(string id)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return false;
        }

        _context.Users.Remove(user);

        await _context.SaveChangesAsync();

        return true;
    }

    public async Task<(bool Success, string Message)> ChangePasswordAsync(
        string id,
        ChangePasswordDto dto)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return (false, "User not found.");
        }

        if (user.Password != dto.CurrentPassword)
        {
            return (false, "Current password is incorrect.");
        }

        user.Password = dto.NewPassword;

        await _context.SaveChangesAsync();

        return (true, "Password changed successfully.");
    }

    private static UserResponseDto ToUserResponse(User user)
    {
        return new UserResponseDto
        {
            Id = user.Id,
            DisplayName = user.DisplayName,
            Email = user.Email,
            CreatedAt = user.CreatedAt
        };
    }
}