using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private static bool _voiceSendFixRegistered;

    internal static void RegisterVoiceSendFix()
    {
        if (_voiceSendFixRegistered) return;
        _voiceSendFixRegistered = true;

        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(VoiceAwareButtonClick),
            true);

        EventManager.RegisterClassHandler(
            typeof(MainView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(VoiceSendFixLoaded));
    }

    private static void VoiceSendFixLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainView view) return;

        view.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new Action(view.AttachVoiceAwareSendButton));
    }

    private void AttachVoiceAwareSendButton()
    {
        var send = FindDescendant<Button>(
            this,
            b => string.Equals(b.Content?.ToString(), "➤", StringComparison.Ordinal));

        if (send == null) return;
        send.Tag = "NovaChat.VoiceAwareSend";
    }

    private static void VoiceAwareButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (!string.Equals(button.Tag?.ToString(), "NovaChat.VoiceAwareSend", StringComparison.Ordinal)) return;
        if (FindMainViewFromButton(button) is not MainView view) return;
        if (view._voiceRecorder == null) return;

        e.Handled = true;
        view.StopVoiceRecording();
    }

    private static MainView? FindMainViewFromButton(DependencyObject element)
    {
        var current = System.Windows.Media.VisualTreeHelper.GetParent(element);
        while (current != null)
        {
            if (current is MainView view) return view;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }
}
