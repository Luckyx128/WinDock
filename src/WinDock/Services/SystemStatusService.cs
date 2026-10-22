using System;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WinDock.Models;

namespace WinDock.Services;

#region CoreAudio Interop Definitions

internal enum EDataFlow
{
    eRender = 0,
    eCapture = 1,
    eAll = 2
}

internal enum ERole
{
    eConsole = 0,
    eMultimedia = 1,
    eCommunications = 2
}

internal enum AudioSessionState
{
    AudioSessionStateInactive = 0,
    AudioSessionStateActive = 1,
    AudioSessionStateExpired = 2
}

[ComImport]
[Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
internal class MMDeviceEnumeratorComObject
{
}

[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IntPtr ppDevices);
    [PreserveSig]
    int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);
    int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);
    int RegisterEndpointNotificationCallback(IntPtr pClient);
    int UnregisterEndpointNotificationCallback(IntPtr pClient);
}

[Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig]
    int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);
    int OpenPropertyStore(uint stgmAccess, out IntPtr ppProperties);
    int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);
    int GetState(out uint pdwState);
}

[Guid("5BC64874-38E4-4DB6-8863-F5D22F834590"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
    int RegisterControlChangeNotify(IntPtr pNotify);
    int UnregisterControlChangeNotify(IntPtr pNotify);
    int GetChannelCount(out uint pnChannelCount);
    int SetMasterVolumeLevel(float fLevelDB, ref Guid pguidEventContext);
    [PreserveSig]
    int SetMasterVolumeLevelScalar(float fLevel, ref Guid pguidEventContext);
    int GetMasterVolumeLevel(out float pfLevelDB);
    [PreserveSig]
    int GetMasterVolumeLevelScalar(out float pfLevel);
    int SetChannelVolumeLevel(uint nChannel, float fLevelDB, ref Guid pguidEventContext);
    int SetChannelVolumeLevelScalar(uint nChannel, float fLevel, ref Guid pguidEventContext);
    int GetChannelVolumeLevel(uint nChannel, out float pfLevelDB);
    int GetChannelVolumeLevelScalar(uint nChannel, out float pfLevel);
    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid pguidEventContext);
    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
    int GetVolumeStepInfo(out uint pnStep, out uint pnStepCount);
    [PreserveSig]
    int VolumeStepUp(ref Guid pguidEventContext);
    [PreserveSig]
    int VolumeStepDown(ref Guid pguidEventContext);
    int QueryHardwareSupport(out uint pdwHardwareSupportMask);
    int GetVolumeRange(out float pflVolumeMindB, out float pflVolumeMaxdB, out float pflVolumeIncrementdB);
}

[Guid("77AA99A0-1BD6-484F-BC0A-511599C04DAE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionManager2
{
    int GetAudioSessionControl(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr SessionControl);
    int GetSimpleAudioVolume(ref Guid AudioSessionGuid, uint StreamFlags, out IntPtr AudioVolume);
    [PreserveSig]
    int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);
    int RegisterSessionNotification(IntPtr NewSession);
    int UnregisterSessionNotification(IntPtr NewSession);
    int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionID, IntPtr duckNotification);
    int UnregisterDuckNotification(IntPtr duckNotification);
}

