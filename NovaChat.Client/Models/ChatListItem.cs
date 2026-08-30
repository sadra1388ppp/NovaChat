using NovaChat.Client.Services;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NovaChat.Client.Models;

public sealed class ChatListItem : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private string _lastMessage = string.Empty;
    private bool _isOnline;
    private string? _avatarUri;
    private object? _avatarSource;

    public ChatModel Chat { get; set; } = new();

    public string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value) return;
            _displayName = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Initials));
        }
    }

    public string LastMessage
    {
        get => _lastMessage;
        set
        {
            if (_lastMessage == value) return;
            _lastMessage = value;
            OnPropertyChanged();
        }
    }

    public bool IsOnline
    {
        get => _isOnline;
        set
        {
            if (_isOnline == value) return;
            _isOnline = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(OnlineVisibility));
            OnPropertyChanged(nameof(ProfileStatusText));
        }
    }

    public string? AvatarUri
    {
        get => _avatarUri;
        set
        {
            if (string.Equals(_avatarUri, value, StringComparison.Ordinal)) return;
            _avatarUri = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasAvatar));
        }
    }

    // Compatibility property used by older MainView initialization code.
    public object? AvatarSource
    {
        get => _avatarSource;
        set
        {
            if (ReferenceEquals(_avatarSource, value)) return;
            _avatarSource = value;
            OnPropertyChanged();
        }
    }

    public bool HasAvatar => !string.IsNullOrWhiteSpace(AvatarUri);
    public string OtherUserId => Chat.OtherUserId(AuthState.UserId);

    public string Initials
    {
        get
        {
            var value = string.IsNullOrWhiteSpace(DisplayName) ? "?" : DisplayName.Trim();
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length >= 2
                ? $"{parts[0][0]}{parts[1][0]}".ToUpperInvariant()
                : value[..Math.Min(2, value.Length)].ToUpperInvariant();
        }
    }

    public string ProfileStatusText => IsOnline ? "Online" : "Offline";
    public System.Windows.Visibility OnlineVisibility => IsOnline ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
