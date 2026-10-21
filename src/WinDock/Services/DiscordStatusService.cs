using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Media;
using Microsoft.Win32;
using WinDock.Models;

namespace WinDock.Services;

public sealed class DiscordStatusService : IDiscordStatusService
{
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    private const byte VK_SHIFT = 0x10;
    private const byte VK_CONTROL = 0x11;
    private const byte VK_M = 0x4D;
    private const byte VK_D = 0x44;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    private static readonly string[] DiscordProcessNames =
    {
        "Discord",
        "DiscordPTB",
        "DiscordCanary",
        "Vesktop"
    };

    private bool _isMuted;
    private bool _isDeafened;
    private ImageSource? _cachedClientIcon;

    public DiscordStatus GetStatus()
    {
        bool isIpcAvailable = CheckDiscordIpcPipe();
        (bool isRunning, IntPtr mainWindowHandle, string windowTitle) = FindDiscordProcessAndWindow();

        (bool isInVoice, TimeSpan duration, string durationFormatted) = CheckMicrophoneVoiceCall();
        (int mentionCount, string channelName, string serverName, string serverInitials) = ParseWindowTitle(windowTitle);

        if (!isRunning && !isIpcAvailable && !isInVoice)
        {
            return new DiscordStatus
            {
                IsRunning = false,
                IsIpcAvailable = false,
                ActivitySummary = "Offline",
                MainWindowHandle = IntPtr.Zero,
                MentionCount = 0,
                ChannelName = string.Empty,
                ServerName = string.Empty,
                ServerInitials = "DC",
                IsInVoiceCall = false,
                VoiceCallDuration = TimeSpan.Zero,
                VoiceCallDurationFormatted = "00:00",
                IsVoiceSpeaking = false,
                IsMuted = _isMuted,
                IsDeafened = _isDeafened,
                DiscordClientIcon = GetOrExtractDiscordClientIcon()
            };
        }

        string activity = BuildActivitySummary(windowTitle, isIpcAvailable, isInVoice);

        return new DiscordStatus
        {
            IsRunning = isRunning || isIpcAvailable || isInVoice,
            IsIpcAvailable = isIpcAvailable,
            ActivitySummary = activity,
            MainWindowHandle = mainWindowHandle,
            MentionCount = mentionCount,
            ChannelName = channelName,
            ServerName = serverName,
            ServerInitials = serverInitials,
            IsInVoiceCall = isInVoice,
            VoiceCallDuration = duration,
            VoiceCallDurationFormatted = durationFormatted,
            IsVoiceSpeaking = isInVoice,
            IsMuted = _isMuted,
            IsDeafened = _isDeafened,
            DiscordClientIcon = GetOrExtractDiscordClientIcon()
        };
    }

