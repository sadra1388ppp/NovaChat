using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;

namespace NovaChat.Client.Models;

public sealed class ChatListItem : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private string _lastMessage = string.Empty;
    private bool _isOnline;

    public ChatModel Chat { get; set; } = new();

    public string DisplayName
    {
        get => _displayName;
        set { if (_displayName == value) return; _displayName = value; OnPropertyChanged(); }
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
        }
    }

    public Visibility OnlineVisibility => IsOnline ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
