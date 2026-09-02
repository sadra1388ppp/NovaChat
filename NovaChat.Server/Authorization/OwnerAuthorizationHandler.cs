using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace NovaChat.Server.Authorization;

public class OwnerAuthorizationHandler : AuthorizationHandler<OwnerRequirement>
{
    private readonly IConfiguration _configuration;

    public OwnerAuthorizationHandler(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        OwnerRequirement requirement)
    {
        var currentUserId =
            context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        var ownerId =
            _configuration["Owner:UserId"];

        if (!string.IsNullOrWhiteSpace(currentUserId) &&
            !string.IsNullOrWhiteSpace(ownerId) &&
            string.Equals(
                currentUserId,
                ownerId,
                StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}