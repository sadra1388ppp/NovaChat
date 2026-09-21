using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.WebUtilities;
using NovaChat.Server.Entities;
using NovaChat.Server.Services;

namespace NovaChat.Server.Middleware;

public sealed class HttpRequestLoggingMiddleware(RequestDelegate next, ILogger<HttpRequestLoggingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, HttpRequestLogService logService)
    {
        var requestText = await BuildRequestTextAsync(context);

        try
        {
            await next(context);
        }
        finally
        {
            try
            {
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
            }
        }
    }

    private static async Task<string> BuildRequestTextAsync(HttpContext context)
    {
        var request = context.Request;
        var builder = new StringBuilder();

        builder.Append(request.Method)
            .Append(' ')
            .Append(request.Path.Value ?? "/")
            .Append(SanitizeQueryString(request.QueryString.Value))
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

            builder.Append(SanitizeBody(body, request.ContentType));
        }

        return builder.ToString();
    }

    private static string SanitizeQueryString(string? queryString)
    {
        if (string.IsNullOrWhiteSpace(queryString))
            return string.Empty;

        var parsed = QueryHelpers.ParseQuery(queryString);
        var parts = new List<string>();

        foreach (var pair in parsed)
        {
            var value = IsSensitive(pair.Key)
                ? "[REDACTED]"
                : string.Join(",", pair.Value.Select(v => v ?? string.Empty));

            parts.Add(
                Uri.EscapeDataString(pair.Key) +
                "=" +
                Uri.EscapeDataString(value));
        }

        return "?" + string.Join("&", parts);
    }

    private static string SanitizeBody(string body, string? contentType)
    {
        if (string.IsNullOrWhiteSpace(body))
            return body;

        if (contentType?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true)
        {
            try
            {
                var node = JsonNode.Parse(body);
                if (node is not null)
                {
                    RedactJson(node);
                    return node.ToJsonString(new JsonSerializerOptions
                    {
                        WriteIndented = false
                    });
                }
            }
            catch (JsonException)
            {
                // Keep the original body when it is not valid JSON.
            }
        }

        if (contentType?.Contains("application/x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase) == true)
        {
            var parsed = QueryHelpers.ParseQuery(body);
            return string.Join(
                "&",
                parsed.Select(pair =>
                {
                    var value = IsSensitive(pair.Key)
                        ? "[REDACTED]"
                        : string.Join(",", pair.Value.Select(v => v ?? string.Empty));

                    return Uri.EscapeDataString(pair.Key) + "=" + Uri.EscapeDataString(value);
                }));
        }

        return body;
    }

    private static void RedactJson(JsonNode node)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToList())
            {
                if (IsSensitive(property.Key))
                    obj[property.Key] = "[REDACTED]";
                else if (property.Value is not null)
                    RedactJson(property.Value);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                    RedactJson(item);
            }
        }
    }

    private static bool IsSensitive(string key)
    {
        return key.Contains("password", StringComparison.OrdinalIgnoreCase)
            || key.Contains("passwd", StringComparison.OrdinalIgnoreCase)
            || key.Contains("token", StringComparison.OrdinalIgnoreCase)
            || key.Contains("secret", StringComparison.OrdinalIgnoreCase)
            || key.Contains("api_key", StringComparison.OrdinalIgnoreCase)
            || key.Contains("apikey", StringComparison.OrdinalIgnoreCase)
            || key.Equals("authorization", StringComparison.OrdinalIgnoreCase);
    }
}
