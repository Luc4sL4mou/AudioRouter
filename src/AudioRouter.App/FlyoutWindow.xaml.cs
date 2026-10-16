using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using AudioRouter.App.ViewModels;

namespace AudioRouter.App;

public partial class FlyoutWindow : Window, IDisposable
{
    private const int MarginDip = 12;

    private readonly FlyoutViewModel _vm;
    private DateTime _lastHidden = DateTime.MinValue;
    private bool _backdropApplied;
    private Vector _slideFrom = new(0, 14);

    /// <summary>Quando verdadeiro, o flyout não fecha ao perder o foco (usado para testes).</summary>
    public bool KeepOpen { get; init; }

    public FlyoutWindow(AudioRouter.Core.Settings.SettingsStore store)
    {
        _vm = new FlyoutViewModel(store);
        InitializeComponent();
        DataContext = _vm;
        Deactivated += (_, _) => { if (!KeepOpen) HideFlyout(); };
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) HideFlyout(); };
        SizeChanged += (_, e) => { if (IsVisible && e.HeightChanged) Reposition(); };
    }

    public void Toggle()
    {
        if (IsVisible) { HideFlyout(); return; }
        // O clique no ícone da bandeja tira o foco do flyout (fechando-o) antes de chegar aqui; sem isto ele reabriria.
        if ((DateTime.UtcNow - _lastHidden).TotalMilliseconds < 250) return;
        ShowFlyout();
    }

    public void ShowFlyout()
    {
        SystemTheme.ApplyToApplication();
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        ApplyBackdrop(hwnd);

        Opacity = 0;
        MaxHeight = WorkAreaHeightDip(hwnd);
        _vm.Start();
        Show();
        UpdateLayout();
        Reposition();
        Activate();
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(120)));
        PlayEntranceAnimation();
    }

    /// <summary>O conteúdo desliza e aparece a partir da borda da barra de tarefas (movimento curto, como os flyouts do Windows 11).</summary>
    private void PlayEntranceAnimation()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(220);
        SlideTransform.BeginAnimation(System.Windows.Media.TranslateTransform.XProperty,
            new DoubleAnimation(_slideFrom.X, 0, duration) { EasingFunction = ease });
        SlideTransform.BeginAnimation(System.Windows.Media.TranslateTransform.YProperty,
            new DoubleAnimation(_slideFrom.Y, 0, duration) { EasingFunction = ease });
        Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    public void HideFlyout()
    {
        if (!IsVisible) return;
        _lastHidden = DateTime.UtcNow;
        Hide();
        _vm.Stop();
    }

    /// <summary>Acrílico, cantos arredondados e tema escuro/claro via DWM (Windows 11).</summary>
    private void ApplyBackdrop(IntPtr hwnd)
    {
        int dark = SystemTheme.AppsUseLightTheme ? 0 : 1;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
        if (_backdropApplied) return;
        _backdropApplied = true;

        int corner = Native.DWMWCP_ROUND;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));

        // Antes do Windows 11 22H2 não há backdrop do sistema: fica a película opaca do tema (ver SystemTheme).
        if (!SystemTheme.SupportsSystemBackdrop) return;

        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is { } target)
            target.BackgroundColor = System.Windows.Media.Colors.Transparent;

        int backdrop = Native.DWMSBT_TRANSIENTWINDOW;
        Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, sizeof(int));
        var margins = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Native.DwmExtendFrameIntoClientArea(hwnd, ref margins);
    }

    private static System.Windows.Forms.Screen CurrentScreen() =>
        System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position);

    private double WorkAreaHeightDip(IntPtr hwnd)
    {
        double scale = Math.Max(1, Native.GetDpiForWindow(hwnd) / 96.0);
        return CurrentScreen().WorkingArea.Height / scale - 2 * MarginDip;
    }

    /// <summary>Encosta o flyout no canto da área útil mais próximo da barra de tarefas (em pixels físicos).</summary>
    private void Reposition()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero || !Native.GetWindowRect(hwnd, out var r)) return;

        var screen = CurrentScreen();
        var work = screen.WorkingArea;
        var bounds = screen.Bounds;
        int margin = (int)Math.Round(MarginDip * Native.GetDpiForWindow(hwnd) / 96.0);
        int width = r.Right - r.Left, height = r.Bottom - r.Top;

        bool taskbarLeft = work.Left > bounds.Left;
        bool taskbarTop = work.Top > bounds.Top;
        bool taskbarRight = work.Right < bounds.Right;
        int x = taskbarLeft ? work.Left + margin : work.Right - width - margin;
        int y = taskbarTop ? work.Top + margin : work.Bottom - height - margin;

        // Direção de onde o conteúdo "vem": da borda onde está a barra de tarefas.
        const double slide = 14;
        _slideFrom = taskbarLeft ? new Vector(-slide, 0)
                   : taskbarRight ? new Vector(slide, 0)
                   : taskbarTop ? new Vector(0, -slide)
                   : new Vector(0, slide);

        Native.SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, Native.SWP_NOSIZE | Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
    }

    private void OnMasterMuteClick(object sender, RoutedEventArgs e) => _vm.ToggleMasterMute();
    private void OnMasterSliderDown(object sender, MouseButtonEventArgs e) => _vm.IsDraggingMaster = true;
    private void OnMasterSliderUp(object sender, MouseEventArgs e) => _vm.IsDraggingMaster = false;

    public void Dispose() => _vm.Dispose();
}
