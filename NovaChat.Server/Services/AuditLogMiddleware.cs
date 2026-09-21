using Microsoft.AspNetCore.Mvc.Controllers;
using System.Security.Claims;

namespace NovaChat.Server.Services;

public sealed class AuditLogMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, AuditLogService auditLogService)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        Exception? failure = null;

        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            try
            {
                var descriptor = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
                var controller = descriptor?.ControllerName;
                var action = descriptor?.ActionName;

                var eventType = string.IsNullOrWhiteSpace(controller)
                    ? $"Http.{context.Request.Method}.{NormalizePath(path)}"
                    : $"Http.{controller}.{action}";

                var category = ResolveCategory(path, controller, action, statusCode);
                var userId = TryGetUserId(context.User);
                var username = context.User.FindFirst("username")?.Value;
                var targetId = ResolveTargetId(context);
                var statusCode = context.Response.StatusCode;

                await auditLogService.LogAsync(
                    category: category,
                    eventType: eventType,
                    userId: userId,
                    username: username,
                    targetType: string.IsNullOrWhiteSpace(targetId) ? "Endpoint" : ResolveTargetType(context, path),
                    targetId: targetId,
                    requestId: context.TraceIdentifier,
                    ipAddress: context.Connection.RemoteIpAddress?.ToString(),
                    userAgent: context.Request.Headers.UserAgent.ToString(),
                    succeeded: failure == null && statusCode < 400,
                    details: failure == null
                        ? $"HTTP {statusCode}."
                        : $"HTTP request failed with {failure.GetType().Name}.");
            }
            catch
            {
                // Auditing must never break the original request pipeline.
            }
        }
    }

    private static string ResolveCategory(string path, string? controller, string? action, int statusCode)
    {
        if (statusCode == StatusCodes.Status403Forbidden)
            return "Authorization";

        if (statusCode == StatusCodes.Status401Unauthorized)
            return "Authentication";

        if (path.StartsWith("/api/User/login", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/User/register", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/User/logout", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(controller, "User", StringComparison.OrdinalIgnoreCase) &&
            (string.Equals(action, "ChangePassword", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(action, "DeleteUser", StringComparison.OrdinalIgnoreCase)))
            return "Authentication";

        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return "Accounting";

        return "Operations";
    }

    private static long? TryGetUserId(ClaimsPrincipal user)
        => long.TryParse(user.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0
            ? id
            : null;

    private static string? ResolveTargetId(HttpContext context)
    {
        foreach (var key in new[] { "messageId", "chatId", "id" })
        {
            if (context.Request.RouteValues.TryGetValue(key, out var value) &&
                value != null &&
                !string.IsNullOrWhiteSpace(value.ToString()))
                return value.ToString();
        }

        return null;
    }

    private static string ResolveTargetType(HttpContext context, string path)
    {
        if (context.Request.RouteValues.ContainsKey("messageId"))
            return "Message";
        if (context.Request.RouteValues.ContainsKey("chatId"))
            return "Chat";
        if (context.Request.RouteValues.ContainsKey("id"))
            return path.Contains("/User/", StringComparison.OrdinalIgnoreCase) ? "User" : "Resource";
        return "Endpoint";
    }

    private static string NormalizePath(string path)
    {
        var normalized = path.Trim('/').Replace('/', '.');
        return normalized.Length <= 96 ? normalized : normalized[..96];
    }
}
