using System.Windows;
using AudioRouter.Core.Audio;
using AudioRouter.Core.Settings;

namespace AudioRouter.App;

public partial class AudioRouterApp : System.Windows.Application
{
    private Mutex? _singleInstance;
    private SettingsStore? _settings;
    private RouteWatcher? _watcher;
    private TrayIconService? _tray;
    private FlyoutWindow? _flyout;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "AudioRouter.SingleInstance", out bool isFirst);
        if (!isFirst)
        {
            Shutdown();
            return;
        }

        // Opções de teste: --show abre o flyout ao iniciar; --keep-open não fecha ao perder o foco.
        bool show = e.Args.Contains("--show");
        bool keepOpen = e.Args.Contains("--keep-open");

        // Opção de teste: --theme=light|dark ignora o tema do sistema.
        var themeArg = e.Args.FirstOrDefault(a => a.StartsWith("--theme=", StringComparison.Ordinal));
        if (themeArg is not null) SystemTheme.Override = themeArg.EndsWith("light", StringComparison.Ordinal);
        SystemTheme.ApplyToApplication();

        _settings = new SettingsStore();
        SyncStartupRegistration();

        // Reaplica as rotas salvas ao iniciar e sempre que um dispositivo conectar/desconectar.
        _watcher = new RouteWatcher(_settings);
        _watcher.Start();

        _flyout = new FlyoutWindow(_settings) { KeepOpen = keepOpen };
        _tray = new TrayIconService { GetStartWithWindows = () => _settings.StartWithWindows };
        _tray.LeftClick += () => _flyout.Toggle();
        _tray.StartWithWindowsChanged += enabled =>
        {
            _settings.StartWithWindows = enabled;
            SyncStartupRegistration();
        };
        _tray.ExitRequested += Shutdown;

        if (show) _flyout.ShowFlyout();
    }

    /// <summary>O JSON é a fonte da verdade; o registro é ajustado a ele (inclusive se o exe mudou de pasta).</summary>
    private void SyncStartupRegistration()
    {
        try
        {
            if (Environment.ProcessPath is { } exe)
                StartupRegistration.Sync(_settings!.StartWithWindows, exe);
        }
        catch { /* registro bloqueado por política: a preferência segue salva */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _watcher?.Dispose();
        _tray?.Dispose();
        _flyout?.Dispose();
        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
