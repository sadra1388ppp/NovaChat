using System.Diagnostics;
using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.WebUtilities;
using NovaChat.Server.Entities;
using NovaChat.Server.Services;

namespace NovaChat.Server.Middleware;

public sealed class HttpRequestLoggingMiddleware(RequestDelegate next, ILogger<HttpRequestLoggingMiddleware> logger)
{
    private static readonly Regex SensitiveQueryKey = new(
        "(^|[_-])(access_token|token|password|passwd|secret|api[_-]?key)([_-]|$)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public async Task InvokeAsync(HttpContext context, HttpRequestLogService logService)
    {
        var startedAt = DateTime.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        Exception? exception = null;
        string? responseBody = null;

        var originalResponseBody = context.Response.Body;
        await using var responseBuffer = new MemoryStream();
        context.Response.Body = responseBuffer;

        try
        {
            context.Response.Headers["X-Request-ID"] = context.TraceIdentifier;
            await next(context);
        }
        catch (Exception ex)
        {
            exception = ex;
            throw;
        }
        finally
        {
            stopwatch.Stop();

            try
            {
                responseBody = await ReadResponseBodyAsync(responseBuffer);
                responseBuffer.Position = 0;
                await responseBuffer.CopyToAsync(originalResponseBody, CancellationToken.None);
            }
            finally
            {
                context.Response.Body = originalResponseBody;
            }

            try
            {
                var userId = long.TryParse(
                    context.User.FindFirstValue(ClaimTypes.NameIdentifier),
                    out var parsedUserId)
                    ? parsedUserId
                    : (long?)null;

                var username = context.User.FindFirst("username")?.Value;
                var query = SanitizeQueryString(context.Request.QueryString.Value);

                await logService.LogAsync(
                    new HttpRequestLog
                    {
                        RequestId = context.TraceIdentifier,
                        Method = context.Request.Method,
                        Scheme = context.Request.Scheme,
                        Host = context.Request.Host.Value,
                        Path = context.Request.Path.Value ?? string.Empty,
                        QueryString = query,
                        Protocol = context.Request.Protocol,
                        StatusCode = context.Response.StatusCode,
                        IsAuthenticated = context.User.Identity?.IsAuthenticated == true,
                        UserId = userId,
                        Username = string.IsNullOrWhiteSpace(username) ? null : username,
                        IpAddress = context.Connection.RemoteIpAddress?.ToString(),
                        UserAgent = context.Request.Headers.UserAgent.ToString(),
                        RequestContentType = context.Request.ContentType,
                        RequestContentLength = context.Request.ContentLength,
                        ResponseContentType = context.Response.ContentType,
                        ResponseContentLength = context.Response.ContentLength,
                        ResponseBody = responseBody,
                        StartedAt = startedAt,
                        CompletedAt = DateTime.UtcNow,
                        DurationMs = stopwatch.ElapsedMilliseconds,
                        Succeeded = exception == null && context.Response.StatusCode < 400
                    },
                    CancellationToken.None);
            }
            catch (Exception logException)
            {
                logger.LogError(logException, "NovaChat HTTP request logging failed for {RequestId}.", context.TraceIdentifier);
            }
        }
    }

    private static async Task<string> ReadResponseBodyAsync(Stream responseStream)
    {
        if (responseStream.Length == 0)
            return string.Empty;

        responseStream.Position = 0;

        using var reader = new StreamReader(
            responseStream,
            System.Text.Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);

        return await reader.ReadToEndAsync(CancellationToken.None);
    }

    private static string? SanitizeQueryString(string? queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
            return null;

        var parsed = QueryHelpers.ParseQuery(queryString);
        var parts = new List<string>();

        foreach (var pair in parsed)
        {
            if (SensitiveQueryKey.IsMatch(pair.Key))
                continue;

            foreach (var value in pair.Value)
                parts.Add(Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(value ?? string.Empty));
        }

        return parts.Count == 0 ? null : "?" + string.Join("&", parts);
    }
}
