using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace NovaChat.Client.Models;

public sealed class ChatListItem : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private string _lastMessage = string.Empty;
    private bool _isOnline;
    private BitmapImage? _avatarSource;

    public ChatModel Chat { get; set; } = new();

    public string DisplayName
    {
        get => _displayName;
        set { if (_displayName == value) return; _displayName = value; OnPropertyChanged(); OnPropertyChanged(nameof(Initials)); }
    }

    public string LastMessage
    {
        get => _lastMessage;
        set { if (_lastMessage == value) return; _lastMessage = value; OnPropertyChanged(); }
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

    public BitmapImage? AvatarSource
    {
        get => _avatarSource;
        set { if (ReferenceEquals(_avatarSource, value)) return; _avatarSource = value; OnPropertyChanged(); }
    }

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
    public Visibility OnlineVisibility => IsOnline ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
