using NovaChat.Client.Services;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NovaChat.Client.Views;

public partial class HttpRequestLogsView : UserControl
{
    private const int PageSize = 50;
    private readonly ApiService _apiService = new();
    private readonly ObservableCollection<HttpRequestLogSearchItem> _logs = [];
    private int _page = 1;
    private bool _isBusy;

    public event Action? BackToChatRequested;

    public HttpRequestLogsView()
    {
        InitializeComponent();
        LogsGrid.ItemsSource = _logs;
        Loaded += HttpRequestLogsView_Loaded;
    }

    private async void HttpRequestLogsView_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= HttpRequestLogsView_Loaded;
        await SearchAsync();
    }

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await SearchAsync(1);

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync(1);
    }

    private async void ReindexButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        _isBusy = true;
        SetBusyState(true);
        StatusText.Text = "Reindexing MariaDB logs...";

        try
        {
            var result = await _apiService.PostAsync<object, ReindexResponse>(
                "api/admin/search/http-requests/reindex",
                new { });

            if (result == null)
                throw new InvalidOperationException("The server returned an empty reindex response.");

            StatusText.Text = $"Indexed {result.Indexed:n0}; failed {result.Failed:n0}.";
            await SearchAsync(_page);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Reindex failed.";
            MessageBox.Show(
                $"Could not reindex HTTP requests.\n\n{ex.Message}",
                "HTTP Request Search",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isBusy = false;
            SetBusyState(false);
        }
    }

    private async Task SearchAsync(int? page = null)
    {
        if (_isBusy && page.HasValue) return;

        _isBusy = true;
        SetBusyState(true);

        var targetPage = page.GetValueOrDefault(_page);
        if (targetPage < 1) targetPage = 1;

        try
        {
            var query = SearchBox.Text.Trim();
            var endpoint = $"api/admin/search/http-requests?page={targetPage}&pageSize={PageSize}";
            if (!string.IsNullOrWhiteSpace(query))
                endpoint += $"&q={Uri.EscapeDataString(query)}";

            var response = await _apiService.GetAsync<HttpRequestSearchResponse>(endpoint);
            if (response == null)
                throw new InvalidOperationException("The server returned an empty search response.");

            _page = Math.Max(1, response.Page);
            _logs.Clear();

            foreach (var item in response.Results ?? [])
            {
                item.StartedAtText = FormatDate(item.StartedAt);
                _logs.Add(item);
            }

            ResultCountText.Text = response.Count.ToString("n0", CultureInfo.InvariantCulture);
            EmptyText.Visibility = _logs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            PreviousButton.IsEnabled = _page > 1;
            NextButton.IsEnabled = _logs.Count == PageSize;
            PageText.Text = $"Page {_page}";

            StatusText.Text = string.IsNullOrWhiteSpace(query)
                ? "Latest indexed requests"
                : $"Search: {query}";

            if (_logs.Count == 0)
                ClearDetails();
        }
        catch (Exception ex)
        {
            _logs.Clear();
            EmptyText.Visibility = Visibility.Visible;
            ResultCountText.Text = "0";
            PreviousButton.IsEnabled = false;
            NextButton.IsEnabled = false;
            PageText.Text = $"Page {targetPage}";
            StatusText.Text = "Search failed.";

            MessageBox.Show(
                $"Could not search HTTP requests.\n\n{ex.Message}",
                "HTTP Request Search",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isBusy = false;
            SetBusyState(false);
        }
    }

    private async void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        if (_page <= 1) return;
        await SearchAsync(_page - 1);
    }

    private async void NextButton_Click(object sender, RoutedEventArgs e)
    {
        if (_logs.Count < PageSize) return;
        await SearchAsync(_page + 1);
    }

    private void LogsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LogsGrid.SelectedItem is not HttpRequestLogSearchItem selected)
        {
            ClearDetails();
            return;
        }

        SelectedRequestText.Text = $"Request #{selected.Id} • {selected.StartedAtText}";
        RequestMethodText.Text = $"{selected.Method} {selected.Protocol} • {selected.Scheme}://{selected.Host}";
        RequestPathText.Text = selected.Path;
        RequestQueryText.Text = string.IsNullOrWhiteSpace(selected.QueryString)
            ? "No query string"
            : selected.QueryString;

        ResponseStatusText.Text = $"{selected.StatusCode} • {(selected.Succeeded ? "Succeeded" : "Failed")}";
        ResponseContentTypeText.Text = string.IsNullOrWhiteSpace(selected.ResponseContentType)
            ? "Response type: unknown"
            : $"Response type: {selected.ResponseContentType}";
        DurationText.Text = $"Duration: {selected.DurationMs:n0} ms";

        UsernameText.Text = string.IsNullOrWhiteSpace(selected.Username)
            ? "User: Anonymous"
            : $"User: @{selected.Username}";
        IpAddressText.Text = string.IsNullOrWhiteSpace(selected.IpAddress)
            ? "IP: Unknown"
            : $"IP: {selected.IpAddress}";
        UserAgentText.Text = string.IsNullOrWhiteSpace(selected.UserAgent)
            ? "User agent: Unknown"
            : $"User agent: {selected.UserAgent}";

        ResponseBodyText.Text = string.IsNullOrWhiteSpace(selected.ResponseBody)
            ? "(empty)"
            : selected.ResponseBody;
    }

    private void ClearDetails()
    {
        SelectedRequestText.Text = "Select a request to inspect its details.";
        RequestMethodText.Text = string.Empty;
        RequestPathText.Text = string.Empty;
        RequestQueryText.Text = string.Empty;
        ResponseStatusText.Text = string.Empty;
        ResponseContentTypeText.Text = string.Empty;
        DurationText.Text = string.Empty;
        UsernameText.Text = string.Empty;
        IpAddressText.Text = string.Empty;
        UserAgentText.Text = string.Empty;
        ResponseBodyText.Text = string.Empty;
    }

    private void SetBusyState(bool busy)
    {
        SearchButton.IsEnabled = !busy;
        SearchBox.IsEnabled = !busy;
        ReindexButton.IsEnabled = !busy;
        PreviousButton.IsEnabled = !busy && _page > 1;
        NextButton.IsEnabled = !busy && _logs.Count == PageSize;
    }

    private static string FormatDate(DateTime value)
        => value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private sealed class HttpRequestSearchResponse
    {
        public string? Query { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int Count { get; set; }
        public List<HttpRequestLogSearchItem> Results { get; set; } = [];
    }

    private sealed class ReindexResponse
    {
        public int Indexed { get; set; }
        public int Failed { get; set; }
    }

    private sealed class HttpRequestLogSearchItem
    {
        public long Id { get; set; }
        public string? RequestId { get; set; }
        public string? Method { get; set; }
        public string? Scheme { get; set; }
        public string? Host { get; set; }
        public string? Path { get; set; }
        public string? QueryString { get; set; }
        public string? Protocol { get; set; }
        public int StatusCode { get; set; }
        public bool IsAuthenticated { get; set; }
        public long? UserId { get; set; }
        public string? Username { get; set; }
        public string? IpAddress { get; set; }
        public string? UserAgent { get; set; }
        public string? RequestContentType { get; set; }
        public long? RequestContentLength { get; set; }
        public string? ResponseContentType { get; set; }
        public long? ResponseContentLength { get; set; }
        public string? ResponseBody { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime CompletedAt { get; set; }
        public long DurationMs { get; set; }
        public bool Succeeded { get; set; }

        [JsonIgnore]
        public string StartedAtText { get; set; } = string.Empty;
    }

}