using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NovaChat.Client.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;

    public ApiService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress =
                new Uri("http://localhost:5256/")
        };
    }

    private void AddAuthorization()
    {
        _httpClient.DefaultRequestHeaders.Authorization =
            null;

        if (!string.IsNullOrWhiteSpace(
                AuthState.Token))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    AuthState.Token);
        }
    }

    public async Task<TResponse?> PostAsync<
        TRequest,
        TResponse>(
        string endpoint,
        TRequest data)
    {
        AddAuthorization();

        var response =
            await _httpClient.PostAsJsonAsync(
                endpoint,
                data);

        if (!response.IsSuccessStatusCode)
            return default;

        return await response.Content
            .ReadFromJsonAsync<TResponse>();
    }

    public async Task<TResponse?> GetAsync<TResponse>(
        string endpoint)
    {
        AddAuthorization();

        var response =
            await _httpClient.GetAsync(endpoint);

        if (!response.IsSuccessStatusCode)
            return default;

        return await response.Content
            .ReadFromJsonAsync<TResponse>();
    }

    public async Task<bool> DeleteAsync(
        string endpoint)
    {
        AddAuthorization();

        var response =
            await _httpClient.DeleteAsync(endpoint);

        return response.IsSuccessStatusCode;
    }
}