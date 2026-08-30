using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static bool _voiceSendFixRegistered;

    internal static void RegisterVoiceSendFix()
    {
        if (_voiceSendFixRegistered) return;
        _voiceSendFixRegistered = true;
        EventManager.RegisterClassHandler(typeof(MainView), FrameworkElement.LoadedEvent, new RoutedEventHandler(OnVoiceSendFixLoaded));
    }

    private static void OnVoiceSendFixLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainView view) return;
        view.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, new Action(view.AttachVoiceSendButton));
    }

    private void AttachVoiceSendButton()
    {
        var send = FindDescendant<Button>(this, b => string.Equals(b.Content?.ToString(), "➤", StringComparison.Ordinal));
        if (send == null) return;
        if (send.Tag is string tag && tag == "NovaChat.VoiceAwareSend") return;

        send.Tag = "NovaChat.VoiceAwareSend";
        send.PreviewMouseLeftButtonDown += VoiceAwareSendButton_PreviewMouseLeftButtonDown;
    }

    private void VoiceAwareSendButton_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_voiceRecorder == null) return;
        e.Handled = true;
        StopVoiceRecording();
    }
}
