using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using NovaChat.Client.Services;
using NovaChat.Client.Views;

namespace NovaChat.Client
{
    public partial class MainWindow : Window
    {
        private MainView? _mainView;
        private readonly ApiService _apiService = new();

        public MainWindow()
        {
            InitializeComponent();
            LoadLightTheme();
            ShowLogin();
        }

        private void LoadLightTheme()
        {
            try
            {
                Resources.MergedDictionaries.Clear();
                Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("Themes/LightTheme.xaml", UriKind.Relative)
                });
            }
            catch
            {
                // Keep the default WPF resources if the theme cannot be loaded.
            }
        }

        private void ShowLogin()
        {
            MainContainer.Children.Clear();
            NotificationService.Dispose();
            _mainView = null;

            var login = new LoginView(_apiService)
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch
            };
            login.LoginSucceeded += ShowMain;
            MainContainer.Children.Add(login);
        }

        private void ShowMain()
        {
            MainContainer.Children.Clear();

            if (_mainView == null)
            {
                _mainView = new MainView();
                _mainView.ProfileRequested += ShowProfile;
                _mainView.SettingsRequested += ShowSettings;
            }

            MainContainer.Children.Add(_mainView);
            _mainView.SetOwnerMode(false);
            _mainView.ActivateMainContent();
            NotificationService.Initialize(this);
        }

        private void ShowProfile()
        {
            MainContainer.Children.Clear();
            var profile = new ProfileView();
            profile.BackRequested += ShowMain;
            MainContainer.Children.Add(profile);
        }

        private void ShowSettings()
        {
            MainContainer.Children.Clear();
            var settings = new SettingsView();
            settings.BackRequested += ShowMain;
            MainContainer.Children.Add(settings);
        }

        protected override void OnClosed(EventArgs e)
        {
            NotificationService.Dispose();
            base.OnClosed(e);
        }
    }

    public static class MessageBox
    {
        private static readonly object ToastLock = new();
        private static readonly List<NovaToastWindow> OpenToasts = [];

        public static System.Windows.MessageBoxResult Show(string messageBoxText) => Show(null, messageBoxText, "NovaChat", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information, System.Windows.MessageBoxResult.OK);
        public static System.Windows.MessageBoxResult Show(string messageBoxText, string caption) => Show(null, messageBoxText, caption, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information, System.Windows.MessageBoxResult.OK);
        public static System.Windows.MessageBoxResult Show(string messageBoxText, string caption, System.Windows.MessageBoxButton button) => Show(null, messageBoxText, caption, button, System.Windows.MessageBoxImage.None, GetDefault(button));
        public static System.Windows.MessageBoxResult Show(string messageBoxText, string caption, System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage icon) => Show(null, messageBoxText, caption, button, icon, GetDefault(button));
        public static System.Windows.MessageBoxResult Show(string messageBoxText, string caption, System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage icon, System.Windows.MessageBoxResult defaultResult) => Show(null, messageBoxText, caption, button, icon, defaultResult);
        public static System.Windows.MessageBoxResult Show(Window owner, string messageBoxText, string caption, System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage icon) => Show(owner, messageBoxText, caption, button, icon, GetDefault(button));

        public static System.Windows.MessageBoxResult Show(Window? owner, string messageBoxText, string caption, System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage icon, System.Windows.MessageBoxResult defaultResult)
        {
            if (button == System.Windows.MessageBoxButton.OK)
            {
                ShowToast(owner, caption, messageBoxText, icon);
                return System.Windows.MessageBoxResult.OK;
            }

            return ShowConfirmation(owner, caption, messageBoxText, button, icon, defaultResult);
        }

        private static void ShowToast(Window? owner, string caption, string message, System.Windows.MessageBoxImage icon)
        {
            if (Application.Current?.Dispatcher == null) return;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var toast = new NovaToastWindow(owner, caption, message, icon);
                lock (ToastLock)
                {
                    OpenToasts.Add(toast);
                    toast.Closed += (_, _) =>
                    {
                        lock (ToastLock)
                        {
                            OpenToasts.Remove(toast);
                            RepositionToasts();
                        }
                    };
                    toast.SetSlot(OpenToasts.Count - 1);
                }
                toast.Show();
            });
        }

        private static void RepositionToasts()
        {
            for (var i = 0; i < OpenToasts.Count; i++) OpenToasts[i].SetSlot(i);
        }

        private static System.Windows.MessageBoxResult ShowConfirmation(Window? owner, string caption, string message, System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage icon, System.Windows.MessageBoxResult defaultResult)
        {
            System.Windows.MessageBoxResult result = defaultResult;
            var dialog = new NovaConfirmWindow(owner, caption, message, button, icon, defaultResult);
            dialog.ResultSelected += selected => result = selected;
            dialog.ShowDialog();
            return result;
        }

        private static System.Windows.MessageBoxResult GetDefault(System.Windows.MessageBoxButton button) => button switch
        {
            System.Windows.MessageBoxButton.YesNo => System.Windows.MessageBoxResult.No,
            System.Windows.MessageBoxButton.OKCancel => System.Windows.MessageBoxResult.Cancel,
            System.Windows.MessageBoxButton.YesNoCancel => System.Windows.MessageBoxResult.Cancel,
            _ => System.Windows.MessageBoxResult.OK
        };
    }

    internal sealed class NovaToastWindow : Window
    {
        private readonly Window? _owner;
        private readonly DispatcherTimer _timer;
        private readonly TranslateTransform _translate = new(40, 0);
        private int _slot;
        private bool _isClosing;

        public NovaToastWindow(Window? owner, string title, string message, System.Windows.MessageBoxImage icon)
        {
            _owner = owner ?? Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive) ?? Application.Current?.MainWindow;
            if (_owner != null) Owner = _owner;
            Width = 380;
            SizeToContent = SizeToContent.Height;
            MaxHeight = 170;
            MinHeight = 92;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            ShowActivated = false;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = false;
            Opacity = 0;

            var accentKey = icon switch
            {
                System.Windows.MessageBoxImage.Error => "DangerBrush",
                System.Windows.MessageBoxImage.Warning => "WarningBrush",
                System.Windows.MessageBoxImage.Information => "InfoBrush",
                _ => "PrimaryBrush"
            };

            var panel = new Border
            {
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 12, 14),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Opacity = 0.22 },
                RenderTransform = _translate,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            panel.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
            panel.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border { Width = 40, Height = 40, CornerRadius = new CornerRadius(14), VerticalAlignment = VerticalAlignment.Top };
            badge.SetResourceReference(Border.BackgroundProperty, "PrimarySoftBrush");
            var badgeText = new TextBlock { Text = IconText(icon), FontSize = 20, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badgeText.SetResourceReference(TextBlock.ForegroundProperty, accentKey);
            badge.Child = badgeText;
            root.Children.Add(badge);

            var text = new StackPanel { Margin = new Thickness(12, 0, 10, 0) };
            var titleText = new TextBlock { Text = string.IsNullOrWhiteSpace(title) ? "NovaChat" : title, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var messageText = new TextBlock { Text = message ?? string.Empty, FontSize = 12, Margin = new Thickness(0, 4, 0, 0), TextWrapping = TextWrapping.Wrap, MaxHeight = 88 };
            messageText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            text.Children.Add(titleText);
            text.Children.Add(messageText);
            Grid.SetColumn(text, 1);
            root.Children.Add(text);

            var close = new Button { Content = "×", Width = 30, Height = 30, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 20, Padding = new Thickness(0), VerticalAlignment = VerticalAlignment.Top, Cursor = System.Windows.Input.Cursors.Hand };
            close.SetResourceReference(Control.ForegroundProperty, "SecondaryTextBrush");
            close.Click += (_, _) => CloseWithAnimation();
            Grid.SetColumn(close, 2);
            root.Children.Add(close);

            var accent = new Border { Width = 4, CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 1), IsHitTestVisible = false };
            accent.SetResourceReference(Border.BackgroundProperty, accentKey);
            Grid.SetColumn(accent, 0);
            root.Children.Add(accent);

            panel.Child = root;
            Content = panel;

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(icon == System.Windows.MessageBoxImage.Error ? 5.5 : icon == System.Windows.MessageBoxImage.Warning ? 5 : 3.8) };
            _timer.Tick += (_, _) => CloseWithAnimation();
            Closed += (_, _) => _timer.Stop();
            Loaded += (_, _) =>
            {
                PositionWindow();
                _timer.Start();
                var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                BeginAnimation(OpacityProperty, fade);
                var slide = new DoubleAnimation(0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
                _translate.BeginAnimation(TranslateTransform.XProperty, slide);
            };
        }

        public void SetSlot(int slot)
        {
            _slot = Math.Max(0, slot);
            if (IsLoaded) PositionWindow();
        }

        private void PositionWindow()
        {
            var owner = _owner ?? Application.Current?.MainWindow;
            if (owner == null || !owner.IsVisible) return;

            UpdateLayout();
            var workingArea = SystemParameters.WorkArea;
            Left = Math.Max(workingArea.Left, owner.Left + owner.ActualWidth - ActualWidth - 22);
            Top = Math.Max(workingArea.Top, owner.Top + 22 + _slot * (ActualHeight + 12));
        }

        private void CloseWithAnimation()
        {
            if (_isClosing) return;
            _isClosing = true;
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
            var slide = new DoubleAnimation(40, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            _translate.BeginAnimation(TranslateTransform.XProperty, slide);
        }

        private static string IconText(System.Windows.MessageBoxImage icon) => icon switch
        {
            System.Windows.MessageBoxImage.Error => "!",
            System.Windows.MessageBoxImage.Warning => "⚠",
            System.Windows.MessageBoxImage.Information => "i",
            System.Windows.MessageBoxImage.Question => "?",
            _ => "•"
        };
    }

    internal sealed class NovaConfirmWindow : Window
    {
        public event Action<System.Windows.MessageBoxResult>? ResultSelected;

        public NovaConfirmWindow(Window? owner, string title, string message, System.Windows.MessageBoxButton button, System.Windows.MessageBoxImage icon, System.Windows.MessageBoxResult defaultResult)
        {
            if (owner != null) Owner = owner;
            Title = title;
            Width = 420;
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Background = Brushes.Transparent;
            AllowsTransparency = true;

            var panel = new Border
            {
                CornerRadius = new CornerRadius(18),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(22),
                Margin = new Thickness(12),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 24, ShadowDepth = 8, Opacity = 0.22 }
            };
            panel.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
            panel.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");

            var stack = new StackPanel();
            var titleText = new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold };
            titleText.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
            var messageText = new TextBlock { Text = message, FontSize = 13, Margin = new Thickness(0, 10, 0, 18), TextWrapping = TextWrapping.Wrap };
            messageText.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
            stack.Children.Add(titleText);
            stack.Children.Add(messageText);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            foreach (var item in GetButtons(button))
            {
                var b = new Button { Content = item.text, MinWidth = 82, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 8, 14, 8), IsDefault = item.result == defaultResult };
                b.Click += (_, _) =>
                {
                    ResultSelected?.Invoke(item.result);
                    DialogResult = true;
                    Close();
                };
                buttons.Children.Add(b);
            }
            stack.Children.Add(buttons);
            panel.Child = stack;
            Content = panel;
        }

        private static IEnumerable<(string text, System.Windows.MessageBoxResult result)> GetButtons(System.Windows.MessageBoxButton button) => button switch
        {
            System.Windows.MessageBoxButton.YesNo => [("No", System.Windows.MessageBoxResult.No), ("Yes", System.Windows.MessageBoxResult.Yes)],
            System.Windows.MessageBoxButton.OKCancel => [("Cancel", System.Windows.MessageBoxResult.Cancel), ("OK", System.Windows.MessageBoxResult.OK)],
            System.Windows.MessageBoxButton.YesNoCancel => [("Cancel", System.Windows.MessageBoxResult.Cancel), ("No", System.Windows.MessageBoxResult.No), ("Yes", System.Windows.MessageBoxResult.Yes)],
            _ => [("OK", System.Windows.MessageBoxResult.OK)]
        };
    }
}
