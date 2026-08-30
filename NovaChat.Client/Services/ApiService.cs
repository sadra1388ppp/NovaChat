using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NovaChat.Client.Services;

public class ApiService
{
    private readonly HttpClient _httpClient;

    public ApiService()
    {
        _httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:5256/") };
    }

    public string BuildAbsoluteUrl(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl)) return string.Empty;
        return Uri.TryCreate(relativeUrl, UriKind.Absolute, out var absolute) ? absolute.ToString() : new Uri(_httpClient.BaseAddress!, relativeUrl).ToString();
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
        var response = await _httpClient.PostAsJsonAsync(endpoint, data);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }

    public async Task<TResponse?> PutAsync<TRequest, TResponse>(string endpoint, TRequest data)
    {
        AddAuthorization();
        var response = await _httpClient.PutAsJsonAsync(endpoint, data);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }

    public async Task<TResponse?> GetAsync<TResponse>(string endpoint)
    {
        AddAuthorization();
        var response = await _httpClient.GetAsync(endpoint);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }

    public async Task<TResponse?> UploadFileAsync<TResponse>(string endpoint, string filePath, string fieldName = "file")
    {
        AddAuthorization();
        using var form = new MultipartFormDataContent();
        await using var stream = File.OpenRead(filePath);
        using var fileContent = new StreamContent(stream);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(fileContent, fieldName, Path.GetFileName(filePath));
        var response = await _httpClient.PostAsync(endpoint, form);
        if (!response.IsSuccessStatusCode) return default;
        return await response.Content.ReadFromJsonAsync<TResponse>();
    }

    public async Task<bool> DeleteAsync(string endpoint)
    {
        AddAuthorization();
        var response = await _httpClient.DeleteAsync(endpoint);
        return response.IsSuccessStatusCode;
    }
}