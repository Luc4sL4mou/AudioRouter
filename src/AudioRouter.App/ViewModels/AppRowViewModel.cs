using System.Collections.ObjectModel;
using System.Windows.Media;
using AudioRouter.Core.Audio;
using AudioRouter.Core.Settings;

namespace AudioRouter.App.ViewModels;

/// <summary>Um app com sessão(ões) de áudio, agrupado por nome de processo.</summary>
public sealed class AppRowViewModel : ObservableObject
{
    private IReadOnlyList<SessionHandle> _sessions = [];
    private double _volume;
    private bool _muted;
    private double _peak;
    private bool _active;
    private string _routeDeviceId = "";
    private bool _suppress;

    private readonly SettingsStore _store;

    public AppRowViewModel(string key, ObservableCollection<DeviceOption> routingOptions, SettingsStore store)
    {
        Key = key;
        RoutingOptions = routingOptions;
        _store = store;
    }

    public string Key { get; }
    public ObservableCollection<DeviceOption> RoutingOptions { get; }

    public string Name { get; private set; } = "";
    public ImageSource? Icon { get; private set; }
    public bool HasIcon => Icon is not null;
    public bool NoIcon => Icon is null;

    /// <summary>Verdadeiro enquanto o usuário arrasta o slider (a atualização do sistema não deve brigar com ele).</summary>
    public bool IsDragging { get; set; }

    public bool IsActive { get => _active; private set { if (Set(ref _active, value)) Raise(nameof(RowOpacity)); } }
    public double RowOpacity => _active ? 1.0 : 0.7;

    public double Peak { get => _peak; private set => Set(ref _peak, value); }

    public double Volume
    {
        get => _volume;
        set
        {
            if (!Set(ref _volume, value)) return;
            Raise(nameof(VolumeText));
            Raise(nameof(MuteGlyph));
            if (_suppress) return;
            foreach (var s in _sessions) s.SetVolume((float)(value / 100.0));
        }
    }

    public string VolumeText => $"{Math.Round(_volume):0}";

    public bool IsMuted
    {
        get => _muted;
        private set { if (Set(ref _muted, value)) Raise(nameof(MuteGlyph)); }
    }

    public string MuteGlyph => Glyphs.Volume(_volume, _muted);

    public void ToggleMute()
    {
        bool target = !_muted;
        foreach (var s in _sessions) s.SetMute(target);
        IsMuted = target;
    }

    /// <summary>Dispositivo de roteamento (Id vazio = padrão). Alterar aplica o roteamento a todos os processos do app.</summary>
    public string RouteDeviceId
    {
        get => _routeDeviceId;
        set
        {
            if (value is null) return; // ComboBox limpa a seleção ao reconstruir a lista
            if (!Set(ref _routeDeviceId, value)) return;
            if (_suppress) return;
            ApplyRoute(value);
        }
    }

    private void ApplyRoute(string deviceId)
    {
        // Salva antes de aplicar: assim o reaplicador em segundo plano nunca vê o estado antigo e desfaz a escolha.
        var routeKey = AppRouteKey.From(Key);
        if (deviceId.Length == 0) _store.RemoveRoute(routeKey);
        else _store.SetRoute(routeKey, deviceId, RoutingOptions.FirstOrDefault(o => o.Id == deviceId)?.Name ?? "");

        foreach (var pid in _sessions.Select(s => s.Info.ProcessId).Distinct())
        {
            try
            {
                if (deviceId.Length == 0) AppRouter.ClearDevice(pid);
                else AppRouter.SetDevice(pid, deviceId);
            }
            catch { /* processo encerrou ou API indisponível: a próxima atualização reflete o estado real */ }
        }
    }

    /// <summary>Reenvia a seleção atual ao ComboBox (após a lista de dispositivos ser reconstruída).</summary>
    public void RefreshSelection() => Raise(nameof(RouteDeviceId));

    public void Update(IReadOnlyList<SessionHandle> sessions, IReadOnlySet<string> knownDeviceIds)
    {
        _sessions = sessions;
        var first = sessions.OrderByDescending(s => s.Info.IsActive).First();

        if (Name != first.FriendlyName)
        {
            Name = first.FriendlyName;
            Raise(nameof(Name));
        }
        if (Icon is null)
        {
            var icon = AppIconService.Get(first.Info.ExecutablePath);
            if (icon is not null)
            {
                Icon = icon;
                Raise(nameof(Icon));
                Raise(nameof(HasIcon));
                Raise(nameof(NoIcon));
            }
        }

        IsActive = sessions.Any(s => s.Info.IsActive);

        _suppress = true;
        try
        {
            if (!IsDragging) Volume = Math.Round(sessions.Max(s => s.Info.Volume) * 100);
            IsMuted = sessions.All(s => s.Info.IsMuted);

            string route = "";
            try { route = AppRouter.GetDevice(first.Info.ProcessId) ?? ""; } catch { }
            if (route.Length > 0 && !knownDeviceIds.Contains(route)) route = "";
            RouteDeviceId = route;
        }
        finally { _suppress = false; }
    }

    /// <summary>Lê o peak das sessões (chamado ~30x/s); cai gradualmente para o meter não piscar.</summary>
    public void UpdatePeak()
    {
        double raw = 0;
        foreach (var s in _sessions) raw = Math.Max(raw, s.Peak);
        Peak = Math.Max(raw, _peak * 0.82);
    }
}
