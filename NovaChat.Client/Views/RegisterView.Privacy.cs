using System.Windows;

namespace NovaChat.Client.Views;

public partial class RegisterView
{
    private string GetRegistrationMessagePrivacy()
    {
        return RegistrationMessageEverybodyRadio?.IsChecked == true
            ? "Everybody"
            : "Requests";
    }

    private bool GetRegistrationAllowGroupAdds() => RegistrationAllowGroupAddsBox?.IsChecked != false;

    private void RegistrationMessagePrivacyChanged(object sender, RoutedEventArgs e)
    {
        if (RegistrationMessageEverybodyRadio?.IsChecked == true)
        {
            RegistrationMessagePrivacyDescription.Text = "Anyone can start a conversation with you directly.";
            RegistrationMessagePrivacyStatus.Text = "Everyone can message you";
        }
        else if (RegistrationMessageRequestsRadio?.IsChecked == true)
        {
            RegistrationMessagePrivacyDescription.Text = "New conversations arrive as requests until you approve them.";
            RegistrationMessagePrivacyStatus.Text = "New conversations require approval";
        }
    }
}
