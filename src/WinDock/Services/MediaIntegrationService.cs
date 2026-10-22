using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;
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

    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private readonly object _managerLock = new();

    private string? _focusedSessionId;
    private MediaPlaybackStatus _cachedStatus = new();
    private MediaInfo _cachedMedia = new();
    private DateTime _lastPollUtc = DateTime.MinValue;

    private string _cachedThumbnailKey = string.Empty;
    private ImageSource? _cachedThumbnail;

    public MediaInfo GetCurrentMedia()
    {
        if ((DateTime.UtcNow - _lastPollUtc).TotalMilliseconds < 500)
        {
            return _cachedMedia;
        }

        _lastPollUtc = DateTime.UtcNow;
        _cachedStatus = FetchPlaybackStatus();
        _cachedMedia = ConvertStatusToMediaInfo(_cachedStatus);
        return _cachedMedia;
    }

    public MediaPlaybackStatus GetPlaybackStatus()
    {
        if ((DateTime.UtcNow - _lastPollUtc).TotalMilliseconds < 500)
        {
            return _cachedStatus;
        }

        _lastPollUtc = DateTime.UtcNow;
        _cachedStatus = FetchPlaybackStatus();
        _cachedMedia = ConvertStatusToMediaInfo(_cachedStatus);
        return _cachedStatus;
    }

    public IReadOnlyList<MediaPlaybackStatus> GetActiveSessions()
    {
        try
        {
            var manager = EnsureSessionManager();
            if (manager == null) return Array.Empty<MediaPlaybackStatus>();

            var sessions = manager.GetSessions();
            if (sessions == null || sessions.Count == 0) return Array.Empty<MediaPlaybackStatus>();

            var list = new List<MediaPlaybackStatus>(sessions.Count);
            foreach (var session in sessions)
            {
                var status = ExtractSessionStatus(session);
                if (status != null && status.HasActiveMedia)
                {
                    list.Add(status);
                }
            }
            return list;
        }
        catch
        {
            return Array.Empty<MediaPlaybackStatus>();
        }
    }

    public void SwitchSession(string sessionId)
    {
        _focusedSessionId = sessionId;
        _lastPollUtc = DateTime.MinValue;
    }

    public void NextSession()
    {
        var sessions = GetActiveSessions();
        if (sessions.Count <= 1) return;

        int currentIndex = -1;
        if (!string.IsNullOrEmpty(_focusedSessionId))
        {
            currentIndex = sessions.ToList().FindIndex(s => string.Equals(s.SessionId, _focusedSessionId, StringComparison.OrdinalIgnoreCase));
        }

        int nextIndex = (currentIndex + 1) % sessions.Count;
        _focusedSessionId = sessions[nextIndex].SessionId;
        _lastPollUtc = DateTime.MinValue;
    }

    public void PlayPause()
    {
        var session = FindTargetSession();
        if (session != null)
        {
            try
            {
                session.TryTogglePlayPauseAsync().AsTask().GetAwaiter().GetResult();
                _lastPollUtc = DateTime.MinValue;
                return;
            }
            catch { }
        }

        SendMediaKey(VK_MEDIA_PLAY_PAUSE);
    }

    public void Play()
    {
        var session = FindTargetSession();
        if (session != null)
        {
            try
            {
                session.TryPlayAsync().AsTask().GetAwaiter().GetResult();
                _lastPollUtc = DateTime.MinValue;
                return;
            }
            catch { }
        }

        SendMediaKey(VK_MEDIA_PLAY_PAUSE);
    }

    public void Pause()
    {
        var session = FindTargetSession();
        if (session != null)
        {
            try
            {
                session.TryPauseAsync().AsTask().GetAwaiter().GetResult();
                _lastPollUtc = DateTime.MinValue;
                return;
            }
            catch { }
        }

        SendMediaKey(VK_MEDIA_PLAY_PAUSE);
    }

    public void Stop()
    {
        var session = FindTargetSession();
        if (session != null)
        {
            try
            {
                session.TryStopAsync().AsTask().GetAwaiter().GetResult();
                _lastPollUtc = DateTime.MinValue;
                return;
            }
            catch { }
        }

        SendMediaKey(VK_MEDIA_PLAY_PAUSE);
    }

    public void NextTrack()
    {
        var session = FindTargetSession();
        if (session != null)
        {
            try
            {
                session.TrySkipNextAsync().AsTask().GetAwaiter().GetResult();
                _lastPollUtc = DateTime.MinValue;
                return;
            }
            catch { }
        }

        SendMediaKey(VK_MEDIA_NEXT_TRACK);
    }

    public void PreviousTrack()
    {
        var session = FindTargetSession();
        if (session != null)
        {
            try
            {
                session.TrySkipPreviousAsync().AsTask().GetAwaiter().GetResult();
                _lastPollUtc = DateTime.MinValue;
                return;
            }
            catch { }
        }

        SendMediaKey(VK_MEDIA_PREV_TRACK);
    }

    private GlobalSystemMediaTransportControlsSessionManager? EnsureSessionManager()
    {
        if (_sessionManager != null) return _sessionManager;

        lock (_managerLock)
        {
            if (_sessionManager != null) return _sessionManager;

            try
            {
                _sessionManager = Task.Run(async () =>
                    await GlobalSystemMediaTransportControlsSessionManager.RequestAsync()
                ).GetAwaiter().GetResult();
            }
            catch
            {
                // GSMTC initialization fallback
            }

            return _sessionManager;
        }
    }

    private GlobalSystemMediaTransportControlsSession? FindTargetSession()
    {
        try
        {
            var manager = EnsureSessionManager();
            if (manager == null) return null;

            var sessions = manager.GetSessions();
            if (sessions == null || sessions.Count == 0) return null;

            if (!string.IsNullOrEmpty(_focusedSessionId))
            {
                var match = sessions.FirstOrDefault(s => string.Equals(s.SourceAppUserModelId, _focusedSessionId, StringComparison.OrdinalIgnoreCase));
                if (match != null) return match;
            }

            var current = manager.GetCurrentSession();
            if (current != null) return current;

            // Prioritize playing session
            var playing = sessions.FirstOrDefault(s =>
            {
                try
                {
                    return s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                }
                catch { return false; }
            });

            return playing ?? sessions.FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private MediaPlaybackStatus FetchPlaybackStatus()
    {
        try
        {
            var session = FindTargetSession();
            if (session != null)
            {
                var status = ExtractSessionStatus(session);
                if (status != null && status.HasActiveMedia)
                {
                    return status;
                }
            }
        }
        catch
        {
        }

        // Fallback: detecção precisa via janelas ativas (YouTube no Chrome/Edge/Brave e players dedicados)
        var fallbackInfo = DetectActiveMediaFromWindows();
        return new MediaPlaybackStatus
        {
            HasActiveMedia = fallbackInfo.HasActiveMedia,
            Title = fallbackInfo.Title,
            Artist = string.Empty,
            SourceApp = fallbackInfo.SourceApp,
            IsYouTube = fallbackInfo.IsYouTube,
            PlaybackStatus = fallbackInfo.HasActiveMedia ? "Playing" : "Stopped",
            IsPlaying = fallbackInfo.HasActiveMedia,
            Position = TimeSpan.Zero,
            Duration = TimeSpan.Zero,
            Thumbnail = null,
            SessionId = string.Empty
        };
    }

    private MediaPlaybackStatus? ExtractSessionStatus(GlobalSystemMediaTransportControlsSession session)
    {
        try
        {
            string sourceAppId = session.SourceAppUserModelId ?? string.Empty;
            string friendlyApp = ResolveFriendlyAppName(sourceAppId);

            var mediaProps = Task.Run(async () =>
                await session.TryGetMediaPropertiesAsync()
            ).GetAwaiter().GetResult();

            if (mediaProps == null) return null;

            string rawTitle = mediaProps.Title?.Trim() ?? string.Empty;
            string rawArtist = mediaProps.Artist?.Trim() ?? string.Empty;
            string rawAlbum = mediaProps.AlbumTitle?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(rawTitle) && string.IsNullOrWhiteSpace(rawArtist))
            {
                return null;
            }

            var playbackInfo = session.GetPlaybackInfo();
            var playbackStatus = playbackInfo?.PlaybackStatus ?? GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed;
            bool isPlaying = playbackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            var timeline = session.GetTimelineProperties();
            TimeSpan position = timeline?.Position ?? TimeSpan.Zero;
            TimeSpan startTime = timeline?.StartTime ?? TimeSpan.Zero;
            TimeSpan endTime = timeline?.EndTime ?? TimeSpan.Zero;
            TimeSpan duration = (endTime > startTime) ? (endTime - startTime) : endTime;
            if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;

            // Determina se é YouTube
            bool isYouTube = IsSessionYouTube(sourceAppId, friendlyApp, rawTitle, rawArtist, rawAlbum);

            string displayTitle = rawTitle;
            if (isYouTube)
            {
                displayTitle = CleanYouTubeTitle(rawTitle);
            }

            // Extrai Thumbnail real via IRandomAccessStreamWithContentType
            string thumbnailKey = $"{sourceAppId}|{rawTitle}|{rawArtist}";
            ImageSource? thumbnail = ExtractThumbnail(mediaProps.Thumbnail, thumbnailKey);

            return new MediaPlaybackStatus
            {
                SessionId = sourceAppId,
                Title = !string.IsNullOrWhiteSpace(displayTitle) ? displayTitle : "Mídia em reprodução",
                Artist = rawArtist,
                AlbumTitle = rawAlbum,
                SourceApp = isYouTube ? "YouTube" : friendlyApp,
                HasActiveMedia = true,
                IsYouTube = isYouTube,
                PlaybackStatus = playbackStatus.ToString(),
                IsPlaying = isPlaying,
                Position = position,
                Duration = duration,
                Thumbnail = thumbnail
            };
        }
        catch
        {
            return null;
        }
    }

    private ImageSource? ExtractThumbnail(IRandomAccessStreamReference? thumbnailRef, string cacheKey)
    {
        if (thumbnailRef == null) return null;

        if (_cachedThumbnail != null && string.Equals(_cachedThumbnailKey, cacheKey, StringComparison.Ordinal))
        {
            return _cachedThumbnail;
        }

        try
        {
            return Task.Run(async () =>
            {
                using IRandomAccessStreamWithContentType streamWithContent = await thumbnailRef.OpenReadAsync();
                if (streamWithContent == null || streamWithContent.Size == 0) return null;

                using var netStream = streamWithContent.AsStreamForRead();
                using var ms = new MemoryStream();
                await netStream.CopyToAsync(ms);
                if (ms.Length == 0) return null;

                ms.Position = 0;
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CreateOptions = BitmapCreateOptions.None;
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = ms;
                bitmap.EndInit();
                bitmap.Freeze();

                _cachedThumbnailKey = cacheKey;
                _cachedThumbnail = bitmap;
                return (ImageSource)bitmap;
            }).GetAwaiter().GetResult();
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSessionYouTube(string appId, string friendlyApp, string title, string artist, string album)
    {
        if (title.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ||
            artist.Contains("YouTube", StringComparison.OrdinalIgnoreCase) ||
            album.Contains("YouTube", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        bool isBrowser = BrowserProcesses.Any(b => appId.Contains(b, StringComparison.OrdinalIgnoreCase) ||
                                                   friendlyApp.Contains(b, StringComparison.OrdinalIgnoreCase));
        if (isBrowser)
        {
            if (title.EndsWith(" - YouTube", StringComparison.OrdinalIgnoreCase) ||
                title.EndsWith(" - YouTube Music", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string CleanYouTubeTitle(string title)
    {
        string cleaned = StripBrowserSuffix(title);
        cleaned = NotificationPrefixRegex.Replace(cleaned, string.Empty).Trim();

        const string ytMusic = " - YouTube Music";
        if (cleaned.EndsWith(ytMusic, StringComparison.OrdinalIgnoreCase))
        {
            return cleaned[..^ytMusic.Length].Trim();
        }

        const string yt = " - YouTube";
        if (cleaned.EndsWith(yt, StringComparison.OrdinalIgnoreCase))
        {
            return cleaned[..^yt.Length].Trim();
        }

        return cleaned;
    }

    private static string ResolveFriendlyAppName(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return "Mídia";

        if (appId.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "Google Chrome";
        if (appId.Contains("msedge", StringComparison.OrdinalIgnoreCase) || appId.Contains("edge", StringComparison.OrdinalIgnoreCase)) return "Microsoft Edge";
        if (appId.Contains("brave", StringComparison.OrdinalIgnoreCase)) return "Brave";
        if (appId.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return "Firefox";
        if (appId.Contains("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        if (appId.Contains("vlc", StringComparison.OrdinalIgnoreCase)) return "VLC";
        if (appId.Contains("zunemusic", StringComparison.OrdinalIgnoreCase) || appId.Contains("media player", StringComparison.OrdinalIgnoreCase)) return "Media Player";
        if (appId.Contains("applemusic", StringComparison.OrdinalIgnoreCase)) return "Apple Music";
        if (appId.Contains("itunes", StringComparison.OrdinalIgnoreCase)) return "iTunes";
        if (appId.Contains("opera", StringComparison.OrdinalIgnoreCase)) return "Opera";
        if (appId.Contains("vivaldi", StringComparison.OrdinalIgnoreCase)) return "Vivaldi";

        foreach (var kvp in DedicatedPlayerNames)
        {
            if (appId.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
            {
                return kvp.Value;
            }
        }

        return Path.GetFileNameWithoutExtension(appId);
    }

    private static MediaInfo ConvertStatusToMediaInfo(MediaPlaybackStatus status)
    {
        return new MediaInfo
        {
            HasActiveMedia = status.HasActiveMedia,
            Title = status.Title,
            Artist = status.Artist,
            SourceApp = status.SourceApp,
            IsYouTube = status.IsYouTube,
            PlaybackStatus = status.PlaybackStatus,
            IsPlaying = status.IsPlaying,
            Position = status.Position,
            Duration = status.Duration,
            Thumbnail = status.Thumbnail,
            SessionId = status.SessionId
        };
    }

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

    private static MediaInfo DetectActiveMediaFromWindows()
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
                            IsYouTube = true,
                            PlaybackStatus = "Playing",
                            IsPlaying = true
                        };
                        return false;
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
                            IsYouTube = false,
                            PlaybackStatus = "Playing",
                            IsPlaying = true
                        };
                    }
                }

                return true;
            }, IntPtr.Zero);
        }
        catch
        {
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
            IsYouTube = false,
            PlaybackStatus = "Stopped",
            IsPlaying = false
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
