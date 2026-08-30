using System.Windows;
using System.Windows.Threading;

namespace NovaChat.Client.Views;

public partial class ProfileView
{
    private DispatcherTimer? _avatarBindingTimer;
    private string? _lastBoundAvatarUrl;
    private string? _lastBoundProfileId;

    static ProfileView()
    {
        EventManager.RegisterClassHandler(
            typeof(ProfileView),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnAvatarBindingLoaded));

        EventManager.RegisterClassHandler(
            typeof(ProfileView),
            FrameworkElement.UnloadedEvent,
            new RoutedEventHandler(OnAvatarBindingUnloaded));
    }

    private static void OnAvatarBindingLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ProfileView view)
            view.StartAvatarBindingFix();
    }

    private static void OnAvatarBindingUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is ProfileView view)
            view.StopAvatarBindingFix();
    }

    private void StartAvatarBindingFix()
    {
        if (_avatarBindingTimer != null) return;

        _avatarBindingTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(200)
        };
        _avatarBindingTimer.Tick += (_, _) => ApplyStableAvatarBinding();
        _avatarBindingTimer.Start();
        ApplyStableAvatarBinding();
    }

    private void StopAvatarBindingFix()
    {
        if (_avatarBindingTimer == null) return;
        _avatarBindingTimer.Stop();
        _avatarBindingTimer = null;
    }

    private void ApplyStableAvatarBinding()
    {
        if (!IsLoaded || _profile == null) return;

        var avatarUrl = _profile.AvatarUrl;
        var profileId = _profile.Id;

        if (string.Equals(_lastBoundAvatarUrl, avatarUrl, StringComparison.Ordinal) &&
            string.Equals(_lastBoundProfileId, profileId, StringComparison.Ordinal))
            return;

        _lastBoundAvatarUrl = avatarUrl;
        _lastBoundProfileId = profileId;
        DataContext = _profile;
        AvatarImage.Visibility = Visibility.Visible;

        if (string.IsNullOrWhiteSpace(avatarUrl))
        {
            AvatarImage.Source = null;
            AvatarInitialsText.Visibility = Visibility.Visible;
        }
        else
        {
            AvatarInitialsText.Visibility = Visibility.Collapsed;
        }
    }
}
