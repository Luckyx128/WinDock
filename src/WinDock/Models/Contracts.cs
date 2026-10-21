using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace WinDock.Models;

public enum BarEdge
{
    Top = 1,
    Bottom = 3
}

public sealed class AppWindowInfo
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public ImageSource? Icon { get; init; }
    public bool IsFocused { get; init; }
    public bool IsMinimized { get; init; }
}

public sealed class HardwareMetrics
{
    public double CpuPercent { get; init; }
    public double GpuPercent { get; init; }
    public double RamPercent { get; init; }
    public double RamUsedGb { get; init; }
    public double RamTotalGb { get; init; }
}

public sealed class MediaInfo
{
    public bool HasActiveMedia { get; init; }
    public string Title { get; init; } = "Nenhuma mídia ativa";
    public string SourceApp { get; init; } = string.Empty;
    public bool IsYouTube { get; init; }
}

public sealed class SteamGameInfo
{
    public int AppId { get; init; }
    public string Name { get; init; } = string.Empty;
}

public sealed class SteamStatus
{
    public bool IsInstalled { get; init; }
    public bool IsRunning { get; init; }
    public int RunningAppId { get; init; }
    public string RunningGameName { get; init; } = string.Empty;
    public IReadOnlyList<SteamGameInfo> InstalledGames { get; init; } = Array.Empty<SteamGameInfo>();
}

public sealed class AiProviderUsage
{
    public string ProviderName { get; init; } = string.Empty;
    public int SessionsToday { get; init; }
    public int InteractionsToday { get; init; }
    public long StorageKb { get; init; }
    public bool IsActiveNow { get; init; }
}

public sealed class AiUsageSnapshot
{
    public int TotalInteractionsToday { get; init; }
    public string ActiveModelSummary { get; init; } = "Inativo";
    public IReadOnlyList<AiProviderUsage> Providers { get; init; } = Array.Empty<AiProviderUsage>();
}

public sealed class DiscordStatus
{
    public bool IsRunning { get; init; }
    public bool IsIpcAvailable { get; init; }
    public string ActivitySummary { get; init; } = "Offline";
    public IntPtr MainWindowHandle { get; init; }
}
