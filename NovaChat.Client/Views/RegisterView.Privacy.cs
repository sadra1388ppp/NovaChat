using System;
using System.Windows.Controls;

namespace NovaChat.Client.Views;

public partial class RegisterView
{
    private string GetRegistrationMessagePrivacy()
    {
        var selected = RegistrationMessagePrivacyBox?.SelectedItem;
        var value = selected is ComboBoxItem item
            ? item.Content?.ToString()
            : selected?.ToString();

        return string.Equals(value, "Requests only", StringComparison.OrdinalIgnoreCase)
            ? "Requests"
            : "Everybody";
    }

    private bool GetRegistrationAllowGroupAdds() => RegistrationAllowGroupAddsBox?.IsChecked != false;
}
