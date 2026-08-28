using System.Windows;
using NovaChat.Client.Views;

namespace NovaChat.Client
{
    public partial class MainWindow : Window
    {
        private bool _isOwner;
        private string? _pendingChatUserId;

        public MainWindow()
        {
            InitializeComponent();
            LoadLightTheme();
            ShowLogin();
        }

        public void ShowLogin()
        {
            _isOwner = false;
            _pendingChatUserId = null;
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
            MainView mainView = new MainView();
            mainView.ProfileRequested += ShowProfile;
            mainView.SettingsRequested += ShowSettings;
            mainView.SetOwnerMode(_isOwner);
            MainContainer.Children.Add(mainView);
            if (!string.IsNullOrWhiteSpace(_pendingChatUserId))
            {
                var userId = _pendingChatUserId;
                _pendingChatUserId = null;
                mainView.Loaded += async (_, _) => await mainView.OpenChatWithUserIdAsync(userId);
            }
        }

        public void ShowProfile()
        {
            MainContainer.Children.Clear();
            ProfileView profileView = new ProfileView();
            profileView.BackToChatRequested += ShowMain;
            profileView.ContactsRequested += ShowContacts;
            MainContainer.Children.Add(profileView);
        }

        public void ShowContacts()
        {
            MainContainer.Children.Clear();
            ContactsView contactsView = new ContactsView();
            contactsView.BackToChatRequested += ShowMain;
            contactsView.ChatRequested += userId =>
            {
                _pendingChatUserId = userId;
                ShowMain();
            };
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
            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary
                {
                    Source = new System.Uri("Resources/LightTheme.xaml", System.UriKind.Relative)
                });
        }
    }
}