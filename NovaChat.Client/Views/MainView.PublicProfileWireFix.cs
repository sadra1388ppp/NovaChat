using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NovaChat.Client.Views;

public partial class MainView
{
    static MainView()
    {
        EventManager.RegisterClassHandler(
            typeof(TextBlock),
            UIElement.MouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnChatUserNameTextClassClick));
    }

    private static void OnChatUserNameTextClassClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TextBlock textBlock || textBlock.Name != "ChatUserNameText")
            return;

        DependencyObject? current = textBlock;
        while (current != null)
        {
            if (current is MainView view)
            {
                view.ChatUserNameText_Click(textBlock, e);
                e.Handled = true;
                return;
            }

            current = VisualTreeHelper.GetParent(current);
        }
    }
}
