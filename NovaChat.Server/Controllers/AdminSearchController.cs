using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaChat.Server.Data;
using NovaChat.Server.Services;

namespace NovaChat.Server.Controllers;

[ApiController]
[Route("api/admin/search")]
[Authorize(Policy = "OwnerOnly")]
public sealed class AdminSearchController(
    ElasticsearchHttpRequestLogService searchService,
    HttpRequestLogDbContext db) : ControllerBase
{
    [HttpGet("http-requests")]
    public async Task<IActionResult> SearchHttpRequests(
        [FromQuery] string? q,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 10000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            var allRows = await db.HttpRequests
                .AsNoTracking()
                .OrderByDescending(x => x.Id)
                .ToListAsync(cancellationToken);

            var allResults = allRows.Select(x => new NovaChat.Server.Models.Elasticsearch.HttpRequestLogSearchDocument
            {
                Id = x.Id,
                RequestId = x.RequestId,
                Method = x.Method,
                Scheme = x.Scheme,
                Host = x.Host,
                Path = x.Path,
                QueryString = x.QueryString,
                Protocol = x.Protocol,
                StatusCode = x.StatusCode,
                IsAuthenticated = x.IsAuthenticated,
                UserId = x.UserId,
                Username = x.Username,
                IpAddress = x.IpAddress,
                UserAgent = x.UserAgent,
                RequestContentType = x.RequestContentType,
                RequestContentLength = x.RequestContentLength,
                ResponseContentType = x.ResponseContentType,
                ResponseContentLength = x.ResponseContentLength,
                ResponseBody = x.ResponseBody,
                StartedAt = x.StartedAt,
                CompletedAt = x.CompletedAt,
                DurationMs = x.DurationMs,
                Succeeded = x.Succeeded
            }).ToList();

            return Ok(new
            {
                query = q,
                page = 1,
                pageSize = allResults.Count,
                count = allResults.Count,
                results = allResults
            });
        }

        var results = await searchService.SearchAsync(q, page, pageSize, cancellationToken);

        return Ok(new
        {
            query = q,
            page = Math.Max(1, page),
            pageSize = Math.Clamp(pageSize, 1, 10000),
            count = results.Count,
            results
        });
    }

    [HttpPost("http-requests/reindex")]
    public async Task<IActionResult> ReindexHttpRequests(CancellationToken cancellationToken = default)
    {
        var result = await searchService.ReindexAsync(db, cancellationToken);
        return Ok(result);
    }
}
