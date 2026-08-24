using System.Net.Http;
using System.Net.Http.Json;

namespace NovaChat.Client.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;

    public ApiService()
    {
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("http://localhost:5256/")
        };
    }

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string endpoint,
        TRequest data)
    {
        var response = await _httpClient.PostAsJsonAsync(endpoint, data);

        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<TResponse>();
    }
}