using NovaChat.Server.Data;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public sealed class HttpRequestLogService(
    HttpRequestLogDbContext db,
    ElasticsearchHttpRequestLogService elasticsearch)
{
    private readonly HttpRequestLogDbContext _db = db;
    private readonly ElasticsearchHttpRequestLogService _elasticsearch = elasticsearch;

    public async Task LogAsync(HttpRequestLog row, CancellationToken cancellationToken = default)
    {
        _db.HttpRequests.Add(row);
        await _db.SaveChangesAsync(cancellationToken);

        await _elasticsearch.IndexAsync(row, cancellationToken);
    }
}
