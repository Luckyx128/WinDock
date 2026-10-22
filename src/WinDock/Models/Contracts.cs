using System;
using System.Collections.Generic;
using System.Windows.Media;

namespace WinDock.Models;

public enum BarEdge
{
    Top = 1,
    Bottom = 3
}

public enum WidgetSize
{
    Size1x1 = 1,
    Size1x2 = 2,
    Size1x3 = 3
}

public sealed class DockShortcut
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;
    public string? IconPath { get; set; }
    public bool IsCustomIcon { get; set; }
}

public sealed class DockTheme
{
    public string Id { get; set; } = "default-dark";
    public string Name { get; set; } = "Dark Modern";
    public string BackgroundColorHex { get; set; } = "#E61A1C23";
    public string BorderColorHex { get; set; } = "#33FFFFFF";
    public string AccentColorHex { get; set; } = "#007ACC";
    public string TextColorHex { get; set; } = "#FFFFFFFF";
    public string CardBackgroundColorHex { get; set; } = "#262A35";
    public double BlurRadius { get; set; } = 20.0;
    public double Opacity { get; set; } = 0.95;
}

public sealed class AppWindowInfo
{
    public IntPtr Handle { get; init; }
    public string Title { get; init; } = string.Empty;
    public string ProcessName { get; init; } = string.Empty;
    public string ExecutablePath { get; init; } = string.Empty;
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

public sealed class MediaPlaybackStatus
{
    public string SessionId { get; init; } = string.Empty;
    public string Title { get; init; } = "Nenhuma mídia ativa";
    public string Artist { get; init; } = string.Empty;
    public string AlbumTitle { get; init; } = string.Empty;
    public string SourceApp { get; init; } = string.Empty;
    public bool HasActiveMedia { get; init; }
    public bool IsYouTube { get; init; }
    public string PlaybackStatus { get; init; } = "Stopped";
    public bool IsPlaying { get; init; }
    public TimeSpan Position { get; init; } = TimeSpan.Zero;
    public TimeSpan Duration { get; init; } = TimeSpan.Zero;
    public ImageSource? Thumbnail { get; init; }
    public double ProgressPercent => Duration.TotalMilliseconds > 0
        ? Math.Clamp(Position.TotalMilliseconds * 100.0 / Duration.TotalMilliseconds, 0.0, 100.0)
        : 0.0;
    public string PositionFormatted => Position.TotalHours >= 1.0
        ? Position.ToString(@"h\:mm\:ss")
        : Position.ToString(@"mm\:ss");
    public string DurationFormatted => Duration.TotalHours >= 1.0
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"mm\:ss");
}

public sealed class MediaInfo
{
    public bool HasActiveMedia { get; init; }
    public string Title { get; init; } = "Nenhuma mídia ativa";
    public string Artist { get; init; } = string.Empty;
    public string SourceApp { get; init; } = string.Empty;
    public bool IsYouTube { get; init; }
    public string PlaybackStatus { get; init; } = "Stopped";
    public bool IsPlaying { get; init; }
    public TimeSpan Position { get; init; } = TimeSpan.Zero;
    public TimeSpan Duration { get; init; } = TimeSpan.Zero;
    public ImageSource? Thumbnail { get; init; }
    public string SessionId { get; init; } = string.Empty;
    public double ProgressPercent => Duration.TotalMilliseconds > 0
        ? Math.Clamp(Position.TotalMilliseconds * 100.0 / Duration.TotalMilliseconds, 0.0, 100.0)
        : 0.0;
    public string PositionFormatted => Position.TotalHours >= 1.0
        ? Position.ToString(@"h\:mm\:ss")
        : Position.ToString(@"mm\:ss");
    public string DurationFormatted => Duration.TotalHours >= 1.0
        ? Duration.ToString(@"h\:mm\:ss")
        : Duration.ToString(@"mm\:ss");
}

public sealed class SystemTrayStatus
{
    public bool HasBattery { get; init; }
    public int BatteryPercent { get; init; } = 100;
    public bool IsCharging { get; init; }
    public string BatteryStatusSummary { get; init; } = "100%";
    public string PowerLineStatus { get; init; } = "Online";

