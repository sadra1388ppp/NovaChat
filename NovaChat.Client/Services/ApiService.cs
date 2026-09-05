using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovaChat.Client.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    public ApiService()
    {
        _httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5256/") };
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new FlexibleStringConverter());
        return options;
    }

    public string BuildAbsoluteUrl(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)) return string.Empty;
        return Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absolute)
            ? absolute.ToString()
            : new Uri(_httpClient.BaseAddress!, relativeUrl).ToString();
    }

    private void AddAuthorization()
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;
        if (!string.IsNullOrWhiteSpace(AuthState.Token))
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AuthState.Token);
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest data)
    {
        AddAuthorization();
        using var response = await _httpClient.PostAsJsonAsync(endpoint, data);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(string endpoint, TRequest data)
    {
        AddAuthorization();
        using var response = await _httpClient.PutAsJsonAsync(endpoint, data);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
    }

    public async Task<TResponse?> GetAsync<TResponse>(string endpoint)
    {
        AddAuthorization();
        using var response = await _httpClient.GetAsync(endpoint);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
    }

    public async Task<byte[]?> GetBytesAsync(string endpoint)
    {
        AddAuthorization();
        using var response = await _httpClient.GetAsync(endpoint, HttpCompletionOption.ResponseHeadersRead);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<TResponse?> UploadFileAsync<TResponse>(string endpoint, string filePath, string fieldName = "file")
    {
        AddAuthorization();

        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(stream);

        var mediaType = System.IO.Path.GetExtension(filePath).ToLowerInvariant() switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".wav" => "audio/wav",
            ".mp3" => "audio/mpeg",
            ".m4a" => "audio/mp4",
            ".mp4" => "video/mp4",
            ".mov" => "video/quicktime",
            ".webm" => "video/webm",
            _ => "application/octet-stream"
        };

        fileContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        form.Add(fileContent, fieldName, System.IO.Path.GetFileName(filePath));

        using var response = await _httpClient.PostAsync(endpoint, form);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
    }

    public async Task<bool> DeleteAsync(string endpoint)
    {
        AddAuthorization();
        using var response = await _httpClient.DeleteAsync(endpoint);
        await EnsureSuccessAsync(response, endpoint);
        return true;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string endpoint)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content.ReadAsStringAsync();
        var message = ExtractErrorMessage(body);
        throw new HttpRequestException(
            $"API {endpoint} failed ({(int)response.StatusCode} {response.ReasonPhrase}): {message}");
    }

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return "The server returned no error details.";

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var message))
                return message.ValueKind == JsonValueKind.String ? message.GetString() ?? body : message.ToString();
            if (document.RootElement.TryGetProperty("title", out var title))
                return title.ValueKind == JsonValueKind.String ? title.GetString() ?? body : title.ToString();
        }
        catch (JsonException)
        {
            // Fall back to plain-text response bodies.
        }

        return body.Length > 1000 ? body[..1000] : body;
    }

    private sealed class FlexibleStringConverter : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.Number => reader.GetRawText(),
                JsonTokenType.True => "true",
                JsonTokenType.False => "false",
                JsonTokenType.Null => null,
                _ => throw new JsonException($"Cannot convert JSON token {reader.TokenType} to string.")
            };
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
