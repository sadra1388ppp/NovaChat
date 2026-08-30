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
            Button.PreviewMouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler(VoiceAwareButtonPreviewMouseDown),
            true);
    }

    private static async void VoiceAwareButtonPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Button button) return;
        if (!string.Equals(button.Content?.ToString(), "➤", StringComparison.Ordinal)) return;
        if (FindMainViewFromButton(button) is not MainView view) return;
        if (view._voiceRecorder == null) return;

        e.Handled = true;
        button.IsEnabled = false;
        try
        {
            await view.ForceStopAndSendVoiceAsync();
        }
        finally
        {
            button.IsEnabled = true;
        }
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
