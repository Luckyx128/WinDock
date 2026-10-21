using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using WinDock.Models;

namespace WinDock.Services;

public sealed class MediaIntegrationService : IMediaIntegrationService
{
    private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
    private const byte VK_MEDIA_PREV_TRACK = 0xB1;
    private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static readonly HashSet<string> BrowserProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome",
        "msedge",
        "firefox",
        "brave",
        "opera",
        "vivaldi",
        "arc"
    };

    private static readonly Dictionary<string, string> DedicatedPlayerNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Spotify"] = "Spotify",
        ["Music.UI"] = "Media Player",
        ["vlc"] = "VLC",
        ["foobar2000"] = "foobar2000",
        ["AIMP"] = "AIMP",
        ["AppleMusic"] = "Apple Music",
        ["iTunes"] = "iTunes",
        ["TIDAL"] = "TIDAL",
        ["Deezer"] = "Deezer"
    };

    private static readonly string[] BrowserSuffixes =
    {
        " - Google Chrome",
        " - Microsoft\u200B Edge",
        " - Microsoft Edge",
        " — Mozilla Firefox",
        " - Mozilla Firefox",
        " - Brave",
        " - Opera",
        " - Vivaldi",
        " - Arc"
    };

    private static readonly Regex NotificationPrefixRegex = new(@"^\(\d+\)\s*", RegexOptions.Compiled);
    private static readonly Regex EdgeTabCountRegex = new(@"\s+and\s+\d+\s+more\s+pages?.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PtBrTabCountRegex = new(@"\s+e\s+mais\s+\d+\s+p[áa]ginas?.*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private MediaInfo _cachedMedia = new();
    private DateTime _lastPollUtc = DateTime.MinValue;

    public MediaInfo GetCurrentMedia()
    {
        if ((DateTime.UtcNow - _lastPollUtc).TotalMilliseconds < 900)
        {
            return _cachedMedia;
        }

        _lastPollUtc = DateTime.UtcNow;
        _cachedMedia = DetectActiveMedia();
        return _cachedMedia;
    }

    public void PlayPause() => SendMediaKey(VK_MEDIA_PLAY_PAUSE);

    public void NextTrack() => SendMediaKey(VK_MEDIA_NEXT_TRACK);

    public void PreviousTrack() => SendMediaKey(VK_MEDIA_PREV_TRACK);

    private static void SendMediaKey(byte virtualKey)
    {
        try
        {
            keybd_event(virtualKey, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
            keybd_event(virtualKey, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch
        {
            // Ignore input injection failures
        }
    }

    private static MediaInfo DetectActiveMedia()
    {
        MediaInfo? youtubeCandidate = null;
        MediaInfo? playerCandidate = null;

        try
        {
            var pidProcessMap = new Dictionary<uint, string>();

            EnumWindows((hWnd, _) =>
            {
                if (!IsWindowVisible(hWnd))
                {
                    return true;
                }

                int length = GetWindowTextLength(hWnd);
                if (length <= 0)
                {
                    return true;
                }

                var sb = new StringBuilder(length + 1);
                if (GetWindowText(hWnd, sb, sb.Capacity) <= 0)
                {
                    return true;
                }

                string rawTitle = sb.ToString().Trim();
                if (string.IsNullOrWhiteSpace(rawTitle))
                {
                    return true;
                }

                GetWindowThreadProcessId(hWnd, out uint pid);
                if (pid == 0)
                {
                    return true;
                }

                if (!pidProcessMap.TryGetValue(pid, out string? processName))
                {
                    processName = TryGetProcessName((int)pid);
                    pidProcessMap[pid] = processName;
                }

                if (string.IsNullOrEmpty(processName))
                {
                    return true;
                }

                if (BrowserProcesses.Contains(processName))
                {
                    if (TryParseYouTubeTitle(rawTitle, out string ytTitle, out string sourceLabel))
                    {
                        youtubeCandidate = new MediaInfo
                        {
                            HasActiveMedia = true,
                            Title = ytTitle,
                            SourceApp = sourceLabel,
                            IsYouTube = true
                        };
                        return false; // Prioritize active YouTube media
                    }
                }
                else if (playerCandidate == null && DedicatedPlayerNames.TryGetValue(processName, out string? friendlyPlayer))
                {
                    if (TryParseDedicatedPlayerTitle(processName, rawTitle, out string trackTitle))
                    {
                        playerCandidate = new MediaInfo
                        {
                            HasActiveMedia = true,
                            Title = trackTitle,
                            SourceApp = friendlyPlayer,
                            IsYouTube = false
                        };
                    }
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
            // Ignore window enumeration exceptions
        }

        if (youtubeCandidate != null)
        {
            return youtubeCandidate;
        }

        if (playerCandidate != null)
        {
            return playerCandidate;
        }

        return new MediaInfo
        {
            HasActiveMedia = false,
            Title = "Nenhuma mídia ativa",
            SourceApp = string.Empty,
            IsYouTube = false
        };
    }

    private static bool TryParseYouTubeTitle(string rawTitle, out string cleanTitle, out string sourceLabel)
    {
        cleanTitle = string.Empty;
        sourceLabel = "YouTube";

        string title = StripBrowserSuffix(rawTitle);
        title = NotificationPrefixRegex.Replace(title, string.Empty).Trim();

        if (title.EndsWith(" - YouTube Music", StringComparison.OrdinalIgnoreCase))
        {
            cleanTitle = title[..^" - YouTube Music".Length].Trim();
            sourceLabel = "YouTube Music";
            return !string.IsNullOrWhiteSpace(cleanTitle);
        }

        if (title.EndsWith(" - YouTube", StringComparison.OrdinalIgnoreCase))
        {
            cleanTitle = title[..^" - YouTube".Length].Trim();
            sourceLabel = "YouTube";
            return !string.IsNullOrWhiteSpace(cleanTitle);
        }

        int ytMusicIndex = title.IndexOf(" - YouTube Music", StringComparison.OrdinalIgnoreCase);
        if (ytMusicIndex > 0)
        {
            cleanTitle = title[..ytMusicIndex].Trim();
            sourceLabel = "YouTube Music";
            return !string.IsNullOrWhiteSpace(cleanTitle);
        }

        int ytIndex = title.IndexOf(" - YouTube", StringComparison.OrdinalIgnoreCase);
        if (ytIndex > 0)
        {
            cleanTitle = title[..ytIndex].Trim();
            sourceLabel = "YouTube";
            return !string.IsNullOrWhiteSpace(cleanTitle);
        }

        if (string.Equals(title, "YouTube", StringComparison.OrdinalIgnoreCase))
        {
            cleanTitle = "YouTube (Aberto)";
            sourceLabel = "YouTube";
            return true;
        }

        return false;
    }

    private static string StripBrowserSuffix(string title)
    {
        string working = title;
        foreach (string suffix in BrowserSuffixes)
        {
            int idx = working.LastIndexOf(suffix, StringComparison.OrdinalIgnoreCase);
            if (idx > 0)
            {
                working = working[..idx].Trim();
                break;
            }
        }

        working = EdgeTabCountRegex.Replace(working, string.Empty).Trim();
        working = PtBrTabCountRegex.Replace(working, string.Empty).Trim();
        return working;
    }

    private static bool TryParseDedicatedPlayerTitle(string processName, string rawTitle, out string trackTitle)
    {
        trackTitle = string.Empty;

        if (processName.Equals("Spotify", StringComparison.OrdinalIgnoreCase))
        {
            if (rawTitle.Equals("Spotify", StringComparison.OrdinalIgnoreCase) ||
                rawTitle.Equals("Spotify Free", StringComparison.OrdinalIgnoreCase) ||
                rawTitle.Equals("Spotify Premium", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            trackTitle = rawTitle;
            return true;
        }

        if (processName.Equals("vlc", StringComparison.OrdinalIgnoreCase))
        {
            const string vlcSuffix = " - VLC media player";
            if (rawTitle.EndsWith(vlcSuffix, StringComparison.OrdinalIgnoreCase))
            {
                trackTitle = rawTitle[..^vlcSuffix.Length].Trim();
                return !string.IsNullOrWhiteSpace(trackTitle);
            }
            return false;
        }

        if (rawTitle.Contains(" - ", StringComparison.Ordinal))
        {
            trackTitle = rawTitle;
            return true;
        }

        return false;
    }

    private static string TryGetProcessName(int pid)
    {
        try
        {
            using Process proc = Process.GetProcessById(pid);
            return proc.ProcessName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
