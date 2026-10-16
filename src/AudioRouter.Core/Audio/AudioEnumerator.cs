using System.Diagnostics;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioRouter.Core.Audio;

/// <summary>Enumera dispositivos de saída e sessões de áudio via Core Audio (NAudio).</summary>
public static class AudioEnumerator
{
    public static IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        string? defaultId = null;
        try
        {
            using var def = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            defaultId = def.ID;
        }
        catch { /* sem dispositivo padrão */ }

        var result = new List<AudioDeviceInfo>();
        foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (d)
            {
                result.Add(new AudioDeviceInfo(
                    d.ID, d.FriendlyName, d.ID == defaultId,
                    d.AudioEndpointVolume.MasterVolumeLevelScalar,
                    d.AudioEndpointVolume.Mute));
            }
        }
        return result;
    }

    /// <summary>Sessões de todos os dispositivos de saída ativos (sessões expiradas são ignoradas).</summary>
    public static IReadOnlyList<AudioSessionInfo> GetSessions()
    {
        using var enumerator = new MMDeviceEnumerator();
        var result = new List<AudioSessionInfo>();

        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device)
            {
                var sessions = device.AudioSessionManager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    var s = sessions[i];
                    if (s.State == AudioSessionState.AudioSessionStateExpired) continue;

                    int pid = (int)s.GetProcessID;
                    bool system = s.IsSystemSoundsSession;
                    string procName = system ? "SystemSounds" : "?";
                    string? path = null;
                    if (!system && pid != 0)
                    {
                        try
                        {
                            using var p = Process.GetProcessById(pid);
                            procName = p.ProcessName;
                            path = p.MainModule?.FileName;
                        }
                        catch { /* processo encerrou ou acesso negado */ }
                    }

                    string display = !string.IsNullOrWhiteSpace(s.DisplayName) ? s.DisplayName : procName;
                    result.Add(new AudioSessionInfo(
                        device.ID, device.FriendlyName, pid, procName, path, display,
                        s.State == AudioSessionState.AudioSessionStateActive, system,
                        s.SimpleAudioVolume.Volume, s.SimpleAudioVolume.Mute,
                        s.AudioMeterInformation.MasterPeakValue));
                }
            }
        }
        return result;
    }
}
