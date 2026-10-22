using System;
using System.Collections.Generic;
using System.Windows;

namespace WinDock.Models;

public interface IAppBarService
{
    void RegisterBar(Window window, double heightDip, BarEdge edge = BarEdge.Bottom);
    void UpdateBarPosition(Window window, double heightDip, BarEdge edge = BarEdge.Bottom);
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
    void ActivateWindow(IntPtr handle);
    void MinimizeWindow(IntPtr handle);
    bool IsForegroundWindowFullscreen();
    bool IsForegroundWindowFullscreen(IntPtr excludeWindowHandle);
    ImageSource? ExtractIconForPath(string filePath);
}

public interface IDockSettingsService
{
    DockSettings CurrentSettings { get; }
    IReadOnlyList<DockTheme> AvailableThemes { get; }
    event EventHandler<DockSettings>? SettingsChanged;
    void LoadSettings();
    void SaveSettings();
    void UpdateSettings(DockSettings settings);
    void ResetToDefaults();
    void AddShortcut(DockShortcut shortcut);
    void RemoveShortcut(string shortcutId);
    void SetWidgetSize(string widgetName, WidgetSize size);
    void SetTheme(DockTheme theme);
}

public interface IHardwareMonitorService
{
    HardwareMetrics GetCurrentMetrics();
}

public interface IMediaIntegrationService
{
    MediaInfo GetCurrentMedia();
    MediaPlaybackStatus GetPlaybackStatus();
    IReadOnlyList<MediaPlaybackStatus> GetActiveSessions();
    void SwitchSession(string sessionId);
    void NextSession();
    void PlayPause();
    void Play();
    void Pause();
    void Stop();
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
    void ToggleMute();
    void ToggleDeafen();
    void DisconnectVoice();
    void OpenDMs();
    void OpenSettings();
    void SetMicrophoneMute(bool isMuted);
    void SetDiscordDeafen(bool isDeafened);
}

public interface ISystemStatusService
{
    SystemTrayStatus GetSystemTrayStatus();
    void SetMasterVolume(int percent);
    void ToggleMasterMute();
    void SetMasterMute(bool isMuted);
    void VolumeUp(int stepPercent = 2);
    void VolumeDown(int stepPercent = 2);
}
