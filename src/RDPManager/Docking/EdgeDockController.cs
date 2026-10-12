using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using RDPManager.Models;
using Rectangle = System.Drawing.Rectangle;
using Screen = System.Windows.Forms.Screen;

namespace RDPManager.Docking;

/// <summary>
/// Shows a topmost panel when the cursor rests on the configured screen edge and hides it again
/// when the cursor leaves. Cursor polling works even while a full-screen mstsc window has focus.
/// </summary>
public sealed class EdgeDockController : IDisposable
{
    private const int EdgeHideMargin = 12;
    private static readonly Duration AnimationDuration = TimeSpan.FromMilliseconds(170);

    private readonly Window _window;
    private readonly FrameworkElement _panel;
    private readonly TranslateTransform _slide;
    private readonly Func<AppSettings> _settings;
    private readonly DispatcherTimer _timer;
    private IntPtr _hwnd;
    private Rectangle _panelRect;
    private double _scale = 1;
    private DateTime? _edgeSince;
    private DateTime? _outsideSince;
    private bool _animating;

    public EdgeDockController(Window window, FrameworkElement panel, TranslateTransform slide, Func<AppSettings> settings)
    {
        _window = window;
        _panel = panel;
        _slide = slide;
        _settings = settings;
        _timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(50) };
        _timer.Tick += (_, _) => Tick();
    }

    /// <summary>Panel stays open regardless of the cursor.</summary>
    public bool IsPinned { get; set; }

    public bool IsShown { get; private set; }

    /// <summary>Shown by hotkey/tray: stays open until the cursor enters it once or the window loses focus.</summary>
    public bool AwaitingMouseEnter { get; private set; }

    /// <summary>Returns true while auto-hide must not happen (dialogs, context menus).</summary>
    public Func<bool> IsBusy { get; set; } = () => false;

    public event EventHandler? Shown;

    public void Start()
    {
        _hwnd = new WindowInteropHelper(_window).EnsureHandle();
        var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE,
            (exStyle | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW);
        _window.DpiChanged += (_, _) => ApplyBounds();
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        _timer.Start();
    }

    public static Screen[] Screens => Screen.AllScreens;

    public void Show(bool activate)
    {
        _edgeSince = null;
        _outsideSince = null;
        AwaitingMouseEnter = activate;
        if (IsShown && !_animating)
        {
            if (activate)
                Activate();
            return;
        }

        IsShown = true;
        UpdateLayoutForEdge();
        if (!_window.IsVisible)
            _window.Show();
        ApplyBounds();
        // WPF may resize the window after a DPI change of the target monitor; re-apply afterwards.
        _window.Dispatcher.BeginInvoke(ApplyBounds, DispatcherPriority.Loaded);

        Animate(fromOffset: OffscreenOffset(), toOffset: 0, fromOpacity: 0, toOpacity: 1, onCompleted: null);
        if (activate)
            Activate();
        Shown?.Invoke(this, EventArgs.Empty);
    }

    public void Hide()
    {
        if (!IsShown)
            return;
        IsShown = false;
        AwaitingMouseEnter = false;
        Animate(fromOffset: 0, toOffset: OffscreenOffset(), fromOpacity: 1, toOpacity: 0, onCompleted: () =>
        {
            if (!IsShown)
                _window.Hide();
        });
    }

    public void Toggle()
    {
        if (IsShown)
            Hide();
        else
            Show(activate: true);
    }

    /// <summary>Recomputes position after settings changed.</summary>
    public void Refresh()
    {
        if (!IsShown)
            return;
        UpdateLayoutForEdge();
        ApplyBounds();
    }

    private void Activate()
    {
        _window.Activate();
        NativeMethods.SetForegroundWindow(_hwnd);
    }

    private void Tick()
    {
        if (!NativeMethods.GetCursorPos(out var p))
            return;
        var now = DateTime.UtcNow;
        var settings = _settings();

        if (!IsShown)
        {
            if (IsAtEdge(p, GetScreen().Bounds, settings.Edge))
            {
                _edgeSince ??= now;
                if (now - _edgeSince >= TimeSpan.FromMilliseconds(settings.ShowDelayMs))
                    Show(activate: false);
            }
            else
            {
                _edgeSince = null;
            }
            return;
        }

        if (IsPinned || _animating || IsBusy())
        {
            _outsideSince = null;
            return;
        }

        var hitRect = _panelRect;
        hitRect.Inflate(EdgeHideMargin, EdgeHideMargin);
        if (hitRect.Contains(p.X, p.Y))
        {
            AwaitingMouseEnter = false;
            _outsideSince = null;
            return;
        }
        if (AwaitingMouseEnter)
            return;

        _outsideSince ??= now;
        if (now - _outsideSince >= TimeSpan.FromMilliseconds(settings.HideDelayMs))
            Hide();
    }

    private static bool IsAtEdge(NativeMethods.POINT p, Rectangle b, DockEdge edge)
    {
        if (!b.Contains(p.X, p.Y))
            return false;
        return edge switch
        {
            DockEdge.Left => p.X <= b.Left,
            DockEdge.Right => p.X >= b.Right - 1,
            DockEdge.Top => p.Y <= b.Top,
            DockEdge.Bottom => p.Y >= b.Bottom - 1,
            _ => false,
        };
    }

    private Screen GetScreen()
    {
        var name = _settings().MonitorDeviceName;
        return Screen.AllScreens.FirstOrDefault(s => s.DeviceName == name) ?? Screen.PrimaryScreen ?? Screen.AllScreens[0];
    }

    private void ApplyBounds()
    {
        if (!IsShown)
            return;
        var b = GetScreen().Bounds;
        _scale = NativeMethods.GetScaleForPoint(b.Left + b.Width / 2, b.Top + b.Height / 2);
        var settings = _settings();
        var size = (int)Math.Round(Math.Clamp(settings.DockSize, 200, 800) * _scale);

        _panelRect = settings.Edge switch
        {
            DockEdge.Left => new Rectangle(b.Left, b.Top, size, b.Height),
            DockEdge.Right => new Rectangle(b.Right - size, b.Top, size, b.Height),
            DockEdge.Top or DockEdge.Bottom => HorizontalRect(b, size, settings.Edge),
            _ => throw new ArgumentOutOfRangeException(),
        };
        NativeMethods.SetWindowPos(_hwnd, NativeMethods.HWND_TOPMOST,
            _panelRect.X, _panelRect.Y, _panelRect.Width, _panelRect.Height,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
    }

    private static Rectangle HorizontalRect(Rectangle b, int size, DockEdge edge)
    {
        var width = Math.Min((int)(size * 1.4), b.Width);
        var height = (int)(b.Height * 0.65);
        var x = b.Left + (b.Width - width) / 2;
        var y = edge == DockEdge.Top ? b.Top : b.Bottom - height;
        return new Rectangle(x, y, width, height);
    }

    private void UpdateLayoutForEdge()
    {
        if (_panel is not System.Windows.Controls.Border border)
            return;
        const double r = 10;
        (border.CornerRadius, border.BorderThickness) = _settings().Edge switch
        {
            DockEdge.Left => (new CornerRadius(0, r, r, 0), new Thickness(0, 0, 1, 0)),
            DockEdge.Right => (new CornerRadius(r, 0, 0, r), new Thickness(1, 0, 0, 0)),
            DockEdge.Top => (new CornerRadius(0, 0, r, r), new Thickness(1, 0, 1, 1)),
            _ => (new CornerRadius(r, r, 0, 0), new Thickness(1, 1, 1, 0)),
        };
    }

    private double OffscreenOffset()
    {
        var settings = _settings();
        var size = settings.Edge is DockEdge.Left or DockEdge.Right
            ? Math.Clamp(settings.DockSize, 200, 800)
            : _window.ActualHeight > 0 ? _window.ActualHeight : 600;
        return settings.Edge is DockEdge.Left or DockEdge.Top ? -size : size;
    }

    private void Animate(double fromOffset, double toOffset, double fromOpacity, double toOpacity, Action? onCompleted)
    {
        var horizontal = _settings().Edge is DockEdge.Left or DockEdge.Right;
        var property = horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty;
        _slide.BeginAnimation(horizontal ? TranslateTransform.YProperty : TranslateTransform.XProperty, null);
        _slide.SetValue(horizontal ? TranslateTransform.YProperty : TranslateTransform.XProperty, 0.0);

        var ease = new CubicEase { EasingMode = toOpacity > 0 ? EasingMode.EaseOut : EasingMode.EaseIn };
        var slide = new DoubleAnimation(fromOffset, toOffset, AnimationDuration) { EasingFunction = ease };
        var fade = new DoubleAnimation(fromOpacity, toOpacity, AnimationDuration);
        _animating = true;
        slide.Completed += (_, _) =>
        {
            _animating = false;
            onCompleted?.Invoke();
        };
        _slide.BeginAnimation(property, slide);
        _panel.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) =>
        _window.Dispatcher.BeginInvoke(ApplyBounds);

    public void Dispose()
    {
        _timer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }
}
