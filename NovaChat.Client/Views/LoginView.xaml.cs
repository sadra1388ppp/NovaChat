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

        private double _pullX;
        private double _pullY;
        private double _pullVelocityX;
        private double _pullVelocityY;
        private Point _lastMousePosition;

        private double _lampAngle;
        private double _lampAngularVelocity;
        private DateTime _lastPhysicsTime;

        private const double CordTopX = 250;
        private const double CordTopY = 239;
        private const double RestPullY = 350;
        private const double MaxPullDistance = 175;
        private const double ToggleThreshold = 72;
        private const double SpringStrength = 23.0;
        private const double Damping = 7.2;

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
            _lampOn = false;
            _draggingCord = false;
            _pullX = 0;
            _pullY = 0;
            _pullVelocityX = 0;
            _pullVelocityY = 0;
            _lampAngle = 0;
            _lampAngularVelocity = 0;

            ApplyLampVisuals(false, false);
            UpdateCordVisual();
            ApplyLampTransform();
        }

        private void LampPull_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _draggingCord = true;
            _lastMousePosition = e.GetPosition(LampCanvas);
            _pullVelocityX = 0;
            _pullVelocityY = 0;
            _physicsTimer.Stop();

            LampPull.CaptureMouse();
            e.Handled = true;
        }

        private void LampCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_draggingCord || e.LeftButton != MouseButtonState.Pressed)
                return;

            Point position = e.GetPosition(LampCanvas);
            double deltaX = position.X - _lastMousePosition.X;
            double deltaY = position.Y - _lastMousePosition.Y;
            _lastMousePosition = position;

            double nextX = _pullX + deltaX;
            double nextY = _pullY + deltaY;

            double distance = Math.Sqrt(nextX * nextX + nextY * nextY);
            if (distance > MaxPullDistance)
            {
                double scale = MaxPullDistance / distance;
                nextX *= scale;
                nextY *= scale;
            }

            _pullVelocityX = deltaX * 8.0;
            _pullVelocityY = deltaY * 8.0;
            _pullX = nextX;
            _pullY = nextY;

            _lampAngularVelocity += (deltaX * 0.018) - (deltaY * 0.004);
            _lampAngularVelocity = Math.Clamp(_lampAngularVelocity, -3.0, 3.0);

            UpdateCordVisual();
            ApplyLampTransform();
        }

        private void LampCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            ReleaseCord();
            e.Handled = true;
        }

        private void ReleaseCord()
        {
            if (!_draggingCord)
                return;

            _draggingCord = false;
            LampPull.ReleaseMouseCapture();

            double distance = Math.Sqrt(_pullX * _pullX + _pullY * _pullY);
            if (distance >= ToggleThreshold)
                ToggleLamp();

            _lastPhysicsTime = DateTime.UtcNow;
            _physicsTimer.Start();
        }

        private void PhysicsTimer_Tick(object? sender, EventArgs e)
        {
            double dt = (DateTime.UtcNow - _lastPhysicsTime).TotalSeconds;
            _lastPhysicsTime = DateTime.UtcNow;
            dt = Math.Clamp(dt, 0.008, 0.035);

            double accelerationX = (-SpringStrength * _pullX) - (Damping * _pullVelocityX);
            double accelerationY = (-SpringStrength * _pullY) - (Damping * _pullVelocityY);

            _pullVelocityX += accelerationX * dt;
            _pullVelocityY += accelerationY * dt;
            _pullX += _pullVelocityX * dt;
            _pullY += _pullVelocityY * dt;

            _lampAngularVelocity += (-13.5 * _lampAngle - 5.0 * _lampAngularVelocity) * dt;
            _lampAngle += _lampAngularVelocity * dt;

            UpdateCordVisual();
            ApplyLampTransform();

            if (Math.Abs(_pullX) < 0.15 &&
                Math.Abs(_pullY) < 0.15 &&
                Math.Abs(_pullVelocityX) < 0.15 &&
                Math.Abs(_pullVelocityY) < 0.15 &&
                Math.Abs(_lampAngle) < 0.08 &&
                Math.Abs(_lampAngularVelocity) < 0.08)
            {
                _pullX = 0;
                _pullY = 0;
                _pullVelocityX = 0;
                _pullVelocityY = 0;
                _lampAngle = 0;
                _lampAngularVelocity = 0;

                UpdateCordVisual();
                ApplyLampTransform();
                _physicsTimer.Stop();
            }
        }

        private void UpdateCordVisual()
        {
            double endX = CordTopX + _pullX;
            double endY = RestPullY + _pullY;

            double length = Math.Max(1, endY - CordTopY);
            double horizontal = endX - CordTopX;
            double sway = Math.Clamp(horizontal * 0.34 + _pullVelocityX * 0.5, -55, 55);

            double control1X = CordTopX + sway * 0.18;
            double control2X = CordTopX + sway;
            double control1Y = CordTopY + length * 0.27;
            double control2Y = CordTopY + length * 0.72;

            PullCord.Data = new PathGeometry
            {
                Figures = new PathFigureCollection
                {
                    new PathFigure
                    {
                        StartPoint = new Point(CordTopX, CordTopY),
                        IsClosed = false,
                        Segments = new PathSegmentCollection
                        {
                            new BezierSegment(
                                new Point(control1X, control1Y),
                                new Point(control2X, control2Y),
                                new Point(endX, endY),
                                true)
                        }
                    }
                }
            };

            Canvas.SetLeft(LampPull, endX - LampPull.Width / 2);
            Canvas.SetTop(LampPull, endY - LampPull.Height / 2);
        }

        private void ToggleLamp()
        {
            _lampOn = !_lampOn;
            ApplyLampVisuals(_lampOn, true);
        }

        private void ApplyLampVisuals(bool on, bool animate)
        {
            Color bulbColor = on ? Color.FromRgb(255, 241, 154) : Color.FromRgb(69, 71, 81);
            Color shadeColor = on ? Color.FromRgb(78, 69, 41) : Color.FromRgb(36, 38, 48);
            Color reflectorColor = on ? Color.FromRgb(105, 91, 50) : Color.FromRgb(40, 42, 49);
            Color cordColor = on ? Color.FromRgb(238, 220, 153) : Color.FromRgb(174, 160, 124);
            Color pullColor = on ? Color.FromRgb(255, 226, 122) : Color.FromRgb(177, 160, 102);

            SetAnimatedColor(LampBulb, Shape.FillProperty, bulbColor, animate);
            SetAnimatedColor(LampShadeGlow, Shape.FillProperty, shadeColor, animate);
            SetAnimatedColor(LampReflector, Shape.FillProperty, reflectorColor, animate);
            SetAnimatedColor(PullCord, Shape.StrokeProperty, cordColor, animate);
            SetAnimatedColor(LampPull, Shape.FillProperty, pullColor, animate);

            AnimateOpacity(LampGlow, on ? 1.0 : 0.0, 480, animate);
            AnimateOpacity(LampBulbGlow, on ? 0.92 : 0.04, 320, animate);
            AnimateOpacity(WarmLightOverlay, on ? 0.78 : 0.0, 720, animate);

            AnimateCardColor(LoginCard, Border.BackgroundProperty,
                on ? Color.FromRgb(31, 28, 20) : Color.FromRgb(18, 20, 27), 420, animate);
            AnimateCardColor(LoginCard, Border.BorderBrushProperty,
                on ? Color.FromRgb(113, 91, 35) : Color.FromRgb(41, 44, 54), 420, animate);

            LoginCardShadow.Color = on ? Color.FromRgb(218, 170, 54) : Color.FromRgb(0, 0, 0);
            LoginCardShadow.Opacity = on ? 0.48 : 0.35;

            SignInTitle.Foreground = new SolidColorBrush(
                on ? Color.FromRgb(255, 248, 220) : Color.FromRgb(233, 233, 237));
            SignInSubtitle.Foreground = new SolidColorBrush(
                on ? Color.FromRgb(188, 168, 111) : Color.FromRgb(119, 121, 131));
            WelcomeText.Foreground = new SolidColorBrush(
                on ? Color.FromRgb(255, 248, 218) : Color.FromRgb(233, 233, 237));
            WelcomeSubText.Foreground = new SolidColorBrush(
                on ? Color.FromRgb(214, 196, 133) : Color.FromRgb(110, 112, 122));
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

        private static void AnimateCardColor(Border border, DependencyProperty property, Color color, int milliseconds, bool animate)
        {
            if (border.GetValue(property) is SolidColorBrush current)
            {
                if (animate)
                {
                    var animation = new ColorAnimation
                    {
                        To = color,
                        Duration = TimeSpan.FromMilliseconds(milliseconds),
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
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
                border.SetValue(property, new SolidColorBrush(color));
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