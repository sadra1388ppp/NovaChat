using System.Windows;
using NovaChat.Client.Views;

namespace NovaChat.Client
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            LoadLightTheme();

            ShowMain();
        }

        public void ShowLogin()
        {
            MainContainer.Children.Clear();

            LoginView loginView = new LoginView();

            loginView.CreateAccountRequested += ShowRegister;

            MainContainer.Children.Add(loginView);
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

            MainContainer.Children.Add(mainView);
        }

        public void ShowProfile()
        {
            MainContainer.Children.Clear();

            ProfileView profileView = new ProfileView();

            profileView.BackToChatRequested += ShowMain;

            MainContainer.Children.Add(profileView);
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
                    Source = new System.Uri(
                        "Resources/LightTheme.xaml",
                        System.UriKind.Relative)
                });
        }
    }
}