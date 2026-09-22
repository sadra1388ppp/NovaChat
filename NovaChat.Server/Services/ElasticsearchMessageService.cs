using Elastic.Clients.Elasticsearch;
using NovaChat.Server.Entities;
using NovaChat.Server.Models.Elasticsearch;

namespace NovaChat.Server.Services;

public sealed class ElasticsearchMessageService(
    ElasticsearchClient client,
    IConfiguration configuration,
    ILogger<ElasticsearchMessageService> logger)
{
    private const string DefaultIndexName = "novachat-messages";

    private readonly ElasticsearchClient _client = client;
    private readonly IConfiguration _configuration = configuration;
    private readonly ILogger<ElasticsearchMessageService> _logger = logger;

    private string IndexName =>
        _configuration["Elasticsearch:IndexName"]?.Trim() is { Length: > 0 } configured
            ? configured
            : DefaultIndexName;

    public async Task EnsureIndexAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var existsResponse = await _client.Indices.ExistsAsync(IndexName, cancellationToken);

            if (existsResponse.Exists)
            {
                _logger.LogInformation("Elasticsearch index {IndexName} is ready.", IndexName);
                return;
            }

            var createResponse = await _client.Indices.CreateAsync<MessageSearchDocument>(
                IndexName,
                descriptor => descriptor.Mappings(mappings => mappings
                    .Properties(properties => properties
                        .IntegerNumber(p => p.MessageId)
                        .IntegerNumber(p => p.ChatId)
                        .Keyword(p => p.SenderId)
                        .Date(p => p.SentAt))));

            if (!createResponse.IsValidResponse)
            {
                _logger.LogWarning(
                    "Elasticsearch index {IndexName} could not be created. DebugInformation: {DebugInformation}",
                    IndexName,
                    createResponse.DebugInformation);
                return;
            }

            _logger.LogInformation("Elasticsearch index {IndexName} was created.", IndexName);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Elasticsearch is unavailable while preparing index {IndexName}. MariaDB remains the source of truth.",
                IndexName);
        }
    }

    public async Task IndexMessageAsync(Message message, CancellationToken cancellationToken = default)
    {
        var document = new MessageSearchDocument
        {
            MessageId = message.Id,
            ChatId = message.ChatId,
            SenderId = message.SenderId,
            SentAt = message.SentAt
        };

        try
        {
            var response = await _client.IndexAsync(
                document,
                IndexName,
                Id.From(message.Id),
                cancellationToken);

            if (!response.IsValidResponse)
            {
                _logger.LogWarning(
                    "Elasticsearch failed to index message {MessageId}. DebugInformation: {DebugInformation}",
                    message.Id,
                    response.DebugInformation);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Elasticsearch indexing failed for message {MessageId}.",
                message.Id);
        }
    }

    public async Task DeleteMessageAsync(int messageId, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _client.DeleteAsync(
                IndexName,
                Id.From(messageId),
                cancellationToken);

            if (!response.IsValidResponse && response.Result != Result.NotFound)
            {
                _logger.LogWarning(
                    "Elasticsearch failed to delete message {MessageId}. DebugInformation: {DebugInformation}",
                    messageId,
                    response.DebugInformation);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(
                exception,
                "Elasticsearch deletion failed for message {MessageId}.",
                messageId);
        }
    }
}
