using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NovaChat.Client.Views
{
    public partial class LoginView
    {
        // Runs after the existing Loaded handler so the subtitle is finally
        // re-parented to the full login root instead of the lamp canvas.
        private readonly bool _subtitleFixRegistered = RegisterSubtitleFix();

        private bool RegisterSubtitleFix()
        {
            Loaded += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (WelcomeSubText.Parent is Panel oldParent)
                        oldParent.Children.Remove(WelcomeSubText);

                    if (!LoginRoot.Children.Contains(WelcomeSubText))
                        LoginRoot.Children.Add(WelcomeSubText);

                    WelcomeSubText.HorizontalAlignment = HorizontalAlignment.Center;
                    WelcomeSubText.VerticalAlignment = VerticalAlignment.Bottom;
                    WelcomeSubText.Margin = new Thickness(0, 0, 0, 18);
                }), DispatcherPriority.Loaded);
            };

            return true;
        }
    }
}
