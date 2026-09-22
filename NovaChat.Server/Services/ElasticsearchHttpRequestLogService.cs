using Elastic.Clients.Elasticsearch;
using Microsoft.EntityFrameworkCore;
using NovaChat.Server.Data;
using NovaChat.Server.Entities;
using NovaChat.Server.Models.Elasticsearch;

namespace NovaChat.Server.Services;

public sealed class ElasticsearchHttpRequestLogService(
    ElasticsearchClient client,
    IConfiguration configuration,
    ILogger<ElasticsearchHttpRequestLogService> logger)
{
    private const string DefaultIndexName = "novachat-http-requests";

    private readonly ElasticsearchClient _client = client;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<ElasticsearchHttpRequestLogService> _logger = logger;

    private string IndexName =>
        _configuration["Elasticsearch:HttpRequestIndexName"]?.Trim() is { Length: > 0 } configured
            ? configured
            : DefaultIndexName;

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var existsResponse = await _client.Indices.ExistsAsync(IndexName, cancellationToken);

            if (existsResponse.Exists)
            {
                _logger.LogInformation("Elasticsearch HTTP request index {IndexName} is ready.", IndexName);
                return;
            }

            var createResponse = await _client.Indices.CreateAsync<HttpRequestLogSearchDocument>(
                IndexName,
                descriptor => descriptor.Mappings(mappings => mappings
                    .Properties(properties => properties
                        .LongNumber(p => p.Id)
                        .Text(p => p.RequestId)
                        .Text(p => p.Method)
                        .Text(p => p.Scheme)
                        .Text(p => p.Host)
                        .Text(p => p.Path)
                        .Text(p => p.QueryString)
                        .Text(p => p.Protocol)
                        .IntegerNumber(p => p.StatusCode)
                        .Boolean(p => p.IsAuthenticated)
                        .LongNumber(p => p.UserId)
                        .Text(p => p.Username)
                        .Text(p => p.IpAddress)
                        .Text(p => p.UserAgent)
                        .Text(p => p.RequestContentType)
                        .LongNumber(p => p.RequestContentLength)
                        .Text(p => p.ResponseContentType)
                        .LongNumber(p => p.ResponseContentLength)
                        .Text(p => p.ResponseBody)
                        .Date(p => p.StartedAt)
                        .Date(p => p.CompletedAt)
                        .LongNumber(p => p.DurationMs)
                        .Boolean(p => p.Succeeded))));

            if (!createResponse.IsValidResponse)
            {
                _logger.LogWarning(
                    "Elasticsearch HTTP request index {IndexName} could not be created. DebugInformation: {DebugInformation}",
                    IndexName,
                    createResponse.DebugInformation);
                return;
            }

            _logger.LogInformation("Elasticsearch HTTP request index {IndexName} was created.", IndexName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Elasticsearch is unavailable while preparing HTTP request index {IndexName}. MariaDB remains the source of truth.",
                IndexName);
        }
    }

    public Task IndexAsync(HttpRequestLog row, CancellationToken cancellationToken = default)
        => IndexAsync(Map(row), cancellationToken);

    public async Task ReindexIfEmptyAsync(
        HttpRequestLogDbContext db,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var countResponse = await _client.CountAsync<HttpRequestLogSearchDocument>(
                request => request.Indices(IndexName),
                cancellationToken);

            if (!countResponse.IsValidResponse || countResponse.Count > 0)
                return;

            var result = await ReindexAsync(db, cancellationToken);

            _logger.LogInformation(
                "Initial Elasticsearch HTTP request sync completed. Indexed: {Indexed}, Failed: {Failed}.",
                result.Indexed,
                result.Failed);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Initial Elasticsearch HTTP request sync could not be completed.");
        }
    }

    public async Task IndexAsync(
        HttpRequestLogSearchDocument document,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.IndexAsync(
                document,
                IndexName,
                Id.From(document.Id),
                cancellationToken);

            if (!response.IsValidResponse)
            {
                _logger.LogWarning(
                    "Elasticsearch failed to index HTTP request {RequestId}. DebugInformation: {DebugInformation}",
                    document.RequestId,
                    response.DebugInformation);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Elasticsearch indexing failed for HTTP request {RequestId}.",
                document.RequestId);
        }
    }

    public async Task<ReindexResult> ReindexAsync(
        HttpRequestLogDbContext db,
        CancellationToken cancellationToken = default)
    {
        await EnsureIndexAsync(cancellationToken);

        var indexed = 0;
        var failed = 0;
        const int batchSize = 250;
        var lastId = 0L;

        while (true)
        {
            var rows = await db.HttpRequests
                .AsNoTracking()
                .Where(x => x.Id > lastId)
                .OrderBy(x => x.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (rows.Count == 0)
                break;

            foreach (var row in rows)
            {
                var document = Map(row);

                try
                {
                    var response = await _client.IndexAsync(
                        document,
                        IndexName,
                        Id.From(document.Id),
                        cancellationToken);

                    if (response.IsValidResponse)
                        indexed++;
                    else
                    {
                        failed++;
                        _logger.LogWarning(
                            "Elasticsearch failed to reindex HTTP request {RequestId}. DebugInformation: {DebugInformation}",
                            document.RequestId,
                            response.DebugInformation);
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed++;
                    _logger.LogWarning(
                        exception,
                        "Elasticsearch reindexing failed for HTTP request {RequestId}.",
                        document.RequestId);
                }

                lastId = row.Id;
            }
        }

        return new ReindexResult(indexed, failed);
    }

    public async Task<IReadOnlyList<HttpRequestLogSearchDocument>> SearchAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 10000);

        try
        {
            var response = string.IsNullOrWhiteSpace(query)
                ? await _client.SearchAsync<HttpRequestLogSearchDocument>(search => search
                    .Indices(IndexName)
                    .From((page - 1) * pageSize)
                    .Size(pageSize)
                    .Query(q => q.MatchAll())
                    .Sort(sort => sort.Field(f => f.Id, field => field.Order(SortOrder.Desc))), cancellationToken)
                : await _client.SearchAsync<HttpRequestLogSearchDocument>(search => search
                    .Indices(IndexName)
                    .From((page - 1) * pageSize)
                    .Size(pageSize)
                    .Query(q => q.QueryString(qs => qs.Query(query)))
                    .Sort(sort => sort.Field(f => f.StartedAt, field => field.Order(SortOrder.Desc))), cancellationToken);

            if (!response.IsValidResponse)
            {
                _logger.LogWarning(
                    "Elasticsearch HTTP request search failed. DebugInformation: {DebugInformation}",
                    response.DebugInformation);
                return [];
            }

            return response.Documents.ToList();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Elasticsearch HTTP request search failed.");
            return [];
        }
    }

    private static HttpRequestLogSearchDocument Map(HttpRequestLog row) => new()
    {
        Id = row.Id,
        RequestId = row.RequestId,
        Method = row.Method,
        Scheme = row.Scheme,
        Host = row.Host,
        Path = row.Path,
        QueryString = row.QueryString,
        Protocol = row.Protocol,
        StatusCode = row.StatusCode,
        IsAuthenticated = row.IsAuthenticated,
        UserId = row.UserId,
        Username = row.Username,
        IpAddress = row.IpAddress,
        UserAgent = row.UserAgent,
        RequestContentType = row.RequestContentType,
        RequestContentLength = row.RequestContentLength,
        ResponseContentType = row.ResponseContentType,
        ResponseContentLength = row.ResponseContentLength,
        ResponseBody = row.ResponseBody,
        StartedAt = row.StartedAt,
        CompletedAt = row.CompletedAt,
        DurationMs = row.DurationMs,
        Succeeded = row.Succeeded
    };
}

public sealed record ReindexResult(int Indexed, int Failed);