    public bool IsNetworkConnected { get; init; }
    public bool IsEthernet { get; init; }
    public bool IsWifi { get; init; }
    public string NetworkName { get; init; } = string.Empty;
    public string NetworkStatusSummary { get; init; } = "Conectado";

    public int MasterVolumePercent { get; init; } = 50;
    public bool IsMasterMuted { get; init; }
    public string VolumeStatusSummary { get; init; } = "50%";
}

public sealed class SteamGameInfo
{
    public int AppId { get; init; }
    public string Name { get; init; } = string.Empty;
    public long LastPlayedUnix { get; init; }
    public long SizeOnDiskBytes { get; init; }
    public int StateFlags { get; init; } = 4;
    public long BytesToDownload { get; init; }
    public long BytesDownloaded { get; init; }
    public string? IconPath { get; init; }
    public string? HeaderImagePath { get; init; }
    public string? LogoImagePath { get; init; }
    public ImageSource? IconImage { get; init; }
    public ImageSource? HeaderImage { get; init; }
    public ImageSource? LogoImage { get; init; }

    public bool IsDownloading => StateFlags != 4 && BytesToDownload > 0 && BytesDownloaded < BytesToDownload;
    public double DownloadPercent => BytesToDownload > 0 ? Math.Clamp(BytesDownloaded * 100.0 / BytesToDownload, 0.0, 100.0) : 100.0;
    public string SizeFormatted
    {
        get
        {
            if (SizeOnDiskBytes <= 0) return "0 GB";
            double gb = SizeOnDiskBytes / (1024.0 * 1024.0 * 1024.0);
            return gb >= 1.0 ? $"{gb:0.0} GB" : $"{SizeOnDiskBytes / (1024.0 * 1024.0):0} MB";
        }
    }
    public string LastPlayedText
    {
        get
        {
            if (LastPlayedUnix <= 0) return "Nunca jogado";
            var dt = DateTimeOffset.FromUnixTimeSeconds(LastPlayedUnix).ToLocalTime();
            var diff = DateTimeOffset.Now - dt;
            if (diff.TotalMinutes < 60) return "Hoje";
            if (diff.TotalHours < 24) return "Hoje";
            if (diff.TotalDays < 2) return "Ontem";
            if (diff.TotalDays < 30) return $"Há {(int)diff.TotalDays} dias";
            return dt.ToString("dd/MM/yyyy");
        }
    }
}

public sealed class SteamStatus
{
    public bool IsInstalled { get; init; }
    public bool IsRunning { get; init; }
    public int RunningAppId { get; init; }
    public string RunningGameName { get; init; } = string.Empty;
    public string PersonaName { get; init; } = "Luckyx";
    public ImageSource? SteamClientIcon { get; init; }
    public IReadOnlyList<SteamGameInfo> InstalledGames { get; init; } = Array.Empty<SteamGameInfo>();
    public SteamGameInfo? LastPlayedGame { get; init; }
    public SteamGameInfo? ActiveDownloadGame { get; init; }
    public SteamGameInfo? LargestGame { get; init; }
    public long TotalStorageBytes { get; init; }
    public string TotalStorageFormatted
    {
        get
        {
            if (TotalStorageBytes <= 0) return "0 GB";
            double gb = TotalStorageBytes / (1024.0 * 1024.0 * 1024.0);
            return $"{gb:0.0} GB";
        }
    }
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
    public int MentionCount { get; init; }
    public string ChannelName { get; init; } = string.Empty;
    public string ServerName { get; init; } = string.Empty;
    public string ServerInitials { get; init; } = "DC";
    public bool IsInVoiceCall { get; init; }
    public TimeSpan VoiceCallDuration { get; init; } = TimeSpan.Zero;
    public string VoiceCallDurationFormatted { get; init; } = "00:00";
    public bool IsVoiceSpeaking { get; init; }
    public bool IsMuted { get; init; }
    public bool IsDeafened { get; init; }
    public ImageSource? DiscordClientIcon { get; init; }
}
