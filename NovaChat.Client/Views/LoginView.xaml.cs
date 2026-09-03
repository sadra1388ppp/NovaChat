using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NovaChat.Client.Models;
using NovaChat.Client.Services;

namespace NovaChat.Client.Views
{
    public partial class LoginView : UserControl
    {
        private readonly ApiService _apiService;
        private readonly Stopwatch _physicsClock = new();

        private PathGeometry? _cordGeometry;
        private BezierSegment? _cordSegment;
        private TranslateTransform? _pullTranslate;

        private bool _lampOn;
        private bool _draggingCord;
        private bool _physicsRunning;

        private double _pullX;
        private double _pullY;
        private double _pullVelocityX;
        private double _pullVelocityY;
        private Point _lastMousePosition;

        private double _lampAngle;
        private double _lampAngularVelocity;

        // Cord is intentionally moved farther to the right of the lamp stem.
        // The pull bead uses the same center coordinate so the cord and bead never separate.
        private const double CordTopX = 330;
        private const double CordTopY = 239;
        private const double RestPullY = 350;
        private const double MaxPullDistance = 175;
        private const double ToggleThreshold = 72;

        private const double SpringStrength = 20.5;
        private const double Damping = 6.8;
        private const double MaxSpeed = 850.0;

        public event Action? CreateAccountRequested;
        public event Action? LoginSuccessful;
        public event Action? OwnerLoginSuccessful;

        public LoginView()
        {
            InitializeComponent();
            _apiService = new ApiService();

            Loaded += LoginView_Loaded;
            Unloaded += LoginView_Unloaded;
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

            EnsureCordGeometry();
            EnsurePullTransform();

            ApplyLampVisuals(false, false);
            UpdateCordVisual();
            ApplyLampTransform();
        }

        private void LoginView_Unloaded(object sender, RoutedEventArgs e)
        {
            StopPhysics();
        }

        private void EnsureCordGeometry()
        {
            if (_cordGeometry != null && _cordSegment != null)
                return;

            _cordSegment = new BezierSegment
            {
                IsStroked = true
            };

            var figure = new PathFigure
            {
                StartPoint = new Point(CordTopX, CordTopY),
                IsClosed = false,
                IsFilled = false
            };
            figure.Segments.Add(_cordSegment);

            _cordGeometry = new PathGeometry();
            _cordGeometry.Figures.Add(figure);
            PullCord.Data = _cordGeometry;
        }

        private void EnsurePullTransform()
        {
            if (_pullTranslate != null)
                return;

            _pullTranslate = new TranslateTransform();
            LampPull.RenderTransform = _pullTranslate;
            LampPull.RenderTransformOrigin = new Point(0.5, 0.5);
        }

        private void LampPull_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _draggingCord = true;
            _lastMousePosition = e.GetPosition(LampCanvas);
            _pullVelocityX = 0;
            _pullVelocityY = 0;

            StartPhysics();
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

            _pullVelocityX = Math.Clamp(_pullVelocityX * 0.55 + deltaX * 12.0, -MaxSpeed, MaxSpeed);
            _pullVelocityY = Math.Clamp(_pullVelocityY * 0.55 + deltaY * 12.0, -MaxSpeed, MaxSpeed);

            _pullX = nextX;
            _pullY = nextY;

            _lampAngularVelocity += deltaX * 0.010 - deltaY * 0.002;
            _lampAngularVelocity = Math.Clamp(_lampAngularVelocity, -2.2, 2.2);

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

            StartPhysics();
        }

        private void StartPhysics()
        {
            if (_physicsRunning)
                return;

            _physicsRunning = true;
            _physicsClock.Restart();
            CompositionTarget.Rendering += PhysicsRendering;
        }

        private void StopPhysics()
        {
            if (!_physicsRunning)
                return;

            CompositionTarget.Rendering -= PhysicsRendering;
            _physicsRunning = false;
            _physicsClock.Stop();
        }

