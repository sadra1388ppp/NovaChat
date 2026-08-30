using Microsoft.Win32;
using NAudio.Wave;
using NovaChat.Client.Models;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private bool _mediaUiRegistered;
    private WaveInEvent? _voiceRecorder;
    private WaveFileWriter? _voiceWriter;
    private string? _voicePath;
    private DateTime _voiceStartedAt;
    private readonly Dictionary<int, SoundPlayer> _voicePlayers = [];

    internal static void RegisterMediaFeatures()
    {
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(MediaMainViewLoaded));
        EventManager.RegisterClassHandler(typeof(Border), FrameworkElement.LoadedEvent, new RoutedEventHandler(MediaBubbleLoaded));
    }

    private static void MediaMainViewLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainView view) view.AttachMediaControls();
    }

    private void AttachMediaControls()
    {
        if (_mediaUiRegistered) return;
        _mediaUiRegistered = true;

        var attach = FindDescendant<Button>(this, b => b.ToolTip is string s && s == "Attach");
        if (attach != null) attach.Click += AttachButton_Click;

        var messageBox = FindDescendant<TextBox>(this, b => b.Name == "MessageTextBox");
        var border = messageBox?.Parent as Border;
        var grid = border?.Parent as Grid;
        if (grid == null) return;

        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(50) });
        var send = grid.Children.OfType<Button>().FirstOrDefault(b => string.Equals(b.Content?.ToString(), "➤", StringComparison.Ordinal));
        if (send != null) Grid.SetColumn(send, 3);

        var voice = new Button
        {
            Content = "🎙",
            Width = 42,
            Height = 42,
            Margin = new Thickness(2, 0, 0, 0),
            Background = (Brush)FindResource("PanelBackgroundBrush"),
            Foreground = (Brush)FindResource("PrimaryBrush"),
            BorderBrush = (Brush)FindResource("BorderBrush"),
            ToolTip = "Voice message",
            Tag = "NovaChat.Voice"
        };
        Grid.SetColumn(voice, 2);
        grid.Children.Add(voice);
        voice.Click += VoiceButton_Click;
    }

    private async void AttachButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentChatId.HasValue || !AuthState.IsAuthenticated) return;
        var dialog = new OpenFileDialog
        {
            Title = "Send photo or file",
            Filter = "Photos and files|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.pdf;*.doc;*.docx;*.xls;*.xlsx;*.ppt;*.pptx;*.zip;*.rar;*.7z;*.txt;*.csv;*.json;*.mp4;*.mov;*.mkv;*.webm|All files|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog() != true) return;

        var ext = Path.GetExtension(dialog.FileName);
        var type = new[] { ".jpg", ".jpeg", ".png", ".webp", ".gif" }.Contains(ext, StringComparer.OrdinalIgnoreCase) ? "image" : "file";
        await UploadMediaAsync(type, dialog.FileName);
    }

    private async void VoiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_currentChatId.HasValue || !AuthState.IsAuthenticated) return;
        if (_voiceRecorder != null)
        {
            StopVoiceRecording();
            return;
        }

        try
        {
            var temp = Path.Combine(Path.GetTempPath(), $"NovaChatVoice_{Guid.NewGuid():N}.wav");
            _voicePath = temp;
            _voiceStartedAt = DateTime.UtcNow;
            _voiceWriter = new WaveFileWriter(temp, new WaveFormat(44100, 16, 1));
            _voiceRecorder = new WaveInEvent { WaveFormat = new WaveFormat(44100, 16, 1) };
            _voiceRecorder.DataAvailable += (_, args) => _voiceWriter?.Write(args.Buffer, 0, args.BytesRecorded);
            _voiceRecorder.RecordingStopped += async (_, _) =>
            {
                _voiceWriter?.Dispose(); _voiceWriter = null;
                var recorder = _voiceRecorder; _voiceRecorder = null; recorder?.Dispose();
                var path = _voicePath; _voicePath = null;
                if (path == null || !File.Exists(path)) return;
                try
                {
                    var seconds = Math.Max(0.1, (DateTime.UtcNow - _voiceStartedAt).TotalSeconds);
                    await UploadMediaAsync("voice", path, seconds);
                }
                finally { try { File.Delete(path); } catch { } }
            };
            _voiceRecorder.StartRecording();
            UpdateVoiceButtonState(true);
        }
        catch (Exception ex)
        {
            StopVoiceRecording();
            MessageBox.Show($"Could not start voice recording.\n\n{ex.Message}", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void StopVoiceRecording()
    {
        try { _voiceRecorder?.StopRecording(); } catch { _voiceRecorder?.Dispose(); _voiceRecorder = null; }
        UpdateVoiceButtonState(false);
    }

    private void UpdateVoiceButtonState(bool recording)
    {
        var button = FindDescendant<Button>(this, b => b.Tag is string s && s == "NovaChat.Voice");
        if (button == null) return;
        button.Content = recording ? "⏹" : "🎙";
        button.ToolTip = recording ? "Stop and send voice message" : "Voice message";
        button.Foreground = recording ? Brushes.IndianRed : (Brush)FindResource("PrimaryBrush");
    }

    private async Task UploadMediaAsync(string type, string path, double? durationSeconds = null)
    {
        if (!_currentChatId.HasValue) return;
        try
        {
            var endpoint = $"api/ChatMedia/{_currentChatId.Value}?type={Uri.EscapeDataString(type)}";
            if (durationSeconds.HasValue) endpoint += $"&durationSeconds={durationSeconds.Value:F2}";
            var result = await _apiService.UploadFileAsync<MediaUploadResponse>(endpoint, path);
            if (result?.Data == null)
            {
                MessageBox.Show(result?.Message ?? "Media upload failed.", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_loadedMessageIds.Add(result.Data.Id)) AddMessageToUi(result.Data);
            UpdateChatPreview(result.Data);
            await ScrollMessagesToBottomAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Media could not be sent.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void MediaBubbleLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not Border border || !IsMediaMessageBorder(border)) return;
        if (border.Tag is string s && s == "media-rendered") return;
        var text = border.Child is StackPanel p ? p.Children.OfType<TextBlock>().FirstOrDefault()?.Text : null;
        if (string.IsNullOrWhiteSpace(text)) return;
        var match = Regex.Match(text, @"\u200B(\d+)$");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var messageId)) return;
        border.Tag = "media-rendered";
        if (FindAncestorMainView(border) is MainView view) _ = view.RenderMediaBubbleAsync(border, messageId);
    }

    private static bool IsMediaMessageBorder(Border border) => border.Child is StackPanel panel && panel.Children.OfType<TextBlock>().Any(t => t.Text.Contains("\u200B", StringComparison.Ordinal));

    private async Task RenderMediaBubbleAsync(Border border, int messageId)
    {
        var existing = border.Child as StackPanel;
        var label = existing?.Children.OfType<TextBlock>().FirstOrDefault()?.Text ?? string.Empty;
        if (label.StartsWith("🎙", StringComparison.Ordinal)) ReplaceWithVoiceBubble(border, messageId);
        else if (label.StartsWith("📷", StringComparison.Ordinal)) await ReplaceWithImageBubbleAsync(border, messageId, label);
        else ReplaceWithFileBubble(border, messageId, label);
    }

    private async Task ReplaceWithImageBubbleAsync(Border border, int messageId, string label)
    {
        var bytes = await _apiService.GetBytesAsync($"api/ChatMedia/{messageId}");
        if (bytes == null) return;
        var image = new BitmapImage();
        using var ms = new MemoryStream(bytes);
        image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = ms; image.EndInit(); image.Freeze();
        var stack = NewMediaStack();
        stack.Children.Add(new Image { Source = image, Width = 320, MaxHeight = 320, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 6) });
        stack.Children.Add(new TextBlock { Text = CleanMediaLabel(label), Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap });
        AddMediaTime(stack, border);
        border.Child = stack;
    }

    private void ReplaceWithFileBubble(Border border, int messageId, string label)
    {
        var stack = NewMediaStack();
        var button = new Button { Content = $"{CleanMediaLabel(label)}\nSave / Open file", Padding = new Thickness(12), Background = Brushes.Transparent, Foreground = Brushes.White, BorderBrush = Brushes.White };
        button.Click += async (_, _) => await DownloadMediaAsync(messageId, CleanMediaLabel(label));
        stack.Children.Add(button); AddMediaTime(stack, border); border.Child = stack;
    }

    private void ReplaceWithVoiceBubble(Border border, int messageId)
    {
        var stack = NewMediaStack();
        var button = new Button { Content = "▶  Play voice message", Padding = new Thickness(12, 8, 12, 8), Background = Brushes.Transparent, Foreground = Brushes.White, BorderBrush = Brushes.White };
        button.Click += async (_, _) => await PlayVoiceAsync(messageId, button);
        stack.Children.Add(button); AddMediaTime(stack, border); border.Child = stack;
    }

    private static StackPanel NewMediaStack() => new() { Orientation = Orientation.Vertical, MaxWidth = 430 };

    private static void AddMediaTime(StackPanel stack, Border border)
    {
        if (border.Child is StackPanel old)
        {
            var time = old.Children.OfType<TextBlock>().Skip(1).FirstOrDefault()?.Text;
            if (!string.IsNullOrWhiteSpace(time)) stack.Children.Add(new TextBlock { Text = time, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Right, Foreground = Brushes.White, Margin = new Thickness(0, 5, 0, 0) });
        }
    }

    private static string CleanMediaLabel(string label) => Regex.Replace(label, @"\u200B\d+$", string.Empty).Trim();

    private async Task DownloadMediaAsync(int messageId, string suggestedName)
    {
        var bytes = await _apiService.GetBytesAsync($"api/ChatMedia/{messageId}");
        if (bytes == null) return;
        var dialog = new SaveFileDialog { FileName = suggestedName.Replace("📎 ", string.Empty), Title = "Save file" };
        if (dialog.ShowDialog() != true) return;
        await File.WriteAllBytesAsync(dialog.FileName, bytes);
        Process.Start(new ProcessStartInfo(dialog.FileName) { UseShellExecute = true });
    }

    private async Task PlayVoiceAsync(int messageId, Button button)
    {
        try
        {
            if (_voicePlayers.TryGetValue(messageId, out var existing)) { existing.Stop(); existing.Play(); button.Content = "▶  Replay voice"; return; }
            var bytes = await _apiService.GetBytesAsync($"api/ChatMedia/{messageId}");
            if (bytes == null) return;
            var path = Path.Combine(Path.GetTempPath(), $"NovaChatPlay_{messageId}.wav");
            await File.WriteAllBytesAsync(path, bytes);
            var player = new SoundPlayer(path); player.Load(); player.Play(); _voicePlayers[messageId] = player;
            button.Content = "▶  Replay voice";
        }
        catch (Exception ex) { MessageBox.Show($"Voice could not be played.\n\n{ex.Message}", "NovaChat", MessageBoxButton.OK, MessageBoxImage.Warning); }
    }

    private static MainView? FindAncestorMainView(DependencyObject element)
    {
        var current = VisualTreeHelper.GetParent(element);
        while (current != null) { if (current is MainView view) return view; current = VisualTreeHelper.GetParent(current); }
        return null;
    }

    private static T? FindDescendant<T>(DependencyObject root, Func<T, bool> predicate) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T typed && predicate(typed)) return typed;
            var nested = FindDescendant(child, predicate);
            if (nested != null) return nested;
        }
        return null;
    }

    private sealed class MediaUploadResponse
    {
        public string Message { get; set; } = string.Empty;
        public MessageModel? Data { get; set; }
    }
}
