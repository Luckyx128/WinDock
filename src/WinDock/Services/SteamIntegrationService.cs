using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WinDock.Models;

namespace WinDock.Services;

public sealed class SteamIntegrationService : ISteamIntegrationService
{
    private const string SteamRegistryPath = @"Software\Valve\Steam";
    private const string SteamActiveProcessRegistryPath = @"Software\Valve\Steam\ActiveProcess";

    private static readonly Regex AcfKeyValueRegex = new(
        "\"(?<key>[^\"]+)\"\\s+\"(?<val>[^\"]*)\"",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private IReadOnlyList<SteamGameInfo> _cachedGames = Array.Empty<SteamGameInfo>();
    private DateTime _lastGamesScanUtc = DateTime.MinValue;
    private string? _cachedSteamPath;

    public SteamStatus GetStatus()
    {
        try
        {
            string? steamPath = GetSteamInstallPath();
            bool isInstalled = !string.IsNullOrWhiteSpace(steamPath) && Directory.Exists(steamPath);

            if (!isInstalled)
            {
                return new SteamStatus
                {
                    IsInstalled = false,
                    IsRunning = false,
                    RunningAppId = 0,
                    RunningGameName = string.Empty,
                    InstalledGames = Array.Empty<SteamGameInfo>()
                };
            }

            bool isRunning = IsSteamClientRunning();
            int runningAppId = GetRunningAppId();

            if ((DateTime.UtcNow - _lastGamesScanUtc).TotalSeconds >= 20 || _cachedGames.Count == 0)
            {
                _cachedGames = ScanInstalledGames(steamPath!);
                _lastGamesScanUtc = DateTime.UtcNow;
            }

            string runningGameName = string.Empty;
            if (runningAppId > 0)
            {
                runningGameName = ResolveGameName(runningAppId, _cachedGames);
            }

            return new SteamStatus
            {
                IsInstalled = true,
                IsRunning = isRunning,
                RunningAppId = runningAppId,
                RunningGameName = runningGameName,
                InstalledGames = _cachedGames
            };
        }
        catch
        {
            return new SteamStatus();
        }
    }

    public void LaunchGame(int appId)
    {
        if (appId <= 0)
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"steam://rungameid/{appId}",
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore launch failures
        }
    }