        private void PhysicsRendering(object? sender, EventArgs e)
        {
            double dt = _physicsClock.Elapsed.TotalSeconds;
            _physicsClock.Restart();
            dt = Math.Clamp(dt, 0.001, 0.032);

            if (!_draggingCord)
            {
                double accelerationX = (-SpringStrength * _pullX) - (Damping * _pullVelocityX);
                double accelerationY = (-SpringStrength * _pullY) - (Damping * _pullVelocityY);

                _pullVelocityX += accelerationX * dt;
                _pullVelocityY += accelerationY * dt;

                double velocityLength = Math.Sqrt(
                    _pullVelocityX * _pullVelocityX +
                    _pullVelocityY * _pullVelocityY);

                if (velocityLength > MaxSpeed)
                {
                    double scale = MaxSpeed / velocityLength;
                    _pullVelocityX *= scale;
                    _pullVelocityY *= scale;
                }

                _pullX += _pullVelocityX * dt;
                _pullY += _pullVelocityY * dt;

                double distance = Math.Sqrt(_pullX * _pullX + _pullY * _pullY);
                if (distance > MaxPullDistance)
                {
                    double scale = MaxPullDistance / distance;
                    _pullX *= scale;
                    _pullY *= scale;
                }

                _lampAngularVelocity += (-11.5 * _lampAngle - 4.2 * _lampAngularVelocity) * dt;
                _lampAngle += _lampAngularVelocity * dt;
            }

            UpdateCordVisual();
            ApplyLampTransform();

            if (!_draggingCord &&
                Math.Abs(_pullX) < 0.08 &&
                Math.Abs(_pullY) < 0.08 &&
                Math.Abs(_pullVelocityX) < 0.08 &&
                Math.Abs(_pullVelocityY) < 0.08 &&
                Math.Abs(_lampAngle) < 0.025 &&
                Math.Abs(_lampAngularVelocity) < 0.025)
            {
                _pullX = 0;
                _pullY = 0;
                _pullVelocityX = 0;
                _pullVelocityY = 0;
                _lampAngle = 0;
                _lampAngularVelocity = 0;

                UpdateCordVisual();
                ApplyLampTransform();
                StopPhysics();
            }
        }

        private void UpdateCordVisual()
        {
            EnsureCordGeometry();
            EnsurePullTransform();

            double endX = CordTopX + _pullX;
            double endY = RestPullY + _pullY;
            double length = Math.Max(1, endY - CordTopY);
            double horizontal = endX - CordTopX;

            double sway = Math.Clamp(horizontal * 0.30 + _pullVelocityX * 0.025, -62, 62);

            _cordSegment!.Point1 = new Point(
                CordTopX + sway * 0.10,
                CordTopY + length * 0.28);

            _cordSegment.Point2 = new Point(
                CordTopX + sway,
                CordTopY + length * 0.73);

            _cordSegment.Point3 = new Point(endX, endY);

            _pullTranslate!.X = _pullX;
            _pullTranslate.Y = _pullY;
        }

        private void ToggleLamp()
        {
            _lampOn = !_lampOn;
            ApplyLampVisuals(_lampOn, true);
        }

        private void ApplyLampVisuals(bool on, bool animate)
        {
            Color bulbColor = on ? Color.FromRgb(255, 255, 255) : Color.FromRgb(245, 240, 230);
            Color shadeColor = on ? Color.FromRgb(255, 255, 255) : Color.FromRgb(245, 240, 230);
            Color reflectorColor = on ? Color.FromRgb(255, 219, 138) : Color.FromRgb(45, 47, 53);
            Color cordColor = on ? Color.FromRgb(85, 85, 85) : Color.FromRgb(85, 85, 85);
            Color pullColor = on ? Color.FromRgb(212, 163, 115) : Color.FromRgb(212, 163, 115);

            SetAnimatedColor(LampBulb, Shape.FillProperty, bulbColor, animate);
            SetAnimatedColor(LampShadeGlow, Shape.StrokeProperty, shadeColor, animate);
            SetAnimatedColor(LampReflector, Shape.FillProperty, reflectorColor, animate);
            SetAnimatedColor(PullCord, Shape.StrokeProperty, cordColor, animate);
            SetAnimatedColor(LampPull, Shape.FillProperty, pullColor, animate);

            AnimateOpacity(LampGlow, on ? 1.0 : 0.0, 480, animate);
            AnimateOpacity(LampBulbGlow, on ? 0.65 : 0.02, 320, animate);
            AnimateOpacity(LampReflector, on ? 0.55 : 0.0, 320, animate);
            AnimateOpacity(WarmLightOverlay, on ? 0.78 : 0.0, 720, animate);

            AnimateOpacity(LoginCard, on ? 1.0 : 0.0, 700, animate);
            LoginCard.IsHitTestVisible = on;

            AnimateCardColor(LoginCard, Border.BackgroundProperty,
                on ? Color.FromRgb(28, 31, 36) : Color.FromRgb(13, 15, 19), 420, animate);
            AnimateCardColor(LoginCard, Border.BorderBrushProperty,
                on ? Color.FromArgb(35, 255, 255, 255) : Color.FromArgb(26, 255, 255, 255), 420, animate);

            LoginCardShadow.Color = on ? Color.FromRgb(255, 214, 110) : Color.FromRgb(0, 0, 0);
            LoginCardShadow.Opacity = on ? 0.35 : 0.30;

            SignInTitle.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            SignInSubtitle.Foreground = new SolidColorBrush(on
                ? Color.FromRgb(153, 153, 153)
                : Color.FromRgb(119, 119, 119));
            WelcomeText.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
            WelcomeSubText.Foreground = new SolidColorBrush(Color.FromRgb(141, 146, 153));
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