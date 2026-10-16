namespace AudioRouter.Core.Audio;

/// <summary>Dispositivo de saída (render) ativo.</summary>
public sealed record AudioDeviceInfo(string Id, string Name, bool IsDefault, float Volume, bool IsMuted);

/// <summary>Sessão de áudio de um aplicativo em um dispositivo de saída.</summary>
public sealed record AudioSessionInfo(
    string DeviceId,
    string DeviceName,
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    string DisplayName,
    bool IsActive,
    bool IsSystemSounds,
    float Volume,
    bool IsMuted,
    float Peak);
