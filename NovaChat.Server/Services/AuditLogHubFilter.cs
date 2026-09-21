using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;

namespace NovaChat.Server.Services;

public sealed class AuditLogHubFilter : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(
        HubInvocationContext invocationContext,
        Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var audit = invocationContext.ServiceProvider.GetRequiredService<AuditLogService>();
        var user = invocationContext.Context.User;

        Exception? failure = null;
        try
        {
            var result = await next(invocationContext);
            return result;
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
                var method = invocationContext.HubMethodName;
                var category = ResolveCategory(method);
                var userId = TryGetUserId(user);
                var username = user?.FindFirst("username")?.Value;
                var target = ResolveTarget(invocationContext);

                await audit.LogAsync(
                    category: category,
                    eventType: $"SignalR.ChatHub.{method}",
                    userId: userId,
                    username: username,
                    targetType: target.Type,
                    targetId: target.Id,
                    chatId: target.ChatId,
                    messageId: target.MessageId,
                    requestId: invocationContext.Context.ConnectionId,
                    ipAddress: TryGetIpAddress(invocationContext.Context),
                    userAgent: null,
                    succeeded: failure == null,
                    details: failure == null
                        ? "Hub method completed successfully."
                        : $"Hub method failed with {failure.GetType().Name}.");
            }
            catch
            {
                // Auditing must never mask the original SignalR operation/result.
            }
        }
    }

    public async Task OnConnectedAsync(
        HubLifetimeContext context,
        Func<HubLifetimeContext, Task> next)
    {
        var audit = context.ServiceProvider.GetRequiredService<AuditLogService>();
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
                await audit.LogAsync(
                    category: "Authentication",
                    eventType: "SignalR.ChatHub.Connected",
                    userId: TryGetUserId(context.Context.User),
                    username: context.Context.User?.FindFirst("username")?.Value,
                    targetType: "Connection",
                    targetId: context.Context.ConnectionId,
                    requestId: context.Context.ConnectionId,
                    ipAddress: TryGetIpAddress(context.Context),
                    succeeded: failure == null,
                    details: failure == null ? "Hub connection established." : $"Connection failed with {failure.GetType().Name}.");
            }
            catch
            {
            }
        }
    }

    public async Task OnDisconnectedAsync(
        HubLifetimeContext context,
        Exception? exception,
        Func<HubLifetimeContext, Exception?, Task> next)
    {
        var audit = context.ServiceProvider.GetRequiredService<AuditLogService>();

        try
        {
            await next(context, exception);
        }
        finally
        {
            try
            {
                await audit.LogAsync(
                    category: "Authentication",
                    eventType: "SignalR.ChatHub.Disconnected",
                    userId: TryGetUserId(context.Context.User),
                    username: context.Context.User?.FindFirst("username")?.Value,
                    targetType: "Connection",
                    targetId: context.Context.ConnectionId,
                    requestId: context.Context.ConnectionId,
                    ipAddress: TryGetIpAddress(context.Context),
                    succeeded: exception == null,
                    details: exception == null ? "Hub connection closed normally." : $"Hub connection closed with {exception.GetType().Name}.");
            }
            catch
            {
            }
        }
    }

    private static string ResolveCategory(string method)
    {
        if (method.Contains("Key", StringComparison.OrdinalIgnoreCase) ||
            method.Contains("Security", StringComparison.OrdinalIgnoreCase))
            return "Security";

        if (method.Contains("Message", StringComparison.OrdinalIgnoreCase) ||
            method.Contains("Chat", StringComparison.OrdinalIgnoreCase) ||
            method.Contains("Group", StringComparison.OrdinalIgnoreCase) ||
            method.Contains("Contact", StringComparison.OrdinalIgnoreCase))
            return "Accounting";

        return "Operations";
    }

    private static long? TryGetUserId(ClaimsPrincipal? user)
        => long.TryParse(user?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0
            ? id
            : null;

    private static string? TryGetIpAddress(HubCallerContext context)
        => context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString();

    private static (string? Type, string? Id, int? ChatId, int? MessageId) ResolveTarget(HubInvocationContext context)
    {
        foreach (var argument in context.HubMethodArguments)
        {
            if (argument is int intValue && intValue > 0)
            {
                if (context.HubMethodName.Contains("Message", StringComparison.OrdinalIgnoreCase))
                    return ("Message", intValue.ToString(), null, intValue);

                return ("Chat", intValue.ToString(), intValue, null);
            }

            if (argument is long longValue && longValue > 0)
                return ("User", longValue.ToString(), null, null);
        }

        return (null, null, null, null);
    }
}
