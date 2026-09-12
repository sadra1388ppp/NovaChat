using NovaChat.Client.Models;
using System.Windows;
using System.Windows.Controls;

namespace NovaChat.Client.Views;

public partial class MainView
{
    private async Task RenderMediaBubbleFromMessageAsync(Border border, MessageModel message)
    {
        if (message.Id <= 0) return;

        try
        {
            if (string.Equals(message.MessageType, "image", StringComparison.OrdinalIgnoreCase))
            {
                var fileName = string.IsNullOrWhiteSpace(message.FileName)
                    ? $"image_{message.Id}.jpg"
                    : message.FileName.Trim();
                var label = $"📷 {fileName}\u200B{message.Id}";
                await ReplaceWithImageBubbleAsync(border, message.Id, label);
                return;
            }

            await RenderMediaBubbleAsync(border, message.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Media bubble rendering failed for message {message.Id}: {ex}");
        }
    }
}
