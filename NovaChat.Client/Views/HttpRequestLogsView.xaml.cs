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
    private const int PageSize = 10000;
    private readonly ApiService _apiService = new();
    private readonly ObservableCollection<HttpRequestLogSearchItem> _logs = [];
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

    private async void SearchButton_Click(object sender, RoutedEventArgs e) => await SearchAsync();

    private void BackButton_Click(object sender, RoutedEventArgs e) => BackToChatRequested?.Invoke();

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchAsync();
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
            await SearchAsync();
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

    private async Task SearchAsync()
    {
        if (_isBusy) return;

        _isBusy = true;
        SetBusyState(true);

        try
        {
            var query = SearchBox.Text.Trim();
            var endpoint = $"api/admin/search/http-requests?page=1&pageSize={PageSize}";

            if (!string.IsNullOrWhiteSpace(query))
                endpoint += $"&q={Uri.EscapeDataString(query)}";

            var response = await _apiService.GetAsync<HttpRequestSearchResponse>(endpoint);

            if (response == null)
                throw new InvalidOperationException("The server returned an empty search response.");

            _logs.Clear();

            foreach (var item in response.Results ?? [])
            {
                item.StartedAtText = FormatDate(item.StartedAt);
                item.CompletedAtText = FormatDate(item.CompletedAt);
                _logs.Add(item);
            }

            ResultCountText.Text = response.Count.ToString("n0", CultureInfo.InvariantCulture);
            EmptyText.Visibility = _logs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            StatusText.Text = string.IsNullOrWhiteSpace(query)
                ? "All indexed requests"
                : $"Search: {query}";
        }
        catch (Exception ex)
        {
            _logs.Clear();
            EmptyText.Visibility = Visibility.Visible;
            ResultCountText.Text = "0";
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

    private void SelectAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _logs.Count == 0) return;

        LogsGrid.SelectAll();
        UpdateSelectionState();
    }

    private async void DeleteSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy) return;

        var ids = LogsGrid.SelectedItems
            .Cast<HttpRequestLogSearchItem>()
            .Select(x => x.Id)
            .Where(x => x > 0)
            .Distinct()
            .ToArray();

        if (ids.Length == 0)
        {
            MessageBox.Show(
                "Select at least one HTTP request first.",
                "Delete HTTP Requests",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var confirmation = MessageBox.Show(
            $"Delete {ids.Length:n0} selected HTTP request(s)?\n\nThis will permanently remove them from MariaDB and Elasticsearch.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        _isBusy = true;
        SetBusyState(true);

        var deleted = false;

        try
        {
            var result = await _apiService.PostAsync<object, DeleteHttpRequestsResponse>(
                "api/admin/search/http-requests/delete",
                new { ids });

            if (result == null)
                throw new InvalidOperationException("The server returned an empty delete response.");

            deleted = true;
            StatusText.Text = result.ElasticsearchSucceeded
                ? $"Deleted {result.DeletedInDatabase:n0} request(s)."
                : $"Deleted {result.DeletedInDatabase:n0} from MariaDB; Elasticsearch cleanup was incomplete.";

            if (!result.ElasticsearchSucceeded)
            {
                MessageBox.Show(
                    "The requests were deleted from MariaDB, but some Elasticsearch documents could not be deleted. Use Reindex to synchronize Elasticsearch.",
                    "Delete HTTP Requests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Delete failed.";
            MessageBox.Show(
                $"Could not delete the selected HTTP requests.\n\n{ex.Message}",
                "Delete HTTP Requests",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isBusy = false;
            SetBusyState(false);
        }

        if (deleted)
            await SearchAsync();
    }

    private async void DeleteAllButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isBusy || _logs.Count == 0) return;

        var confirmation = MessageBox.Show(
            "Delete ALL HTTP requests?\n\nThis permanently removes every record from MariaDB and Elasticsearch. This action cannot be undone.",
            "Confirm Delete All",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirmation != MessageBoxResult.Yes)
            return;

        _isBusy = true;
        SetBusyState(true);

        var deleted = false;

        try
        {
            var result = await _apiService.PostAsync<object, DeleteHttpRequestsResponse>(
                "api/admin/search/http-requests/delete-all",
                new { });

            if (result == null)
                throw new InvalidOperationException("The server returned an empty delete response.");

            deleted = true;
            StatusText.Text = result.ElasticsearchSucceeded
                ? $"Deleted {result.DeletedInDatabase:n0} request(s)."
                : $"Deleted {result.DeletedInDatabase:n0} from MariaDB; Elasticsearch cleanup was incomplete.";

            if (!result.ElasticsearchSucceeded)
            {
                MessageBox.Show(
                    "All MariaDB records were deleted, but Elasticsearch could not be fully cleaned. Use Reindex to synchronize Elasticsearch.",
                    "Delete All HTTP Requests",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "Delete all failed.";
            MessageBox.Show(
                $"Could not delete all HTTP requests.\n\n{ex.Message}",
                "Delete All HTTP Requests",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            _isBusy = false;
            SetBusyState(false);
        }

        if (deleted)
            await SearchAsync();
    }

    private void LogsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionState();
    }

    private void UpdateSelectionState()
    {
        var selectedCount = LogsGrid.SelectedItems.Count;

        if (!_isBusy)
            StatusText.Text = selectedCount > 0
                ? $"{selectedCount:n0} request(s) selected."
                : (string.IsNullOrWhiteSpace(SearchBox.Text)
                    ? "All indexed requests"
                    : $"Search: {SearchBox.Text.Trim()}");

        DeleteSelectedButton.IsEnabled = !_isBusy && selectedCount > 0;
        SelectAllButton.IsEnabled = !_isBusy && _logs.Count > 0;
        DeleteAllButton.IsEnabled = !_isBusy && _logs.Count > 0;
    }

    private void SetBusyState(bool busy)
    {
        SearchButton.IsEnabled = !busy;
        SearchBox.IsEnabled = !busy;
        ReindexButton.IsEnabled = !busy;
        SelectAllButton.IsEnabled = !busy && _logs.Count > 0;
        DeleteSelectedButton.IsEnabled = !busy && LogsGrid.SelectedItems.Count > 0;
        DeleteAllButton.IsEnabled = !busy && _logs.Count > 0;
    }

    private static string FormatDate(DateTime value)
        => value.ToString("yyyy-MM-dd HH:mm:ss.ffffff", CultureInfo.InvariantCulture);

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

    private sealed class DeleteHttpRequestsResponse
    {
        public int DeletedInDatabase { get; set; }
        public long DeletedInElasticsearch { get; set; }
        public bool ElasticsearchSucceeded { get; set; }
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

        [JsonIgnore]
        public string CompletedAtText { get; set; } = string.Empty;
    }
}
