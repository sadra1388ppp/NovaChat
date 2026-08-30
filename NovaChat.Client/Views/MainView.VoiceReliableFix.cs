using NAudio.Wave;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static bool _reliableVoiceFixRegistered;

    private WasapiCapture? _reliableVoiceCapture;
    private WaveFileWriter? _reliableVoiceWriter;
    private TaskCompletionSource<bool>? _reliableVoiceStopped;
    private string? _reliableVoicePath;
    private DateTime _reliableVoiceStartedAt;
    private bool _reliableVoiceFinishing;

    internal static void RegisterReliableVoiceFix()
    {
        if (_reliableVoiceFixRegistered) return;
        _reliableVoiceFixRegistered = true;

        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(ReliableVoiceButtonPreview),
            true);
    }

    private static async void ReliableVoiceButtonPreview(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Button button) return;
        if (FindReliableMainView(button) is not MainView view) return;

        if (string.Equals(button.Tag?.ToString(), "NovaChat.Voice", StringComparison.Ordinal))
        {
            e.Handled = true;
            await view.ToggleReliableVoiceAsync();
            return;
        }

        if (string.Equals(button.Content?.ToString(), "➤", StringComparison.Ordinal) && view._reliableVoiceCapture != null)
        {
            e.Handled = true;
            await view.FinishReliableVoiceAsync();
        }
    }

    private static MainView? FindReliableMainView(DependencyObject element)
    {
        var current = element;
        while (current != null)
        {
            if (current is MainView view) return view;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    private async Task ToggleReliableVoiceAsync()
    {
        if (!_currentChatId.HasValue || !AuthState.IsAuthenticated) return;
        if (_reliableVoiceCapture != null)
        {
            await FinishReliableVoiceAsync();
            return;
        }

        try
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"NovaChatVoice_{Guid.NewGuid():N}.wav");

            var capture = new WasapiCapture();
            var writer = new WaveFileWriter(path, capture.WaveFormat);
            var stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            _reliableVoicePath = path;
            _reliableVoiceStartedAt = DateTime.UtcNow;
            _reliableVoiceCapture = capture;
            _reliableVoiceWriter = writer;
            _reliableVoiceStopped = stopped;
            _reliableVoiceFinishing = false;

            capture.DataAvailable += (_, args) =>
            {
                try
                {
                    lock (this)
                    {
                        _reliableVoiceWriter?.Write(args.Buffer, 0, args.BytesRecorded);
                    }
                }
                catch { }
            };

            capture.RecordingStopped += (_, _) => stopped.TrySetResult(true);

            capture.StartRecording();
            UpdateReliableVoiceButton(true);
        }
        catch (Exception ex)
        {
            CleanupReliableVoice(deleteFile: true);
            MessageBox.Show(
                $"Could not start voice recording.\n\n{ex.Message}",
                "Voice Message",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task FinishReliableVoiceAsync()
    {
        if (_reliableVoiceFinishing) return;
        if (_reliableVoiceCapture == null) return;
        _reliableVoiceFinishing = true;

        var capture = _reliableVoiceCapture;
        var stopped = _reliableVoiceStopped;

        try
        {
            try { capture.StopRecording(); }
            catch { }

            if (stopped != null)
            {
                try { await stopped.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
                catch { }
            }

            await Task.Delay(75);

            var path = _reliableVoicePath;
            var writer = _reliableVoiceWriter;

            lock (this)
            {
                _reliableVoiceWriter = null;
                _reliableVoiceCapture = null;
                _reliableVoiceStopped = null;
                _reliableVoicePath = null;
            }

            try { writer?.Flush(); } catch { }
            try { writer?.Dispose(); } catch { }
            try { capture.Dispose(); } catch { }

            if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            {
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("The voice recording could not be created.", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            var info = new System.IO.FileInfo(path);
            if (info.Length <= 1000)
            {
                try { System.IO.File.Delete(path); } catch { }
                await Dispatcher.InvokeAsync(() =>
                    MessageBox.Show("The recording is empty. Please check Windows microphone permissions and try again.", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Warning));
                return;
            }

            var duration = Math.Max(0.1, (DateTime.UtcNow - _reliableVoiceStartedAt).TotalSeconds);
            await UploadMediaAsync("voice", path, duration);

            try { System.IO.File.Delete(path); } catch { }
        }
        catch (Exception ex)
        {
            await Dispatcher.InvokeAsync(() =>
                MessageBox.Show($"Voice could not be sent.\n\n{ex.Message}", "Voice Message", MessageBoxButton.OK, MessageBoxImage.Error));
        }
        finally
        {
            _reliableVoiceFinishing = false;
            await Dispatcher.InvokeAsync(() => UpdateReliableVoiceButton(false));
            if (!string.IsNullOrWhiteSpace(_reliableVoicePath))
            {
                try { System.IO.File.Delete(_reliableVoicePath); } catch { }
                _reliableVoicePath = null;
            }
        }
    }

    private void UpdateReliableVoiceButton(bool recording)
    {
        var button = FindDescendant<Button>(this, b =>
            string.Equals(b.Tag?.ToString(), "NovaChat.Voice", StringComparison.Ordinal));
        if (button == null) return;

        button.Content = recording ? "⏹" : "🎙";
        button.ToolTip = recording ? "Stop and send voice message" : "Voice message";
        button.Foreground = recording ? Brushes.IndianRed : (Brush)FindResource("PrimaryBrush");
    }

    private void CleanupReliableVoice(bool deleteFile)
    {
        try { _reliableVoiceCapture?.StopRecording(); } catch { }
        try { _reliableVoiceCapture?.Dispose(); } catch { }
        try { _reliableVoiceWriter?.Dispose(); } catch { }
        _reliableVoiceCapture = null;
        _reliableVoiceWriter = null;
        _reliableVoiceStopped = null;

        if (deleteFile && !string.IsNullOrWhiteSpace(_reliableVoicePath))
        {
            try { System.IO.File.Delete(_reliableVoicePath); } catch { }
            _reliableVoicePath = null;
        }
    }
}
