using System.ComponentModel.DataAnnotations;

namespace NovaChat.Server.DTOs;

public class RegisterDto
{
    [Required]
    [StringLength(30, MinimumLength = 3)]
    public string Id { get; set; } = string.Empty;

    [Required]
    [StringLength(50, MinimumLength = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    [StringLength(32, MinimumLength = 7)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [StringLength(100, MinimumLength = 6)]
    public string Password { get; set; } = string.Empty;
}