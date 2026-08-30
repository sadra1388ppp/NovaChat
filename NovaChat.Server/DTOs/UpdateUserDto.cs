using System.ComponentModel.DataAnnotations;

namespace NovaChat.Server.DTOs;

public class UpdateUserDto
{
    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [StringLength(160)]
    public string? Bio { get; set; }

    [StringLength(32, MinimumLength = 3)]
    [RegularExpression("^[a-zA-Z0-9_.-]+$", ErrorMessage = "User ID may contain only letters, numbers, dot, underscore and hyphen.")]
    public string? NewUserId { get; set; }
}