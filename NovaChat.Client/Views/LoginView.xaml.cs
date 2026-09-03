using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views
{
    public partial class LoginView : UserControl
    {
        private readonly ApiService _apiService;
        private readonly DispatcherTimer _physicsTimer;

        private bool _lampOn;
        private bool _draggingCord;
        private double _pullOffset;
        private double _pullVelocity;
        private double _lastMouseY;
        private double _lampAngle;
        private double _lampAngularVelocity;
        private DateTime _lastPhysicsTime;

        private const double RestPullY = 350;
        private const double MaxPullOffset = 145;
        private const double ToggleThreshold = 72;
        private const double SpringStrength = 22.0;
        private const double Damping = 7.5;

        public event Action? CreateAccountRequested;
        public event Action? LoginSuccessful;
        public event Action? OwnerLoginSuccessful;

        public LoginView()
        {
            InitializeComponent();
            _apiService = new ApiService();

            _physicsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _physicsTimer.Tick += PhysicsTimer_Tick;

            Loaded += LoginView_Loaded;
        }

        private void LoginView_Loaded(object sender, RoutedEventArgs e)
        {
            // Always begin in the dark/off state.
            _lampOn = false;
            _pullOffset = 0;
            _pullVelocity = 0;
            _lampAngle = 0;
            _lampAngularVelocity = 0;

            ApplyLampVisuals(false, false);
            UpdateCordVisual();
        }

        private void LampPull_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _draggingCord = true;
            _lastMouseY = e.GetPosition(LampCanvas).Y;
            _pullVelocity = 0;
            _physicsTimer.Stop();
            LampPull.CaptureMouse();
            e.Handled = true;
        }

        private void LampCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_draggingCord || e.LeftButton != MouseButtonState.Pressed)
                return;

            Point position = e.GetPosition(LampCanvas);
            double delta = position.Y - _lastMouseY;
            _lastMouseY = position.Y;

            // Only downward movement stretches the pull cord.
            _pullOffset = Math.Clamp(_pullOffset + delta, 0, MaxPullOffset);
            _pullVelocity = delta * 8.0;

            // A real pull also gives the hanging lamp a small reactive sway.
            _lampAngularVelocity += delta * 0.018;
            _lampAngularVelocity = Math.Clamp(_lampAngularVelocity, -2.8, 2.8);

            UpdateCordVisual();
            ApplyLampTransform();
        }

        private void LampCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_draggingCord)
                return;

            _draggingCord = false;
            LampPull.ReleaseMouseCapture();

            if (_pullOffset >= ToggleThreshold)
                ToggleLamp();

            // Let the cord return naturally instead of snapping back.
            _lastPhysicsTime = DateTime.UtcNow;
            _physicsTimer.Start();
            e.Handled = true;
        }

        private void PhysicsTimer_Tick(object? sender, EventArgs e)
        {
            double dt = (DateTime.UtcNow - _lastPhysicsTime).TotalSeconds;
            _lastPhysicsTime = DateTime.UtcNow;
            dt = Math.Clamp(dt, 0.008, 0.035);

            // Damped spring: x'' = -kx - cv.
            double acceleration = (-SpringStrength * _pullOffset) - (Damping * _pullVelocity);
            _pullVelocity += acceleration * dt;
            _pullOffset += _pullVelocity * dt;

            if (_pullOffset < 0)
            {
                _pullOffset = 0;
                _pullVelocity *= -0.22;
            }

            // The lamp keeps a subtle sway while the cord settles.
            _lampAngularVelocity += (-14.0 * _lampAngle - 5.5 * _lampAngularVelocity) * dt;
            _lampAngle += _lampAngularVelocity * dt;

            UpdateCordVisual();
            ApplyLampTransform();

            if (Math.Abs(_pullOffset) < 0.15 && Math.Abs(_pullVelocity) < 0.15 &&
                Math.Abs(_lampAngle) < 0.08 && Math.Abs(_lampAngularVelocity) < 0.08)
            {
                _pullOffset = 0;
                _pullVelocity = 0;
                _lampAngle = 0;
                _lampAngularVelocity = 0;
                UpdateCordVisual();
                ApplyLampTransform();
                _physicsTimer.Stop();
            }
        }

        private void UpdateCordVisual()
        {
            const double topX = 250;
            const double topY = 220;
            double handleY = RestPullY + _pullOffset;

            // A curved Bezier keeps the cord visibly soft while it stretches.
            double sag = Math.Min(28, Math.Abs(_pullVelocity) * 0.9 + _pullOffset * 0.055);
            double direction = _pullVelocity >= 0 ? 1 : -1;
            double control1X = topX + direction * sag;
            double control2X = topX - direction * sag * 0.65;
            double control1Y = topY + (handleY - topY) * 0.28;
            double control2Y = topY + (handleY - topY) * 0.74;

            PullCord.Data = new PathGeometry
            {
                Figures = new PathFigureCollection
                {
                    new PathFigure
                    {
                        StartPoint = new Point(topX, topY),
                        IsClosed = false,
                        Segments = new PathSegmentCollection
                        {
                            new BezierSegment(
                                new Point(control1X, control1Y),
                                new Point(control2X, control2Y),
                                new Point(topX, handleY),
                                true)
                        }
                    }
                }
            };

            Canvas.SetTop(LampPull, handleY - LampPull.Height / 2);
            Canvas.SetLeft(LampPull, topX - LampPull.Width / 2);

            PullHint.Text = _lampOn ? "pull to turn off" : "pull to turn on";
            PullHint.Opacity = _pullOffset > 18 ? 0.35 : 0.75;
        }

        private void ToggleLamp()
        {
            _lampOn = !_lampOn;
            ApplyLampVisuals(_lampOn, true);
        }

        private void ApplyLampVisuals(bool on, bool animate)
        {
            Color bulbColor = on ? Color.FromRgb(255, 241, 154) : Color.FromRgb(69, 71, 81);
            Color shadeColor = on ? Color.FromRgb(76, 67, 39) : Color.FromRgb(36, 38, 48);
            Color cordColor = on ? Color.FromRgb(238, 220, 153) : Color.FromRgb(183, 169, 133);
            Color pullColor = on ? Color.FromRgb(255, 226, 122) : Color.FromRgb(185, 169, 111);

            SetAnimatedColor(LampBulb, Shape.FillProperty, bulbColor, animate);
            SetAnimatedColor(LampShadeGlow, Shape.FillProperty, shadeColor, animate);
            SetAnimatedColor(PullCord, Shape.StrokeProperty, cordColor, animate);
            SetAnimatedColor(LampPull, Shape.FillProperty, pullColor, animate);

            double targetGlow = on ? 1.0 : 0.0;
            double targetWarm = on ? 0.82 : 0.0;

            AnimateOpacity(LampGlow, targetGlow, 420, animate);
            AnimateOpacity(WarmLightOverlay, targetWarm, 650, animate);

            WelcomeText.Foreground = new SolidColorBrush(
                on ? Color.FromRgb(255, 248, 218) : Color.FromRgb(233, 233, 237));
            WelcomeSubText.Foreground = new SolidColorBrush(
                on ? Color.FromRgb(214, 196, 133) : Color.FromRgb(110, 112, 122));
            PullHint.Foreground = new SolidColorBrush(
                on ? Color.FromRgb(221, 196, 111) : Color.FromRgb(119, 121, 131));
        }

        private static void SetAnimatedColor(Shape shape, DependencyProperty property, Color color, bool animate)
        {
            if (shape.GetValue(property) is SolidColorBrush current)
            {
                if (animate)
                {
                    var animation = new ColorAnimation
                    {
                        To = color,
                        Duration = TimeSpan.FromMilliseconds(280),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                    };
                    current.BeginAnimation(SolidColorBrush.ColorProperty, animation);
                }
                else
                {
                    current.BeginAnimation(SolidColorBrush.ColorProperty, null);
                    current.Color = color;
                }
            }
            else
            {
                shape.SetValue(property, new SolidColorBrush(color));
            }
        }

        private static void AnimateOpacity(UIElement element, double target, int milliseconds, bool animate)
        {
            if (!animate)
            {
                element.BeginAnimation(UIElement.OpacityProperty, null);
                element.Opacity = target;
                return;
            }

            var animation = new DoubleAnimation
            {
                To = target,
                Duration = TimeSpan.FromMilliseconds(milliseconds),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            element.BeginAnimation(UIElement.OpacityProperty, animation);
        }

        private void ApplyLampTransform()
        {
            LampRotateTransform.Angle = _lampAngle;
        }

        private async void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            string userId = UserIdTextBox.Text.Trim();
            string password = PasswordBox.Password;

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(password))
            {
                MessageBox.Show(
                    "Please enter your User ID and Password.",
                    "Login",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            try
            {
                LoginButton.IsEnabled = false;

                var request = new LoginRequest
                {
                    Id = userId,
                    Password = password
                };

                var result = await _apiService.PostAsync<LoginRequest, LoginResponse>(
                    "api/User/login",
                    request);

                if (result == null || string.IsNullOrWhiteSpace(result.Token))
                {
                    MessageBox.Show(
                        "Invalid User ID or Password.",
                        "Login Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                AuthState.Set(
                    result.Token,
                    result.User.Id,
                    result.User.DisplayName,
                    result.User.Email);

                const string ownerId = "BlackRoom";

                if (string.Equals(result.User.Id, ownerId, StringComparison.OrdinalIgnoreCase))
                    OwnerLoginSuccessful?.Invoke();
                else
                    LoginSuccessful?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Could not connect to the server.\n\n{ex.Message}",
                    "Connection Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                LoginButton.IsEnabled = true;
            }
        }

        private void CreateAccountButton_Click(object sender, RoutedEventArgs e)
        {
            CreateAccountRequested?.Invoke();
        }
    }
}