    public void FocusOrLaunchDiscord()
    {
        DiscordStatus status = GetStatus();

        if (status.MainWindowHandle != IntPtr.Zero)
        {
            try
            {
                if (IsIconic(status.MainWindowHandle))
                {
                    ShowWindow(status.MainWindowHandle, SW_RESTORE);
                }
                else
                {
                    ShowWindow(status.MainWindowHandle, SW_SHOW);
                }

                SetForegroundWindow(status.MainWindowHandle);
                return;
            }
            catch
            {
                // Fallback to launch/restore via Update.exe
            }
        }

        if (TryLaunchDiscordExecutable())
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "discord://",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore if protocol is not registered
        }
    }

    public void ToggleMute()
    {
        _isMuted = !_isMuted;
        SendGlobalDiscordKeyCombination(VK_M);
    }

    public void ToggleDeafen()
    {
        _isDeafened = !_isDeafened;
        SendGlobalDiscordKeyCombination(VK_D);
    }

    public void DisconnectVoice()
    {
        FocusOrLaunchDiscord();
    }

    public void OpenDMs()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "discord://-/channels/@me",
                UseShellExecute = true
            });
        }
        catch
        {
            FocusOrLaunchDiscord();
        }
    }

    public void OpenSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "discord://-/settings/voice",
                UseShellExecute = true
            });
        }
        catch
        {
            FocusOrLaunchDiscord();
        }
    }

    private static void SendGlobalDiscordKeyCombination(byte vkKey)
    {
        try
        {
            keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
            keybd_event(vkKey, 0, 0, UIntPtr.Zero);

            keybd_event(vkKey, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
        }
        catch
        {
            // Ignore keybd_event errors
        }
    }

    private static (bool IsInVoice, TimeSpan Duration, string DurationFormatted) CheckMicrophoneVoiceCall()
    {
        try
        {
            using var rootKey = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\microphone\NonPackaged");
            if (rootKey == null) return (false, TimeSpan.Zero, "00:00");

            long latestActiveStart = 0;
            foreach (string subKeyName in rootKey.GetSubKeyNames())
            {
                if (!subKeyName.Contains("Discord", StringComparison.OrdinalIgnoreCase)) continue;

                using var subKey = rootKey.OpenSubKey(subKeyName);
                if (subKey == null) continue;

                object? startVal = subKey.GetValue("LastUsedTimeStart");
                object? stopVal = subKey.GetValue("LastUsedTimeStop");

                if (startVal is long start && start > 0)
                {
                    long stop = stopVal is long s ? s : 0;
                    if (stop == 0 || start > stop)
                    {
                        if (start > latestActiveStart)
                        {
                            latestActiveStart = start;
                        }
                    }
                }
            }

            if (latestActiveStart > 0)
            {
                DateTime startTime = DateTime.FromFileTimeUtc(latestActiveStart);
                TimeSpan duration = DateTime.UtcNow - startTime;
                if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
                string formatted = duration.TotalHours >= 1.0
                    ? duration.ToString(@"h\:mm\:ss")
                    : duration.ToString(@"mm\:ss");
                return (true, duration, formatted);
            }
        }
        catch
        {
            // Suppress registry inspection errors
        }

        return (false, TimeSpan.Zero, "00:00");
    }

    private static (int MentionCount, string ChannelName, string ServerName, string ServerInitials) ParseWindowTitle(string windowTitle)
    {
        int mentionCount = 0;
        string channelName = string.Empty;
        string serverName = string.Empty;
        string serverInitials = "DC";

        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return (0, string.Empty, string.Empty, "DC");
        }

        string title = windowTitle.Trim();

        // 1. Mentions e.g. "(3) " or "(99+) "
        if (title.StartsWith("(") && title.IndexOf(')') is int closeParen && closeParen > 1)
        {
            string countStr = title.Substring(1, closeParen - 1);
            if (int.TryParse(countStr, out int count))
            {
                mentionCount = count;
            }
            title = title.Substring(closeParen + 1).Trim();
        }

        // 2. Suffix " - Discord"
        const string suffix = " - Discord";
        if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            title = title.Substring(0, title.Length - suffix.Length).Trim();
        }

        // 3. Channel & Server e.g. "#balela | Casa Corp"
        if (title.Contains('|'))
        {
            string[] parts = title.Split(new[] { '|' }, 2, StringSplitOptions.TrimEntries);
            channelName = parts[0];
            serverName = parts[1];
        }
        else if (!title.Equals("Discord", StringComparison.OrdinalIgnoreCase))
        {
            if (title.StartsWith("#") || title.StartsWith("@"))
            {
                channelName = title;
            }
            else
            {
                serverName = title;
            }
        }

        // 4. Server Initials
        if (!string.IsNullOrWhiteSpace(serverName))
        {
            var words = serverName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length >= 2)
            {
                serverInitials = $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
            }
            else if (serverName.Length >= 2)
            {
                serverInitials = serverName.Substring(0, 2).ToUpperInvariant();
            }
            else
            {
                serverInitials = serverName.ToUpperInvariant();
            }
        }
        else if (!string.IsNullOrWhiteSpace(channelName))
        {
            string cleanCh = channelName.TrimStart('#', '@');
            serverInitials = cleanCh.Length >= 2 ? cleanCh.Substring(0, 2).ToUpperInvariant() : "DC";
        }

        return (mentionCount, channelName, serverName, serverInitials);
    }

    private static bool CheckDiscordIpcPipe()
    {
        try
        {
            for (int i = 0; i <= 9; i++)
            {
                if (File.Exists($@"\\.\pipe\discord-ipc-{i}"))
                {
                    return true;
                }
            }
        }
        catch
        {
            // Ignore pipe check errors
        }

        return false;
    }

    private static (bool IsRunning, IntPtr WindowHandle, string WindowTitle) FindDiscordProcessAndWindow()
    {
        bool anyRunning = false;
        IntPtr bestHandle = IntPtr.Zero;
        string bestTitle = string.Empty;

        foreach (string procName in DiscordProcessNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(procName);
            }
            catch
            {
                continue;
            }

            foreach (Process proc in processes)
            {
                try
                {
                    anyRunning = true;
                    if (proc.MainWindowHandle != IntPtr.Zero && bestHandle == IntPtr.Zero)
                    {
                        bestHandle = proc.MainWindowHandle;
                        bestTitle = proc.MainWindowTitle ?? string.Empty;
                    }
                }
                catch
                {
                    // Ignore process access errors
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }

        if (anyRunning && bestHandle == IntPtr.Zero)
        {
            // Fallback: scan visible top-level windows for a Discord title
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

                string title = sb.ToString();
                if (title.EndsWith(" - Discord", StringComparison.OrdinalIgnoreCase) ||
                    title.Equals("Discord", StringComparison.OrdinalIgnoreCase))
                {
                    bestHandle = hWnd;
                    bestTitle = title;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
        }

        return (anyRunning, bestHandle, bestTitle);
    }

    private static string BuildActivitySummary(string windowTitle, bool isIpcAvailable, bool isInVoice)
    {
        if (isInVoice)
        {
            return "Em Chamada de Voz";
        }

        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            string cleaned = windowTitle.Trim();
            const string suffix = " - Discord";
            if (cleaned.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..^suffix.Length].Trim();
            }

            if (!string.IsNullOrWhiteSpace(cleaned) &&
                !cleaned.Equals("Discord", StringComparison.OrdinalIgnoreCase))
            {
                return cleaned;
            }
        }

        return isIpcAvailable ? "Online (IPC Ativo)" : "Em segundo plano";
    }

    private static bool TryLaunchDiscordExecutable()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        (string Folder, string Exe)[] candidates =
        {
            ("Discord", "Discord.exe"),
            ("DiscordPTB", "DiscordPTB.exe"),
            ("DiscordCanary", "DiscordCanary.exe")
        };

        foreach ((string folder, string exe) in candidates)
        {
            try
            {
                string updateExe = Path.Combine(localAppData, folder, "Update.exe");
                if (File.Exists(updateExe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = updateExe,
                        Arguments = $"--processStart {exe}",
                        UseShellExecute = true
                    });
                    return true;
                }
            }
            catch
            {
                // Try next candidate
            }
        }

        return false;
    }

    private ImageSource? GetOrExtractDiscordClientIcon()
    {
        if (_cachedClientIcon != null) return _cachedClientIcon;

        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string discordBaseDir = Path.Combine(localAppData, "Discord");
            if (Directory.Exists(discordBaseDir))
            {
                var appDirs = Directory.GetDirectories(discordBaseDir, "app-*");
                if (appDirs.Length > 0)
                {
                    Array.Sort(appDirs, StringComparer.OrdinalIgnoreCase);
                    string latestAppDir = appDirs[^1];
                    string discordExe = Path.Combine(latestAppDir, "Discord.exe");
                    if (File.Exists(discordExe))
                    {
                        var icon = ExtractIconFromFile(discordExe);
                        if (icon != null)
                        {
                            _cachedClientIcon = icon;
                            return icon;
                        }
                    }
                }

                string directExe = Path.Combine(discordBaseDir, "Discord.exe");
                if (File.Exists(directExe))
                {
                    var icon = ExtractIconFromFile(directExe);
                    if (icon != null)
                    {
                        _cachedClientIcon = icon;
                        return icon;
                    }
                }
            }
        }
        catch
        {
            // Suppress icon extraction errors
        }

        return null;
    }

    private static ImageSource? ExtractIconFromFile(string filePath)
    {
        try
        {
            using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(filePath);
            if (sysIcon != null)
            {
                var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    sysIcon.Handle,
                    System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                return bmp;
            }
        }
        catch { }
        return null;
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
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
}
