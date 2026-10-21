using System;
using System.Collections.Generic;
using System.Windows;

namespace WinDock.Models;

public interface IAppBarService
{
    void RegisterBar(Window window, double heightDip, BarEdge edge = BarEdge.Top);
    void UpdateBarPosition(Window window, double heightDip, BarEdge edge = BarEdge.Top);
    void UnregisterBar(Window window);
    void PinToDesktop(Window widgetWindow);
}

public interface INativeTaskbarService
{
    bool IsNativeTaskbarHidden { get; }
    void HideNativeTaskbar();
    void ShowNativeTaskbar();
    void ToggleNativeTaskbar();
}

public interface IWindowTrackerService
{
    IReadOnlyList<AppWindowInfo> GetOpenWindows(IntPtr excludeWindowHandle);
    void ToggleOrActivateWindow(IntPtr handle);
}

public interface IHardwareMonitorService
{
    HardwareMetrics GetCurrentMetrics();
}

public interface IMediaIntegrationService
{
    MediaInfo GetCurrentMedia();
    void PlayPause();
    void NextTrack();
    void PreviousTrack();
}

public interface ISteamIntegrationService
{
    SteamStatus GetStatus();
    void LaunchGame(int appId);
    void OpenSteam();
}

public interface IAiUsageService
{
    AiUsageSnapshot GetUsageSnapshot();
}

public interface IDiscordStatusService
{
    DiscordStatus GetStatus();
    void FocusOrLaunchDiscord();
}
