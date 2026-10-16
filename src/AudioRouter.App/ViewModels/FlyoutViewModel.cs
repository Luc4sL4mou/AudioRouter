using System.Collections.ObjectModel;
using System.Windows.Threading;
using AudioRouter.Core.Audio;
using AudioRouter.Core.Settings;

namespace AudioRouter.App.ViewModels;

public sealed class FlyoutViewModel : ObservableObject, IDisposable
{
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _peakTimer;
    private AudioSnapshot? _snapshot;
    private DeviceHandle? _master;
    private bool _suppress;
    private bool _refreshing;

    private string _defaultDeviceId = "";
    private double _masterVolume;
    private bool _masterMuted;
    private double _masterPeak;

    private readonly SettingsStore _store;

    public FlyoutViewModel(SettingsStore store)
    {
        _store = store;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refreshTimer.Tick += (_, _) => Refresh();
        _peakTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(33) };
        _peakTimer.Tick += (_, _) => UpdatePeaks();
        Apps.CollectionChanged += (_, _) => { Raise(nameof(HasApps)); Raise(nameof(HasNoApps)); };
    }

    /// <summary>Dispositivos reais (seletor do dispositivo padrão).</summary>
    public ObservableCollection<DeviceOption> Devices { get; } = [];

    /// <summary>"Padrão do sistema" + dispositivos reais (seletor de roteamento por app).</summary>
    public ObservableCollection<DeviceOption> RoutingOptions { get; } = [];

    public ObservableCollection<AppRowViewModel> Apps { get; } = [];
    public bool HasApps => Apps.Count > 0;
    public bool HasNoApps => Apps.Count == 0;

    public bool IsDraggingMaster { get; set; }

    public string DefaultDeviceId
    {
        get => _defaultDeviceId;
        set
        {
            if (value is null) return;
            if (!Set(ref _defaultDeviceId, value)) return;
            if (_suppress || value.Length == 0) return;
            try { DefaultDeviceSwitcher.SetDefault(value); } catch { /* a próxima atualização reflete o estado real */ }
            Refresh();
        }
    }

    public double MasterVolume
    {
        get => _masterVolume;
        set
        {
            if (!Set(ref _masterVolume, value)) return;
            Raise(nameof(MasterVolumeText));
            Raise(nameof(MasterGlyph));
            if (!_suppress) _master?.SetVolume((float)(value / 100.0));
        }
    }

    public string MasterVolumeText => $"{Math.Round(_masterVolume):0}";
    public bool MasterMuted { get => _masterMuted; private set { if (Set(ref _masterMuted, value)) Raise(nameof(MasterGlyph)); } }
    public string MasterGlyph => Glyphs.Volume(_masterVolume, _masterMuted);
    public double MasterPeak { get => _masterPeak; private set => Set(ref _masterPeak, value); }

    public void ToggleMasterMute()
    {
        bool target = !_masterMuted;
        _master?.SetMute(target);
        MasterMuted = target;
    }

    /// <summary>Chamado quando o flyout abre: captura síncrona (a janela já abre com o tamanho final) e inicia os timers.</summary>
    public void Start()
    {
        Refresh();
        _refreshTimer.Start();
        _peakTimer.Start();
    }

    /// <summary>Chamado quando o flyout fecha: para tudo e libera os objetos COM.</summary>
    public void Stop()
    {
        _refreshTimer.Stop();
        _peakTimer.Stop();
        Apps.Clear();
        _master = null;
        _snapshot?.Dispose();
        _snapshot = null;
    }

    private void Refresh()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            AudioSnapshot next;
            try { next = AudioSnapshot.Capture(); }
            catch { return; } // serviço de áudio indisponível: tenta de novo no próximo tick

            Apply(next);
            var old = _snapshot;
            _snapshot = next;
            old?.Dispose();
        }
        finally { _refreshing = false; }
    }

    private void Apply(AudioSnapshot snap)
    {
        // Dispositivos
        var ids = snap.Devices.Select(d => d.Info.Id).ToList();
        bool sameSet = ids.SequenceEqual(Devices.Select(d => d.Id));
        if (!sameSet)
        {
            Devices.Clear();
            RoutingOptions.Clear();
            RoutingOptions.Add(new DeviceOption("", "Padrão do sistema"));
            foreach (var d in snap.Devices)
            {
                Devices.Add(new DeviceOption(d.Info.Id, d.Info.Name));
                RoutingOptions.Add(new DeviceOption(d.Info.Id, d.Info.Name));
            }
        }
        else
        {
            foreach (var d in snap.Devices)
            {
                var opt = Devices.First(o => o.Id == d.Info.Id);
                opt.Name = d.Info.Name;
                RoutingOptions.First(o => o.Id == d.Info.Id).Name = d.Info.Name;
            }
        }

        // Dispositivo padrão e volume mestre
        _master = snap.DefaultDevice;
        _suppress = true;
        try
        {
            DefaultDeviceId = _master?.Info.Id ?? "";
            if (!IsDraggingMaster && _master is not null) MasterVolume = Math.Round(_master.Info.Volume * 100);
            MasterMuted = _master?.Info.IsMuted ?? false;
        }
        finally { _suppress = false; }

        // Apps (agrupados por processo, ordem alfabética estável)
        var groups = snap.Sessions
            .Where(s => !s.Info.IsSystemSounds && s.Info.ProcessId != 0)
            .GroupBy(s => s.Info.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<SessionHandle>)g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var gone in Apps.Where(a => !groups.ContainsKey(a.Key)).ToList())
            Apps.Remove(gone);

        var known = ids.ToHashSet();
        foreach (var (key, sessions) in groups)
        {
            var row = Apps.FirstOrDefault(a => string.Equals(a.Key, key, StringComparison.OrdinalIgnoreCase));
            if (row is null)
            {
                row = new AppRowViewModel(key, RoutingOptions, _store);
                row.Update(sessions, known);
                int index = 0;
                while (index < Apps.Count && string.Compare(Apps[index].Name, row.Name, StringComparison.CurrentCultureIgnoreCase) < 0) index++;
                Apps.Insert(index, row);
            }
            else row.Update(sessions, known);
        }

        if (!sameSet)
        {
            foreach (var row in Apps) row.RefreshSelection();
            Raise(nameof(DefaultDeviceId));
        }
    }

    private void UpdatePeaks()
    {
        double raw = 0;
        try { raw = _master?.Peak ?? 0; } catch { }
        MasterPeak = Math.Max(raw, _masterPeak * 0.82);
        foreach (var row in Apps) row.UpdatePeak();
    }

    public void Dispose() => Stop();
}
