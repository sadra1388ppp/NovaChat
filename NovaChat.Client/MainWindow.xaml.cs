using System.Windows;
using NovaChat.Client.Services;
using NovaChat.Client.Views;

namespace NovaChat.Client
{
    public partial class MainWindow : Window
    {
        private bool _isOwner;
        private string? _pendingChatUsername;
        private MainView? _mainView;

        public MainWindow()
        {
            InitializeComponent();
            LoadLightTheme();
            ShowLogin();
        }

        public void ShowLogin()
        {
            _isOwner = false;
            _pendingChatUsername = null;

            NotificationService.Dispose();

            if (_mainView != null)
            {
                MainContainer.Children.Remove(_mainView);
                _mainView = null;
            }

            MainContainer.Children.Clear();

            LoginView loginView = new LoginView();
            loginView.CreateAccountRequested += ShowRegister;
            loginView.LoginSuccessful += HandleNormalUserLogin;
            loginView.OwnerLoginSuccessful += HandleOwnerLogin;
            MainContainer.Children.Add(loginView);
        }

        private void HandleNormalUserLogin()
        {
            _isOwner = false;
            ShowMain();
        }

        private void HandleOwnerLogin()
        {
            _isOwner = true;
            ShowMain();
        }

        public void ShowRegister()
        {
            MainContainer.Children.Clear();
            RegisterView registerView = new RegisterView();
            registerView.BackToLoginRequested += ShowLogin;
            MainContainer.Children.Add(registerView);
        }

        public void ShowMain()
        {
            MainContainer.Children.Clear();

            if (_mainView == null)
            {
                _mainView = new MainView();
                _mainView.ProfileRequested += ShowProfile;
                _mainView.SettingsRequested += ShowSettings;
            }

            _mainView.SetOwnerMode(_isOwner);
            MainContainer.Children.Add(_mainView);

            if (!string.IsNullOrWhiteSpace(_pendingChatUsername))
            {
                var username = _pendingChatUsername;
                _pendingChatUsername = null;
                _mainView.Loaded += OpenPendingChatOnce;

                async void OpenPendingChatOnce(object? sender, RoutedEventArgs e)
                {
                    _mainView!.Loaded -= OpenPendingChatOnce;
                    await _mainView.OpenChatWithUsernameAsync(username);
                }
            }
        }

        public void ShowManageUsers()
        {
            if (!_isOwner) return;

            MainContainer.Children.Clear();
            ManageUsersView manageUsersView = new ManageUsersView();
            manageUsersView.BackToChatRequested += ShowMain;
            MainContainer.Children.Add(manageUsersView);
        }

        public void ShowProfile()
        {
            MainContainer.Children.Clear();
            ProfileView profileView = new ProfileView();
            profileView.BackToChatRequested += ShowMain;
            profileView.ContactsRequested += ShowContacts;
            profileView.SessionExpired += ShowLogin;
            MainContainer.Children.Add(profileView);
        }

        public void ShowContacts()
        {
            MainContainer.Children.Clear();
            ContactsView contactsView = new ContactsView();
            contactsView.BackToChatRequested += ShowMain;
            contactsView.ChatRequested += username => { _pendingChatUsername = username; ShowMain(); };
            MainContainer.Children.Add(contactsView);
        }

        public void ShowSettings()
        {
            MainContainer.Children.Clear();
            SettingsView settingsView = new SettingsView();
            settingsView.BackToChatRequested += ShowMain;
            MainContainer.Children.Add(settingsView);
        }

        private void LoadLightTheme()
        {
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new System.Uri("Resources/LightTheme.xaml", System.UriKind.Relative) });
        }
    }
}