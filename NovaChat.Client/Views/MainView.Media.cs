using Microsoft.Win32;
using NAudio.Wave;
using NovaChat.Client.Models;
using NovaChat.Client.Services;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IOPath = System.IO.Path;

namespace NovaChat.Client.Views;

public partial class MainView
{
    // Existing implementation retained; media upload duration formatting is culture-invariant.
    private string BuildMediaEndpoint(int chatId, string type, double? durationSeconds)
    {
        var endpoint = $"api/ChatMedia/{chatId}?type={Uri.EscapeDataString(type)}";
        if (durationSeconds.HasValue)
            endpoint += $"&durationSeconds={durationSeconds.Value.ToString("F2", CultureInfo.InvariantCulture)}";
        return endpoint;
    }
}
