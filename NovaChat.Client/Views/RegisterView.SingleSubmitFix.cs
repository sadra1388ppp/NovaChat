using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views;

public partial class RegisterView
{
    private static int _singleSubmitHookRegistered;

    internal static void RegisterSingleSubmitFix()
    {
        if (Interlocked.Exchange(ref _singleSubmitHookRegistered, 1) != 0) return;

        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.PreviewMouseLeftButtonDownEvent,
            new System.Windows.Input.MouseButtonEventHandler(OnRegisterButtonPreviewMouseDown),
            true);
    }

    private static void OnRegisterButtonPreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Button button || !string.Equals(button.Name, "RegisterButton", StringComparison.Ordinal)) return;
        if (FindRegisterView(button) is not RegisterView view) return;
        if (view.RegisterButton.IsEnabled == false) { e.Handled = true; return; }

        // Let the normal Click handler execute exactly once, while immediately
        // disabling the button before WPF can queue another click.
        view.RegisterButton.IsEnabled = false;
        button.Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Input,
            new Action(() => view.RegisterButton.IsEnabled = true));
    }

    private static RegisterView? FindRegisterView(DependencyObject element)
    {
        var current = System.Windows.Media.VisualTreeHelper.GetParent(element);
        while (current != null)
        {
            if (current is RegisterView view) return view;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
