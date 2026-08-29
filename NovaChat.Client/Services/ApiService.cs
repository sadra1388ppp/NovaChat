using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace NovaChat.Client.Services;

public class ApiService
{
    private readonly HttpClient _httpClient = new()
    {
        BaseAddress = new Uri("http://localhost:5256/")
    };

    private void AddAuthorization()
    {
        _httpClient.DefaultRequestHeaders.Authorization = null;

        if (!string.IsNullOrWhiteSpace(AuthState.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", AuthState.Token);
        }
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest data)
    {
        AddAuthorization();
        var response = await _httpClient.PostAsJsonAsync(endpoint, data);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }

    public async Task<TResponse?> GetAsync<TResponse>(string endpoint)
    {
        AddAuthorization();
        var response = await _httpClient.GetAsync(endpoint);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(string endpoint, TRequest data)
    {
        AddAuthorization();
        var response = await _httpClient.PutAsJsonAsync(endpoint, data);
        await EnsureSuccessAsync(response, endpoint);
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }

    public async Task<bool> DeleteAsync(string endpoint)
    {
        AddAuthorization();
        var response = await _httpClient.DeleteAsync(endpoint);
        await EnsureSuccessAsync(response, endpoint);
        return true;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, string endpoint)
    {
        if (response.IsSuccessStatusCode)
            return;

        var body = await response.Content.ReadAsStringAsync();
        var message = TryGetServerMessage(body);

        throw new HttpRequestException(
            $"Request failed ({(int)response.StatusCode} {response.StatusCode}) for '{endpoint}'. " +
            (string.IsNullOrWhiteSpace(message) ? body : message));
    }

    private static string? TryGetServerMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("message", out var message))
                return message.GetString();

            if (document.RootElement.TryGetProperty("title", out var title))
                return title.GetString();
        }
        catch (JsonException)
        {
        }

        return null;
    }
}
