using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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

    private static readonly ConcurrentDictionary<string, BitmapImage?> _bitmapCache = new(StringComparer.OrdinalIgnoreCase);
    private static ImageSource? _cachedSteamClientIcon;

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
                    PersonaName = "Steam",
                    SteamClientIcon = null,
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

            string personaName = ResolvePersonaName(steamPath!);
            ImageSource? clientIcon = ResolveSteamClientIcon(steamPath!);

            var lastPlayed = _cachedGames
                .Where(g => g.LastPlayedUnix > 0)
                .OrderByDescending(g => g.LastPlayedUnix)
                .FirstOrDefault() ?? _cachedGames.FirstOrDefault();

            var activeDownload = _cachedGames.FirstOrDefault(g => g.IsDownloading);

            var largest = _cachedGames
                .OrderByDescending(g => g.SizeOnDiskBytes)
                .FirstOrDefault();

            long totalStorage = _cachedGames.Sum(g => g.SizeOnDiskBytes);

            return new SteamStatus
            {
                IsInstalled = true,
                IsRunning = isRunning,
                RunningAppId = runningAppId,
                RunningGameName = runningGameName,
                PersonaName = personaName,
                SteamClientIcon = clientIcon,
                InstalledGames = _cachedGames,
                LastPlayedGame = lastPlayed,
                ActiveDownloadGame = activeDownload,
                LargestGame = largest,
                TotalStorageBytes = totalStorage
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

    public void OpenSteamUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            // Ignore protocol errors
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

    private static string ResolvePersonaName(string steamPath)
    {
        try
        {
            string vdfPath = Path.Combine(steamPath, "config", "loginusers.vdf");
            if (File.Exists(vdfPath))
            {
                string content = File.ReadAllText(vdfPath);

                var blocks = Regex.Matches(content, @"\{[^{}]*\}", RegexOptions.Singleline);
                foreach (Match block in blocks)
                {
                    string b = block.Value;
                    if (b.Contains("\"MostRecent\"", StringComparison.OrdinalIgnoreCase) &&
                        (b.Contains("\"1\"", StringComparison.OrdinalIgnoreCase) || b.Contains("\"MostRecent\"\t\t\"1\"", StringComparison.OrdinalIgnoreCase)))
                    {
                        var m = Regex.Match(b, "\"PersonaName\"\\s+\"(?<name>[^\"]+)\"", RegexOptions.IgnoreCase);
                        if (m.Success && !string.IsNullOrWhiteSpace(m.Groups["name"].Value))
                        {
                            return m.Groups["name"].Value.Trim();
                        }
                    }
                }

                var anyPersona = Regex.Match(content, "\"PersonaName\"\\s+\"(?<name>[^\"]+)\"", RegexOptions.IgnoreCase);
                if (anyPersona.Success && !string.IsNullOrWhiteSpace(anyPersona.Groups["name"].Value))
                {
                    return anyPersona.Groups["name"].Value.Trim();
                }
            }
        }
        catch
        {
            // Ignore errors
        }

        return "Luckyx";
    }

    private static ImageSource? ResolveSteamClientIcon(string steamPath)
    {
        if (_cachedSteamClientIcon != null)
        {
            return _cachedSteamClientIcon;
        }

        try
        {
            string exe = Path.Combine(steamPath, "steam.exe");
            if (File.Exists(exe))
            {
                using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
                if (sysIcon != null)
                {
                    var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                        sysIcon.Handle,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                    bitmap.Freeze();
                    _cachedSteamClientIcon = bitmap;
                    return _cachedSteamClientIcon;
                }
            }
        }
        catch
        {
            // Ignore icon extraction failure
        }

        return null;
    }

    private static BitmapImage? SafeLoadBitmapImage(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        if (_bitmapCache.TryGetValue(filePath, out var cached))
        {
            return cached;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(filePath);
            using var ms = new MemoryStream(bytes);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = ms;
            bitmap.EndInit();
            bitmap.Freeze();

            _bitmapCache[filePath] = bitmap;
            return bitmap;
        }
        catch
        {
            _bitmapCache[filePath] = null;
            return null;
        }
    }

    private static void ResolveGameArt(
        string steamPath,
        int appId,
        out string? iconPath,
        out string? headerPath,
        out string? logoPath,
        out ImageSource? iconImage,
        out ImageSource? headerImage,
        out ImageSource? logoImage)
    {
        iconPath = null;
        headerPath = null;
        logoPath = null;
        iconImage = null;
        headerImage = null;
        logoImage = null;

        try
        {
            string cacheDir = Path.Combine(steamPath, "appcache", "librarycache", appId.ToString());
            if (Directory.Exists(cacheDir))
            {
                var files = Directory.GetFiles(cacheDir, "*.*", SearchOption.AllDirectories);

                // 1. Logo (logo.png)
                string? logoFile = files.FirstOrDefault(f => Path.GetFileName(f).Equals("logo.png", StringComparison.OrdinalIgnoreCase));
                if (logoFile != null)
                {
                    logoPath = logoFile;
                    logoImage = SafeLoadBitmapImage(logoFile);
                }

                // 2. Header / Banner
                string? headerFile = files.FirstOrDefault(f => Path.GetFileName(f).Equals("header.jpg", StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault(f => Path.GetFileName(f).Equals("library_header.jpg", StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault(f => Path.GetFileName(f).Equals("library_hero.jpg", StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault(f => Path.GetFileName(f).Equals("library_capsule.jpg", StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault(f => Path.GetFileName(f).Equals("library_hero_blur.jpg", StringComparison.OrdinalIgnoreCase));

                if (headerFile != null)
                {
                    headerPath = headerFile;
                    headerImage = SafeLoadBitmapImage(headerFile);
                }

                // 3. Square Icon (<hash>.jpg not starting with library_, header, logo)
                string? iconFile = files.FirstOrDefault(f =>
                {
                    string fn = Path.GetFileName(f);
                    string ext = Path.GetExtension(f);
                    if (!ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".png", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    if (fn.StartsWith("library_", StringComparison.OrdinalIgnoreCase) ||
                        fn.StartsWith("header", StringComparison.OrdinalIgnoreCase) ||
                        fn.StartsWith("logo", StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                    return true;
                });

                iconFile ??= headerFile;

                if (iconFile != null)
                {
                    iconPath = iconFile;
                    iconImage = SafeLoadBitmapImage(iconFile);
                }

                iconImage ??= headerImage;
                headerImage ??= iconImage;
            }
        }
        catch
        {
            // Ignore art resolution errors
        }
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
                    if (TryParseAcfManifest(acfFile, steamPath, out SteamGameInfo? gameInfo) && gameInfo != null)
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
            .OrderByDescending(g => g.LastPlayedUnix)
            .ThenBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase)
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

    private static bool TryParseAcfManifest(string acfFilePath, string steamPath, out SteamGameInfo? gameInfo)
    {
        gameInfo = null;
        try
        {
            int appId = 0;
            string? name = null;
            long lastPlayed = 0;
            long sizeOnDisk = 0;
            int stateFlags = 4;
            long bytesToDownload = 0;
            long bytesDownloaded = 0;

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
                else if (key.Equals("LastPlayed", StringComparison.OrdinalIgnoreCase))
                {
                    _ = long.TryParse(val, out lastPlayed);
                }
                else if (key.Equals("SizeOnDisk", StringComparison.OrdinalIgnoreCase))
                {
                    _ = long.TryParse(val, out sizeOnDisk);
                }
                else if (key.Equals("StateFlags", StringComparison.OrdinalIgnoreCase))
                {
                    _ = int.TryParse(val, out stateFlags);
                }
                else if (key.Equals("BytesToDownload", StringComparison.OrdinalIgnoreCase))
                {
                    _ = long.TryParse(val, out bytesToDownload);
                }
                else if (key.Equals("BytesDownloaded", StringComparison.OrdinalIgnoreCase))
                {
                    _ = long.TryParse(val, out bytesDownloaded);
                }
            }

            if (appId > 0 && !string.IsNullOrWhiteSpace(name))
            {
                ResolveGameArt(
                    steamPath,
                    appId,
                    out string? iconPath,
                    out string? headerPath,
                    out string? logoPath,
                    out ImageSource? iconImage,
                    out ImageSource? headerImage,
                    out ImageSource? logoImage);

                gameInfo = new SteamGameInfo
                {
                    AppId = appId,
                    Name = name.Trim(),
                    LastPlayedUnix = lastPlayed,
                    SizeOnDiskBytes = sizeOnDisk,
                    StateFlags = stateFlags,
                    BytesToDownload = bytesToDownload,
                    BytesDownloaded = bytesDownloaded,
                    IconPath = iconPath,
                    HeaderImagePath = headerPath,
                    LogoImagePath = logoPath,
                    IconImage = iconImage,
                    HeaderImage = headerImage,
                    LogoImage = logoImage
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
