using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaChat.Server.DTOs;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UserController : ControllerBase
{
    private readonly UserService _userService;
    private readonly JwtService _jwtService;

    public UserController(
        UserService userService,
        JwtService jwtService)
    {
        _userService = userService;
        _jwtService = jwtService;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterDto dto)
    {
        var result = await _userService.RegisterAsync(dto);

        if (!result.Success)
        {
            return Conflict(new
            {
                message = result.Message
            });
        }

        return Ok(new
        {
            message = result.Message,
            user = result.User
        });
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var user = await _userService.LoginAsync(dto);

        if (user == null)
        {
            return Unauthorized(new
            {
                message = "Invalid User ID or Password."
            });
        }

        var token = _jwtService.GenerateToken(user);

        return Ok(new
        {
            message = "Login successful.",
            token,
            user = new
            {
                user.Id,
                user.DisplayName,
                user.Email,
                user.CreatedAt
            }
        });
    }

    [Authorize]
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (currentUserId != id)
        {
            return Forbid();
        }

        var user = await _userService.GetUserByIdAsync(id);

        if (user == null)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        return Ok(user);
    }

    [Authorize]
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(
        string id,
        UpdateUserDto dto)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (currentUserId != id)
        {
            return Forbid();
        }

        var result = await _userService.UpdateUserAsync(id, dto);

        if (!result.Success)
        {
            if (result.Message == "User not found.")
            {
                return NotFound(new
                {
                    message = result.Message
                });
            }

            return Conflict(new
            {
                message = result.Message
            });
        }

        return Ok(new
        {
            message = result.Message,
            user = result.User
        });
    }

    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (currentUserId != id)
        {
            return Forbid();
        }

        var deleted = await _userService.DeleteUserAsync(id);

        if (!deleted)
        {
            return NotFound(new
            {
                message = "User not found."
            });
        }

        return Ok(new
        {
            message = "User deleted successfully."
        });
    }

    [Authorize]
    [HttpPut("{id}/password")]
    public async Task<IActionResult> ChangePassword(
        string id,
        ChangePasswordDto dto)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (currentUserId != id)
        {
            return Forbid();
        }

        var result = await _userService.ChangePasswordAsync(id, dto);

        if (!result.Success)
        {
            if (result.Message == "User not found.")
            {
                return NotFound(new
                {
                    message = result.Message
                });
            }

            return BadRequest(new
            {
                message = result.Message
            });
        }

        return Ok(new
        {
            message = result.Message
        });
    }
}