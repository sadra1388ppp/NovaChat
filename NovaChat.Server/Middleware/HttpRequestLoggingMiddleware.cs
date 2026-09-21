using System.Text;
using NovaChat.Server.Entities;
using NovaChat.Server.Services;

namespace NovaChat.Server.Middleware;

public sealed class HttpRequestLoggingMiddleware(RequestDelegate next, ILogger<HttpRequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, HttpRequestLogService logService)
    {
        try
        {
            var requestText = await BuildRequestTextAsync(context);

            await next(context);

            await logService.LogAsync(
                new HttpRequestLog
                {
                    Request = requestText
                },
                CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "NovaChat HTTP request logging failed.");
            throw;
        }
    }

    private static async Task<string> BuildRequestTextAsync(HttpContext context)
    {
        var request = context.Request;
        var builder = new StringBuilder();

        var query = request.QueryString.Value ?? string.Empty;
        builder.Append(request.Method)
            .Append(' ')
            .Append(request.Path.Value ?? "/")
            .Append(query)
            .Append(' ')
            .Append(request.Protocol)
            .Append("\r\n");

        foreach (var header in request.Headers)
        {
            var value = header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                ? "[REDACTED]"
                : header.Value.ToString();

            builder.Append(header.Key)
                .Append(": ")
                .Append(value)
                .Append("\r\n");
        }

        builder.Append("\r\n");

        if (request.ContentLength is > 0)
        {
            request.EnableBuffering();
            request.Body.Position = 0;

            using var reader = new StreamReader(
                request.Body,
                Encoding.UTF8,
                detectEncodingFromByteOrderMarks: true,
                leaveOpen: true);

            var body = await reader.ReadToEndAsync(CancellationToken.None);
            request.Body.Position = 0;

            builder.Append(body);
        }

        return builder.ToString();
    }
}
