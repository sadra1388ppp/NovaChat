using System.Globalization;
using System.Windows;

namespace NovaChat.Client.Views;

public partial class ProfileView
{
    static ProfileView()
    {
        EventManager.RegisterClassHandler(
            typeof(ProfileView),
            FrameworkElement.DataContextChangedEvent,
            new DependencyPropertyChangedEventHandler(OnProfileDataContextChanged));
    }

    private static void OnProfileDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ProfileView view || view.JoinedText == null || view.DataContext is not Models.ProfileModel profile)
            return;

        view.JoinedText.Text = $"Joined {profile.CreatedAt.ToLocalTime().ToString("dd MMM yyyy", CultureInfo.InvariantCulture)}";
    }
}
