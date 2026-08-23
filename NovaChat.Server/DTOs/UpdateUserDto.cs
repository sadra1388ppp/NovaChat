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
}