[Guid("E2F5BB11-070F-4CC5-A56A-D2723B5D1682"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionEnumerator
{
    [PreserveSig]
    int GetCount(out int SessionCount);
    [PreserveSig]
    int GetSession(int SessionIndex, out IAudioSessionControl Session);
}

[Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl
{
    [PreserveSig]
    int GetState(out AudioSessionState pRetVal);
    int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
    int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
    int GetGroupingParam(out Guid pRetVal);
    int SetGroupingParam(ref Guid Override, ref Guid EventContext);
    int RegisterAudioSessionNotification(IntPtr NewNotifications);
    int UnregisterAudioSessionNotification(IntPtr NewNotifications);
}

[Guid("bfb7ff88-7239-4fc9-8fa8-8ae4f24f467c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioSessionControl2 : IAudioSessionControl
{
    [PreserveSig]
    new int GetState(out AudioSessionState pRetVal);
    new int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    new int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
    new int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    new int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);
    new int GetGroupingParam(out Guid pRetVal);
    new int SetGroupingParam(ref Guid Override, ref Guid EventContext);
    new int RegisterAudioSessionNotification(IntPtr NewNotifications);
    new int UnregisterAudioSessionNotification(IntPtr NewNotifications);

    int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);
    int IsSystemSoundsSession();
    int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    [PreserveSig]
    int GetProcessId(out uint pRetVal);
}

[Guid("87CE5498-6878-4479-A46B-C470AE2CED70"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISimpleAudioVolume
{
    [PreserveSig]
    int SetMasterVolume(float fLevel, ref Guid EventContext);
    [PreserveSig]
    int GetMasterVolume(out float pfLevel);
    [PreserveSig]
    int SetMute([MarshalAs(UnmanagedType.Bool)] bool bMute, ref Guid EventContext);
    [PreserveSig]
    int GetMute([MarshalAs(UnmanagedType.Bool)] out bool pbMute);
}

internal static class CoreAudioHelper
{
    private const uint CLSCTX_INPROC_SERVER = 1;

    public static IAudioEndpointVolume? GetDefaultEndpointVolume(EDataFlow dataFlow, ERole role = ERole.eMultimedia)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(dataFlow, role, out IMMDevice endpoint) == 0 && endpoint != null)
            {
                var iid = typeof(IAudioEndpointVolume).GUID;
                if (endpoint.Activate(ref iid, CLSCTX_INPROC_SERVER, IntPtr.Zero, out object obj) == 0)
                {
                    return obj as IAudioEndpointVolume;
                }
            }
        }
        catch
        {
        }
        return null;
    }

    public static IAudioSessionManager2? GetDefaultAudioSessionManager(EDataFlow dataFlow, ERole role = ERole.eMultimedia)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            if (enumerator.GetDefaultAudioEndpoint(dataFlow, role, out IMMDevice endpoint) == 0 && endpoint != null)
            {
                var iid = typeof(IAudioSessionManager2).GUID;
                if (endpoint.Activate(ref iid, CLSCTX_INPROC_SERVER, IntPtr.Zero, out object obj) == 0)
                {
                    return obj as IAudioSessionManager2;
                }
            }
        }
        catch
        {
        }
        return null;
    }
}

#endregion

public sealed class SystemStatusService : ISystemStatusService
{
    private SystemTrayStatus _cachedStatus = new();
    private DateTime _lastPollUtc = DateTime.MinValue;

    public SystemTrayStatus GetSystemTrayStatus()
    {
        if ((DateTime.UtcNow - _lastPollUtc).TotalMilliseconds < 500)
        {
            return _cachedStatus;
        }

        _lastPollUtc = DateTime.UtcNow;
        _cachedStatus = ReadSystemTrayStatus();
        return _cachedStatus;
    }

    public void SetMasterVolume(int percent)
    {
        try
        {
            int clamped = Math.Clamp(percent, 0, 100);
            float scalar = clamped / 100.0f;
            var endpointVolume = CoreAudioHelper.GetDefaultEndpointVolume(EDataFlow.eRender, ERole.eMultimedia);
            if (endpointVolume != null)
            {
                var empty = Guid.Empty;
                endpointVolume.SetMasterVolumeLevelScalar(scalar, ref empty);
            }
        }
        catch
        {
        }
    }

    public void ToggleMasterMute()
    {
        try
        {
            var endpointVolume = CoreAudioHelper.GetDefaultEndpointVolume(EDataFlow.eRender, ERole.eMultimedia);
            if (endpointVolume != null)
            {
                if (endpointVolume.GetMute(out bool isMuted) == 0)
                {
                    var empty = Guid.Empty;
                    endpointVolume.SetMute(!isMuted, ref empty);
                }
            }
        }
        catch
        {
        }
    }

    public void SetMasterMute(bool isMuted)
    {
        try
        {
            var endpointVolume = CoreAudioHelper.GetDefaultEndpointVolume(EDataFlow.eRender, ERole.eMultimedia);
            if (endpointVolume != null)
            {
                var empty = Guid.Empty;
                endpointVolume.SetMute(isMuted, ref empty);
            }
        }
        catch
        {
        }
    }

    public void VolumeUp(int stepPercent = 2)
    {
        try
        {
            var endpointVolume = CoreAudioHelper.GetDefaultEndpointVolume(EDataFlow.eRender, ERole.eMultimedia);
            if (endpointVolume != null)
            {
                if (endpointVolume.GetMasterVolumeLevelScalar(out float scalar) == 0)
                {
                    float next = Math.Clamp(scalar + (stepPercent / 100.0f), 0.0f, 1.0f);
                    var empty = Guid.Empty;
                    endpointVolume.SetMasterVolumeLevelScalar(next, ref empty);
                }
            }
        }
        catch
        {
        }
    }

