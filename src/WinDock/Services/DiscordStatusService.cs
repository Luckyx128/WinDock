using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using WinDock.Models;

namespace WinDock.Services;

public sealed class DiscordStatusService : IDiscordStatusService
{
    private const int SW_SHOW = 5;
    private const int SW_RESTORE = 9;

    private static readonly string[] DiscordProcessNames =
    {
        "Discord",
        "DiscordPTB",
        "DiscordCanary",
        "Vesktop"
    };

    public DiscordStatus GetStatus()
    {
        bool isIpcAvailable = CheckDiscordIpcPipe();
        (bool isRunning, IntPtr mainWindowHandle, string windowTitle) = FindDiscordProcessAndWindow();

        if (!isRunning && !isIpcAvailable)
        {
            return new DiscordStatus
            {
                IsRunning = false,
                IsIpcAvailable = false,
                ActivitySummary = "Offline",
                MainWindowHandle = IntPtr.Zero
            };
        }

        string activity = BuildActivitySummary(windowTitle, isIpcAvailable);

        return new DiscordStatus
        {
            IsRunning = isRunning || isIpcAvailable,
            IsIpcAvailable = isIpcAvailable,
            ActivitySummary = activity,
            MainWindowHandle = mainWindowHandle
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

    private static string BuildActivitySummary(string windowTitle, bool isIpcAvailable)
    {
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
}
