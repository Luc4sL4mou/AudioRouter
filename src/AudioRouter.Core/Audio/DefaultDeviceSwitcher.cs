using System.Runtime.InteropServices;

namespace AudioRouter.Core.Audio;

/// <summary>Troca o dispositivo de saída padrão do sistema via IPolicyConfig (API não documentada, estável há anos).</summary>
public static class DefaultDeviceSwitcher
{
    // eConsole=0, eMultimedia=1, eCommunications=2
    private static readonly int[] Roles = [0, 1, 2];

    public static void SetDefault(string mmDeviceId)
    {
        var type = Type.GetTypeFromCLSID(new Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9"))
                   ?? throw new InvalidOperationException("CPolicyConfigClient indisponível.");
        var config = (IPolicyConfig)Activator.CreateInstance(type)!;
        try
        {
            foreach (var role in Roles)
            {
                int hr = config.SetDefaultEndpoint(mmDeviceId, role);
                if (hr < 0) Marshal.ThrowExceptionForHR(hr);
            }
        }
        finally { Marshal.ReleaseComObject(config); }
    }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr deviceId, IntPtr format);
        [PreserveSig] int GetDeviceFormat(IntPtr deviceId, int defaultFormat, IntPtr format);
        [PreserveSig] int ResetDeviceFormat(IntPtr deviceId);
        [PreserveSig] int SetDeviceFormat(IntPtr deviceId, IntPtr endpointFormat, IntPtr mixFormat);
        [PreserveSig] int GetProcessingPeriod(IntPtr deviceId, int defaultPeriod, IntPtr defaultValue, IntPtr minimumValue);
        [PreserveSig] int SetProcessingPeriod(IntPtr deviceId, IntPtr period);
        [PreserveSig] int GetShareMode(IntPtr deviceId, IntPtr mode);
        [PreserveSig] int SetShareMode(IntPtr deviceId, IntPtr mode);
        [PreserveSig] int GetPropertyValue(IntPtr deviceId, int fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetPropertyValue(IntPtr deviceId, int fxStore, IntPtr key, IntPtr value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, int role);
        [PreserveSig] int SetEndpointVisibility(IntPtr deviceId, int visible);
    }
}
