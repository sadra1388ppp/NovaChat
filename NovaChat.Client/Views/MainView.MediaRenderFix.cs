using NovaChat.Client.Models;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

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
                await RenderE2eeImageBubbleAsync(border, message);
                return;
            }

            await RenderMediaBubbleAsync(border, message.Id);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Media bubble rendering failed for message {message.Id}: {ex}");
        }
    }

    private async Task RenderE2eeImageBubbleAsync(Border border, MessageModel message)
    {
        var bytes = await _apiService.GetE2eeMediaBytesAsync(message.Id, _e2ee);
        if (bytes.Length == 0)
            throw new InvalidDataException($"The encrypted image blob for message {message.Id} is empty after decryption.");

        var bitmap = new BitmapImage();
        using var stream = new MemoryStream(bytes, writable: false);
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();

        var mine = string.Equals(message.SenderId, AuthState.Username, StringComparison.OrdinalIgnoreCase);
        var fileName = string.IsNullOrWhiteSpace(message.FileName)
            ? $"image_{message.Id}.jpg"
            : message.FileName.Trim();

        var stack = new StackPanel();
        var imageCard = new Border
        {
            CornerRadius = new CornerRadius(10),
            Clip = new RectangleGeometry(new Rect(0, 0, 320, 320), 10, 10)
        };

        var image = new Image
        {
            Source = bitmap,
            Width = 320,
            MaxHeight = 320,
            Stretch = Stretch.Uniform,
            Cursor = System.Windows.Input.Cursors.Hand
        };
        image.MouseLeftButtonUp += async (_, _) => await SaveImageCopyAsync(message.Id, fileName);
        imageCard.Child = image;
        stack.Children.Add(imageCard);

        stack.Children.Add(new TextBlock
        {
            Text = $"📷 {fileName}",
            Foreground = mine ? Brushes.White : (Brush)FindResource("TextBrush"),
            FontSize = 12,
            Margin = new Thickness(2, 7, 2, 0),
            TextWrapping = TextWrapping.Wrap
        });

        border.Child = stack;
    }
}
