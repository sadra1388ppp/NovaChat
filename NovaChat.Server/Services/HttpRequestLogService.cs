using NovaChat.Server.Data;
using NovaChat.Server.Entities;

namespace NovaChat.Server.Services;

public sealed class HttpRequestLogService(HttpRequestLogDbContext db)
{
    private readonly HttpRequestLogDbContext _db = db;

    public async Task LogAsync(HttpRequestLog row, CancellationToken cancellationToken = default)
    {
        _db.HttpRequests.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
