using System.Security.Claims;

namespace NovaChat.Server.Services;

public sealed class JwtRevocationMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    public async Task InvokeAsync(HttpContext context, JwtRevocationService revocationService)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var jti = context.User.FindFirstValue("jti");

            if (!string.IsNullOrWhiteSpace(jti) && revocationService.IsRevoked(jti))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                await context.Response.WriteAsJsonAsync(new
                {
                    message = "This session has been logged out."
                }, context.RequestAborted);
                return;
            }
        }

        await _next(context);
    }
}