    public void VolumeDown(int stepPercent = 2)
    {
        try
        {
            var endpointVolume = CoreAudioHelper.GetDefaultEndpointVolume(EDataFlow.eRender, ERole.eMultimedia);
            if (endpointVolume != null)
            {
                if (endpointVolume.GetMasterVolumeLevelScalar(out float scalar) == 0)
                {
                    float next = Math.Clamp(scalar - (stepPercent / 100.0f), 0.0f, 1.0f);
                    var empty = Guid.Empty;
                    endpointVolume.SetMasterVolumeLevelScalar(next, ref empty);
                }
            }
        }
        catch
        {
        }
    }

    private static SystemTrayStatus ReadSystemTrayStatus()
    {
        // 1. Bateria (SystemInformation.PowerStatus)
        bool hasBattery = false;
        int batteryPercent = 100;
        bool isCharging = false;
        string powerLineStr = "Online";
        string batterySummary = "100%";

        try
        {
            var power = SystemInformation.PowerStatus;
            powerLineStr = power.PowerLineStatus == PowerLineStatus.Online ? "Online" : "Offline";
            isCharging = power.PowerLineStatus == PowerLineStatus.Online;

            float life = power.BatteryLifePercent;
            bool noBattery = power.BatteryChargeStatus.HasFlag(BatteryChargeStatus.NoSystemBattery)
                || life < 0f || life > 1.0f || (int)life == 255;
            hasBattery = !noBattery;

            if (hasBattery)
            {
                batteryPercent = Math.Clamp((int)Math.Round(life * 100), 0, 100);
                batterySummary = isCharging
                    ? $"Carregando ({batteryPercent}%)"
                    : $"{batteryPercent}% restante";
            }
            else
            {
                batteryPercent = 100;
                batterySummary = "Conectado à tomada (Desktop)";
            }
        }
        catch
        {
            // Fallback for systems where PowerStatus is inaccessible
        }

        // 2. Rede (NetworkInterface)
        bool isConnected = false;
        bool isEthernet = false;
        bool isWifi = false;
        string networkName = string.Empty;

        try
        {
            isConnected = NetworkInterface.GetIsNetworkAvailable();
            var interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (var nic in interfaces)
            {
                if (nic.OperationalStatus != OperationalStatus.Up ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Loopback ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel)
                {
                    continue;
                }

                if (nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.GigabitEthernet ||
                    nic.NetworkInterfaceType == NetworkInterfaceType.FastEthernetFx)
                {
                    isEthernet = true;
                    if (string.IsNullOrEmpty(networkName))
                    {
                        networkName = !string.IsNullOrWhiteSpace(nic.Description) ? nic.Description : nic.Name;
                    }
                }
                else if (nic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                {
                    isWifi = true;
                    if (string.IsNullOrEmpty(networkName))
                    {
                        networkName = !string.IsNullOrWhiteSpace(nic.Description) ? nic.Description : nic.Name;
                    }
                }
            }
        }
        catch
        {
            // Fallback for network inspection errors
        }

        string networkSummary = isConnected
            ? (isEthernet ? "Ethernet Conectado" : (isWifi ? "Wi-Fi Conectado" : "Rede Conectada"))
            : "Sem conexão";

        // 3. Volume Master do Windows & IsMuted (CoreAudio / EndpointVolume)
        int volumePercent = 50;
        bool isMuted = false;

        try
        {
            var endpointVolume = CoreAudioHelper.GetDefaultEndpointVolume(EDataFlow.eRender, ERole.eMultimedia);
            if (endpointVolume != null)
            {
                if (endpointVolume.GetMasterVolumeLevelScalar(out float level) == 0)
                {
                    volumePercent = Math.Clamp((int)Math.Round(level * 100), 0, 100);
                }

                if (endpointVolume.GetMute(out bool mute) == 0)
                {
                    isMuted = mute;
                }
            }
        }
        catch
        {
            // Fallback for endpoint volume query errors
        }

        string volumeSummary = isMuted ? "Mutado" : $"{volumePercent}%";

        return new SystemTrayStatus
        {
            HasBattery = hasBattery,
            BatteryPercent = batteryPercent,
            IsCharging = isCharging,
            BatteryStatusSummary = batterySummary,
            PowerLineStatus = powerLineStr,
            IsNetworkConnected = isConnected,
            IsEthernet = isEthernet,
            IsWifi = isWifi,
            NetworkName = networkName,
            NetworkStatusSummary = networkSummary,
            MasterVolumePercent = volumePercent,
            IsMasterMuted = isMuted,
            VolumeStatusSummary = volumeSummary
        };
    }
}
