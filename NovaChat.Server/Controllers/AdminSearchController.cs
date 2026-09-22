using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NovaChat.Server.Data;

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
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var results = await searchService.SearchAsync(q, page, pageSize, cancellationToken);

        return Ok(new
        {
            query = q,
            page = Math.Max(1, page),
            pageSize = Math.Clamp(pageSize, 1, 100),
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
