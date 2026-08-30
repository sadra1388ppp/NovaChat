using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

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

        if (textBlock.DataContext is MainView view)
        {
            view.ChatUserNameText_Click(textBlock, e);
            e.Handled = true;
        }
    }
}
