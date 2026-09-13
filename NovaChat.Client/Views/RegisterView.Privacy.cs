using System;

namespace NovaChat.Client.Views;

public partial class RegisterView
{
    private string GetRegistrationMessagePrivacy() =>
        string.Equals(RegistrationMessagePrivacyBox?.SelectedItem?.ToString(), "Requests only", StringComparison.OrdinalIgnoreCase)
            ? "Requests"
            : "Everybody";

    private bool GetRegistrationAllowGroupAdds() => RegistrationAllowGroupAddsBox?.IsChecked != false;
}
