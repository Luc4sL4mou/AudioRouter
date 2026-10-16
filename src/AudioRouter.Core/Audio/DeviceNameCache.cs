using System.Collections.Concurrent;
using NAudio.CoreAudioApi;

namespace AudioRouter.Core.Audio;

/// <summary>
/// MMDevice.FriendlyName lê o property store do dispositivo (~70 ms por chamada). O nome fica em cache por ID
/// e só é relido quando o Core Audio avisa que o dispositivo mudou (ver RouteWatcher).
/// </summary>
internal static class DeviceNameCache
{
    private static readonly ConcurrentDictionary<string, string> Cache = new();

    public static string Get(MMDevice device) =>
        Cache.GetOrAdd(device.ID, _ => device.FriendlyName);

    public static void Invalidate(string deviceId) => Cache.TryRemove(deviceId, out _);

    public static void Clear() => Cache.Clear();
}
