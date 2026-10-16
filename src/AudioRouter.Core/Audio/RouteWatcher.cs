using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using AudioRouter.Core.Settings;

namespace AudioRouter.Core.Audio;

/// <summary>
/// Mantém as rotas salvas aplicadas: ao iniciar, quando um dispositivo é conectado/desconectado
/// e periodicamente (para apps que começam a tocar depois). Roda em threads do pool.
/// </summary>
public sealed class RouteWatcher : IDisposable
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan DeviceSettleDelay = TimeSpan.FromMilliseconds(700);

    private readonly RouteReconciler _reconciler;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _poll;
    private readonly Timer _debounce;
    private MMDeviceEnumerator? _enumerator;
    private NotificationClient? _client;
    private volatile bool _disposed;

    public RouteWatcher(SettingsStore store)
    {
        _reconciler = new RouteReconciler(store);
        _poll = new Timer(_ => Run(), null, Timeout.Infinite, Timeout.Infinite);
        _debounce = new Timer(_ => Run(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _client = new NotificationClient(OnDeviceChanged);
            _enumerator.RegisterEndpointNotificationCallback(_client);
        }
        catch { /* sem notificações: o polling cobre */ }
        _poll.Change(TimeSpan.Zero, PollInterval);
    }

    // O dispositivo recém-conectado leva um instante para expor suas sessões; agrupa rajadas de eventos.
    private void OnDeviceChanged()
    {
        DeviceNameCache.Clear();
        _debounce.Change(DeviceSettleDelay, Timeout.InfiniteTimeSpan);
    }

    private void Run()
    {
        if (_disposed || !_gate.Wait(0)) return;
        try
        {
            using var snapshot = AudioSnapshot.Capture();
            _reconciler.Apply(snapshot);
        }
        catch { /* serviço de áudio reiniciando etc.: próxima passada tenta de novo */ }
        finally { _gate.Release(); }
    }

    public void Dispose()
    {
        _disposed = true;
        _poll.Dispose();
        _debounce.Dispose();
        try
        {
            if (_enumerator is not null && _client is not null)
                _enumerator.UnregisterEndpointNotificationCallback(_client);
        }
        catch { }
        _enumerator?.Dispose();
    }

    private sealed class NotificationClient(Action onChanged) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => onChanged();
        public void OnDeviceAdded(string pwstrDeviceId) => onChanged();
        public void OnDeviceRemoved(string deviceId) => onChanged();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { }
        // Renomeação feita pelo usuário: só relê o nome desse dispositivo (não precisa reaplicar rotas).
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) => DeviceNameCache.Invalidate(pwstrDeviceId);
    }
}
