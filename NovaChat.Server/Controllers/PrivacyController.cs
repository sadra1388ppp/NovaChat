using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaChat.Server.Entities;
using NovaChat.Server.Services;
using System.Security.Claims;

namespace NovaChat.Server.Controllers;

[ApiController]
[Authorize]
[Route("api/Privacy")]
public sealed class PrivacyController(UserService users) : ControllerBase
{
    private readonly UserService _users = users;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var profile = await _users.GetUserByIdAsync(userId.ToString(), true);
        return profile == null ? NotFound(new { message = "User not found." }) : Ok(new { messagePrivacy = profile.MessagePrivacy, allowGroupAdds = profile.AllowGroupAdds });
    }

    [HttpPut]
    public async Task<IActionResult> Update(UpdatePrivacyRequest request)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var dto = new NovaChat.Server.DTOs.UpdateUserDto { DisplayName = "x", Email = "x@example.invalid", PhoneNumber = "00000000000", MessagePrivacy = request.MessagePrivacy, AllowGroupAdds = request.AllowGroupAdds };
        // Update privacy directly to avoid requiring profile identity fields in this dedicated endpoint.
        var result = await ApplyAsync(userId, request);
        return result.Success ? Ok(new { message = "Privacy settings updated.", messagePrivacy = result.MessagePrivacy, allowGroupAdds = result.AllowGroupAdds }) : BadRequest(new { message = result.Error });
    }

    private async Task<(bool Success, string? Error, string MessagePrivacy, bool AllowGroupAdds)> ApplyAsync(long userId, UpdatePrivacyRequest request)
    {
        var dbField = HttpContext.RequestServices.GetRequiredService<Microsoft.EntityFrameworkCore.DbContext>();
        var db = dbField as Microsoft.EntityFrameworkCore.DbContext;
        var concrete = HttpContext.RequestServices.GetRequiredService<NovaChat.Server.Data.AppDbContext>();
        var user = await concrete.Users.FirstOrDefaultAsync(u => u.Id == userId);
        if (user == null) return (false, "User not found.", "Everybody", true);
        var privacy = string.Equals(request.MessagePrivacy?.Trim(), "Requests", StringComparison.OrdinalIgnoreCase) ? "Requests" : "Everybody";
        user.MessagePrivacy = privacy;
        user.AllowGroupAdds = request.AllowGroupAdds;
        await concrete.SaveChangesAsync();
        return (true, null, privacy, user.AllowGroupAdds);
    }

    private bool TryGetUserId(out long userId) => long.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out userId) && userId > 0;

    public sealed class UpdatePrivacyRequest
    {
        public string MessagePrivacy { get; set; } = "Everybody";
        public bool AllowGroupAdds { get; set; } = true;
    }
}
