using System.Runtime.InteropServices;

namespace AudioRouter.Core.Audio;

/// <summary>
/// Define/lê o dispositivo de saída preferido por processo (o mesmo mecanismo de
/// "Configurações > Som > Mixer de volume" do Windows), via IAudioPolicyConfigFactory (API não documentada).
/// </summary>
public static class AppRouter
{
    private const string RenderSuffix = "#{e6327cad-dcec-4949-ae8a-991e976a79d2}"; // DEVINTERFACE_AUDIO_RENDER
    private const string MmdevPrefix = @"\\?\SWD#MMDEVAPI#";

    // eConsole=0, eMultimedia=1, eCommunications=2. Windows grava os três juntos na UI.
    private static readonly int[] Roles = [0, 1, 2];
    private const int ERender = 0;

    /// <summary>Roteia o processo para o dispositivo (ID MMDevice, ex.: "{0.0.0.00000000}.{guid}").</summary>
    public static void SetDevice(int processId, string mmDeviceId)
    {
        var factory = GetFactory();
        var hs = CreateHString(ToPolicyId(mmDeviceId));
        try
        {
            foreach (var role in Roles)
                Check(factory.SetPersistedDefaultAudioEndpoint((uint)processId, ERender, role, hs));
        }
        finally { WindowsDeleteString(hs); }
    }

    /// <summary>Remove o roteamento do processo (volta ao dispositivo padrão do sistema).</summary>
    public static void ClearDevice(int processId)
    {
        var factory = GetFactory();
        foreach (var role in Roles)
            Check(factory.SetPersistedDefaultAudioEndpoint((uint)processId, ERender, role, IntPtr.Zero));
    }

    /// <summary>Dispositivo configurado para o processo (ID MMDevice) ou null se segue o padrão.</summary>
    public static string? GetDevice(int processId)
    {
        var factory = GetFactory();
        Check(factory.GetPersistedDefaultAudioEndpoint((uint)processId, ERender, 1, out var hs));
        try
        {
            var raw = hs == IntPtr.Zero ? null : Marshal.PtrToStringUni(WindowsGetStringRawBuffer(hs, out _));
            return string.IsNullOrEmpty(raw) ? null : FromPolicyId(raw);
        }
        finally { if (hs != IntPtr.Zero) WindowsDeleteString(hs); }
    }

    private static string ToPolicyId(string mmId) => MmdevPrefix + mmId + RenderSuffix;

    private static string FromPolicyId(string policyId)
    {
        var s = policyId;
        if (s.StartsWith(MmdevPrefix, StringComparison.OrdinalIgnoreCase)) s = s[MmdevPrefix.Length..];
        if (s.EndsWith(RenderSuffix, StringComparison.OrdinalIgnoreCase)) s = s[..^RenderSuffix.Length];
        return s;
    }

    private static void Check(int hr)
    {
        if (hr < 0) Marshal.ThrowExceptionForHR(hr);
    }

    private static IAudioPolicyConfig GetFactory()
    {
        var classId = CreateHString("Windows.Media.Internal.AudioPolicyConfig");
        try
        {
            var iid = typeof(IAudioPolicyConfig).GUID;
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(classId, ref iid, out var ptr));
            try { return (IAudioPolicyConfig)Marshal.GetObjectForIUnknown(ptr); }
            finally { Marshal.Release(ptr); }
        }
        finally { WindowsDeleteString(classId); }
    }

    private static IntPtr CreateHString(string s)
    {
        Marshal.ThrowExceptionForHR(WindowsCreateString(s, s.Length, out var h));
        return h;
    }

    // Variante do Windows 10 21H2+ / Windows 11. Os 3 primeiros métodos são os de IInspectable.
    [ComImport, Guid("ab3d4648-e242-459f-b02f-541c70306324"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAudioPolicyConfig
    {
        [PreserveSig] int GetIids(out int count, out IntPtr iids);
        [PreserveSig] int GetRuntimeClassName(out IntPtr className);
        [PreserveSig] int GetTrustLevel(out int trustLevel);

        [PreserveSig] int _add_CtxVolumeChange();
        [PreserveSig] int _remove_CtxVolumeChange();
        [PreserveSig] int _add_RingerVibrateStateChanged();
        [PreserveSig] int _remove_RingerVibrateStateChange();
        [PreserveSig] int _SetVolumeGroupGainForId();
        [PreserveSig] int _GetVolumeGroupGainForId();
        [PreserveSig] int _GetActiveVolumeGroupForEndpointId();
        [PreserveSig] int _GetVolumeGroupsForEndpoint();
        [PreserveSig] int _GetCurrentVolumeContext();
        [PreserveSig] int _SetVolumeGroupMuteForId();
        [PreserveSig] int _GetVolumeGroupMuteForId();
        [PreserveSig] int _SetRingerVibrateState();
        [PreserveSig] int _GetRingerVibrateState();
        [PreserveSig] int _SetPreferredChatApplication();
        [PreserveSig] int _ResetPreferredChatApplication();
        [PreserveSig] int _GetPreferredChatApplication();
        [PreserveSig] int _GetCurrentChatApplications();
        [PreserveSig] int _add_ChatContextChanged();
        [PreserveSig] int _remove_ChatContextChanged();

        [PreserveSig] int SetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, IntPtr deviceId);
        [PreserveSig] int GetPersistedDefaultAudioEndpoint(uint processId, int flow, int role, out IntPtr deviceId);
        [PreserveSig] int ClearAllPersistedApplicationDefaultEndpoints();
    }

    [DllImport("combase.dll")]
    private static extern int RoGetActivationFactory(IntPtr activatableClassId, ref Guid iid, out IntPtr factory);

    [DllImport("combase.dll", CharSet = CharSet.Unicode)]
    private static extern int WindowsCreateString(string source, int length, out IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern int WindowsDeleteString(IntPtr hstring);

    [DllImport("combase.dll")]
    private static extern IntPtr WindowsGetStringRawBuffer(IntPtr hstring, out uint length);
}
