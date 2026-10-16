using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioRouter.Core.Audio;

/// <summary>Dispositivo de saída com controle vivo de volume/mudo/peak.</summary>
public sealed class DeviceHandle
{
    private readonly MMDevice _device;

    internal DeviceHandle(AudioDeviceInfo info, MMDevice device)
    {
        Info = info;
        _device = device;
    }

    public AudioDeviceInfo Info { get; }

    public float Peak
    {
        get { try { return _device.AudioMeterInformation.MasterPeakValue; } catch { return 0; } }
    }

    public void SetVolume(float volume)
    {
        try { _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0f, 1f); } catch { }
    }

    public void SetMute(bool muted)
    {
        try { _device.AudioEndpointVolume.Mute = muted; } catch { }
    }
}

/// <summary>Sessão de áudio de um app com controle vivo de volume/mudo/peak.</summary>
public sealed class SessionHandle : IDisposable
{
    private readonly AudioSessionControl _control;

    internal SessionHandle(AudioSessionInfo info, string friendlyName, AudioSessionControl control)
    {
        Info = info;
        FriendlyName = friendlyName;
        _control = control;
    }

    public AudioSessionInfo Info { get; }
    public string FriendlyName { get; }

    public float Peak
    {
        get { try { return _control.AudioMeterInformation.MasterPeakValue; } catch { return 0; } }
    }

    public void SetVolume(float volume)
    {
        try { _control.SimpleAudioVolume.Volume = Math.Clamp(volume, 0f, 1f); } catch { }
    }

    public void SetMute(bool muted)
    {
        try { _control.SimpleAudioVolume.Mute = muted; } catch { }
    }

    public void Dispose() => _control.Dispose();
}

/// <summary>
/// Retrato dos dispositivos e sessões de áudio, mantendo os objetos COM vivos para leitura de peak.
/// Descartar (Dispose) ao trocar por um retrato mais novo.
/// </summary>
public sealed class AudioSnapshot : IDisposable
{
    private readonly List<MMDevice> _mmDevices = [];
    private readonly List<DeviceHandle> _devices = [];
    private readonly List<SessionHandle> _sessions = [];

    private AudioSnapshot() { }

    public IReadOnlyList<DeviceHandle> Devices => _devices;
    public IReadOnlyList<SessionHandle> Sessions => _sessions;
    public DeviceHandle? DefaultDevice => _devices.FirstOrDefault(d => d.Info.IsDefault);

    public static AudioSnapshot Capture()
    {
        var snap = new AudioSnapshot();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            string? defaultId = null;
            try
            {
                using var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultId = def.ID;
            }
            catch { /* sem dispositivo padrão */ }

            var livePids = new HashSet<int>();
            foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                snap._mmDevices.Add(device);
                string deviceName = DeviceNameCache.Get(device);
                var info = new AudioDeviceInfo(
                    device.ID, deviceName, device.ID == defaultId,
                    device.AudioEndpointVolume.MasterVolumeLevelScalar, device.AudioEndpointVolume.Mute);
                snap._devices.Add(new DeviceHandle(info, device));

                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    if (s.State == AudioSessionState.AudioSessionStateExpired) { s.Dispose(); continue; }

                    int pid = (int)s.GetProcessID;
                    bool system = s.IsSystemSoundsSession;
                    string procName = system ? "SystemSounds" : "?";
                    string? path = null;
                    string friendly = procName;
                    if (!system && pid != 0)
                    {
                        livePids.Add(pid);
                        var details = ProcessInfoCache.Resolve(pid);
                        if (details is not null)
                        {
                            procName = details.ProcessName;
                            path = details.ExecutablePath;
                            friendly = details.FriendlyName;
                        }
                    }

                    string display = !string.IsNullOrWhiteSpace(s.DisplayName) ? s.DisplayName : friendly;
                    var sessionInfo = new AudioSessionInfo(
                        device.ID, deviceName, pid, procName, path, display,
                        s.State == AudioSessionState.AudioSessionStateActive, system,
                        s.SimpleAudioVolume.Volume, s.SimpleAudioVolume.Mute,
                        s.AudioMeterInformation.MasterPeakValue);
                    snap._sessions.Add(new SessionHandle(sessionInfo, friendly, s));
                }
            }
            ProcessInfoCache.Retain(livePids);
            return snap;
        }
        catch
        {
            snap.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var s in _sessions) s.Dispose();
        foreach (var d in _mmDevices) d.Dispose();
        _sessions.Clear();
        _devices.Clear();
        _mmDevices.Clear();
    }
}