    public void OpenSteam()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "steam://open/main",
                UseShellExecute = true
            });
        }
        catch
        {
            // Fallback to executable path if protocol fails
            try
            {
                string? steamPath = GetSteamInstallPath();
                if (!string.IsNullOrWhiteSpace(steamPath))
                {
                    string exe = Path.Combine(steamPath, "steam.exe");
                    if (File.Exists(exe))
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = exe,
                            UseShellExecute = true
                        });
                    }
                }
            }
            catch
            {
                // Ignore fallback errors
            }
        }
    }

    private string? GetSteamInstallPath()
    {
        if (!string.IsNullOrWhiteSpace(_cachedSteamPath) && Directory.Exists(_cachedSteamPath))
        {
            return _cachedSteamPath;
        }

        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SteamRegistryPath);
            string? path = key?.GetValue("SteamPath") as string;
            if (!string.IsNullOrWhiteSpace(path))
            {
                string normalized = path.Replace('/', Path.DirectorySeparatorChar);
                if (Directory.Exists(normalized))
                {
                    _cachedSteamPath = normalized;
                    return _cachedSteamPath;
                }
            }
        }
        catch
        {
            // Ignore registry errors
        }

        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam");
            string? path = key?.GetValue("InstallPath") as string;
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                _cachedSteamPath = path;
                return _cachedSteamPath;
            }
        }
        catch
        {
            // Ignore registry errors
        }

        return null;
    }

    private static bool IsSteamClientRunning()
    {
        try
        {
            using RegistryKey? activeKey = Registry.CurrentUser.OpenSubKey(SteamActiveProcessRegistryPath);
            if (activeKey?.GetValue("pid") is int pid && pid > 0)
            {
                try
                {
                    using Process proc = Process.GetProcessById(pid);
                    if (!proc.HasExited)
                    {
                        return true;
                    }
                }
                catch
                {
                    // Process with recorded PID is not running
                }
            }

            Process[] steamProcs = Process.GetProcessesByName("steam");
            bool anyRunning = steamProcs.Length > 0;
            foreach (Process p in steamProcs)
            {
                p.Dispose();
            }
            return anyRunning;
        }
        catch
        {
            return false;
        }
    }

    private static int GetRunningAppId()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SteamRegistryPath);
            if (key?.GetValue("RunningAppID") is int appId && appId > 0)
            {
                return appId;
            }
        }
        catch
        {
            // Ignore registry access error
        }

        return 0;
    }

    private static string ResolveGameName(int appId, IReadOnlyList<SteamGameInfo> installedGames)
    {
        SteamGameInfo? match = installedGames.FirstOrDefault(g => g.AppId == appId);
        if (match != null && !string.IsNullOrWhiteSpace(match.Name))
        {
            return match.Name;
        }

        try
        {
            using RegistryKey? appKey = Registry.CurrentUser.OpenSubKey($@"{SteamRegistryPath}\Apps\{appId}");
            string? regName = appKey?.GetValue("Name") as string;
            if (!string.IsNullOrWhiteSpace(regName))
            {
                return regName;
            }
        }
        catch
        {
            // Ignore registry lookup errors
        }

        return $"Steam App #{appId}";
    }

    private static IReadOnlyList<SteamGameInfo> ScanInstalledGames(string steamPath)
    {
        var gamesByAppId = new Dictionary<int, SteamGameInfo>();
        var librarySteamAppsDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string primarySteamApps = Path.Combine(steamPath, "steamapps");
        if (Directory.Exists(primarySteamApps))
        {
            librarySteamAppsDirs.Add(primarySteamApps);
            string libraryFoldersVdf = Path.Combine(primarySteamApps, "libraryfolders.vdf");
            if (File.Exists(libraryFoldersVdf))
            {
                foreach (string extraPath in ParseLibraryFolders(libraryFoldersVdf))
                {
                    string extraSteamApps = Path.Combine(extraPath, "steamapps");
                    if (Directory.Exists(extraSteamApps))
                    {
                        librarySteamAppsDirs.Add(extraSteamApps);
                    }
                }
            }
        }

        foreach (string steamAppsDir in librarySteamAppsDirs)
        {
            try
            {
                foreach (string acfFile in Directory.EnumerateFiles(steamAppsDir, "appmanifest_*.acf"))
                {
                    if (TryParseAcfManifest(acfFile, out SteamGameInfo? gameInfo) && gameInfo != null)
                    {
                        // Skip Steamworks Common Redistributables (228980)
                        if (gameInfo.AppId == 228980)
                        {
                            continue;
                        }

                        gamesByAppId[gameInfo.AppId] = gameInfo;
                    }
                }
            }
            catch
            {
                // Ignore inaccessible library folders
            }
        }

        return gamesByAppId.Values
            .OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static IEnumerable<string> ParseLibraryFolders(string vdfFilePath)
    {
        var paths = new List<string>();
        try
        {
            string content = File.ReadAllText(vdfFilePath);
            foreach (Match match in AcfKeyValueRegex.Matches(content))
            {
                string key = match.Groups["key"].Value;
                if (key.Equals("path", StringComparison.OrdinalIgnoreCase))
                {
                    string rawPath = match.Groups["val"].Value.Replace(@"\\", @"\");
                    if (!string.IsNullOrWhiteSpace(rawPath))
                    {
                        paths.Add(rawPath);
                    }
                }
            }
        }
        catch
        {
            // Ignore malformed VDF
        }

        return paths;
    }

    private static bool TryParseAcfManifest(string acfFilePath, out SteamGameInfo? gameInfo)
    {
        gameInfo = null;
        try
        {
            int appId = 0;
            string? name = null;

            foreach (string line in File.ReadLines(acfFilePath))
            {
                Match match = AcfKeyValueRegex.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                string key = match.Groups["key"].Value;
                string val = match.Groups["val"].Value;

                if (key.Equals("appid", StringComparison.OrdinalIgnoreCase))
                {
                    _ = int.TryParse(val, out appId);
                }
                else if (key.Equals("name", StringComparison.OrdinalIgnoreCase))
                {
                    name = val;
                }

                if (appId > 0 && !string.IsNullOrWhiteSpace(name))
                {
                    break;
                }
            }

            if (appId > 0 && !string.IsNullOrWhiteSpace(name))
            {
                gameInfo = new SteamGameInfo
                {
                    AppId = appId,
                    Name = name.Trim()
                };
                return true;
            }
        }
        catch
        {
            // Ignore locked or corrupted manifest files
        }

        return false;
    }
}
