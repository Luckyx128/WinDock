using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using WinDock.Models;

namespace WinDock.Services;

public sealed class AiUsageService : IAiUsageService
{
    private static readonly (string Name, string FolderName, string[] ProcessHints)[] ProviderDefinitions =
    {
        ("Claude", ".claude", new[] { "claude" }),
        ("Codex", ".codex", new[] { "codex" }),
        ("Gemini", ".gemini", new[] { "gemini", "antigravity" })
    };

    private static readonly HashSet<string> IgnoredSubfolders = new(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules",
        "builtin",
        "plugins",
        "bin",
        "cache",
        ".git"
    };

    private static readonly HashSet<string> SessionExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jsonl",
        ".json",
        ".md",
        ".log",
        ".txt"
    };

    private AiUsageSnapshot _cachedSnapshot = new();
    private DateTime _lastScanUtc = DateTime.MinValue;

    public AiUsageSnapshot GetUsageSnapshot()
    {
        if ((DateTime.UtcNow - _lastScanUtc).TotalSeconds < 6)
        {
            return _cachedSnapshot;
        }

        _lastScanUtc = DateTime.UtcNow;
        _cachedSnapshot = BuildSnapshot();
        return _cachedSnapshot;
    }

    private static AiUsageSnapshot BuildSnapshot()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        DateTime today = DateTime.Today;
        DateTime activeThreshold = DateTime.Now.AddMinutes(-3);
        HashSet<string> runningProcesses = GetRunningProcessNames();

        var providers = new List<AiProviderUsage>(ProviderDefinitions.Length);

        foreach ((string name, string folderName, string[] processHints) in ProviderDefinitions)
        {
            string rootPath = Path.Combine(userProfile, folderName);
            AiProviderUsage usage = InspectProviderDirectory(
                name,
                rootPath,
                today,
                activeThreshold,
                processHints,
                runningProcesses);

            providers.Add(usage);
        }

        int totalInteractionsToday = providers.Sum(p => p.InteractionsToday);
        List<string> activeNames = providers.Where(p => p.IsActiveNow).Select(p => p.ProviderName).ToList();

        string summary;
        if (activeNames.Count > 0)
        {
            summary = $"{string.Join(" + ", activeNames)} ativo ({totalInteractionsToday} hoje)";
        }
        else if (totalInteractionsToday > 0)
        {
            AiProviderUsage topProvider = providers.OrderByDescending(p => p.InteractionsToday).First();
            summary = $"{topProvider.ProviderName}: {totalInteractionsToday} interações hoje";
        }
        else
        {
            summary = "Inativo";
        }

        return new AiUsageSnapshot
        {
            TotalInteractionsToday = totalInteractionsToday,
            ActiveModelSummary = summary,
            Providers = providers
        };
    }

    private static AiProviderUsage InspectProviderDirectory(
        string providerName,
        string rootPath,
        DateTime today,
        DateTime activeThreshold,
        string[] processHints,
        HashSet<string> runningProcesses)
    {
        bool processActive = processHints.Any(runningProcesses.Contains);

        if (!Directory.Exists(rootPath))
        {
            return new AiProviderUsage
            {
                ProviderName = providerName,
                SessionsToday = 0,
                InteractionsToday = 0,
                StorageKb = 0,
                IsActiveNow = processActive
            };
        }

        long totalBytes = 0;
        int sessionsToday = 0;
        int interactionsToday = 0;
        bool recentlyModified = false;
        int scannedFiles = 0;
        const int maxFilesToScan = 1500;

        try
        {
            var pendingDirs = new Stack<(string Path, int Depth)>();
            pendingDirs.Push((rootPath, 0));

            while (pendingDirs.Count > 0 && scannedFiles < maxFilesToScan)
            {
                (string currentDir, int depth) = pendingDirs.Pop();

                try
                {
                    foreach (string filePath in Directory.EnumerateFiles(currentDir))
                    {
                        scannedFiles++;
                        if (scannedFiles > maxFilesToScan)
                        {
                            break;
                        }

                        try
                        {
                            var info = new FileInfo(filePath);
                            totalBytes += info.Length;

                            DateTime lastWrite = info.LastWriteTime;
                            if (lastWrite >= activeThreshold)
                            {
                                recentlyModified = true;
                            }

                            if (lastWrite.Date == today && SessionExtensions.Contains(info.Extension))
                            {
                                sessionsToday++;
                                interactionsToday += EstimateFileInteractions(info);
                            }
                        }
                        catch
                        {
                            // Ignore individual file access issues
                        }
                    }

                    if (depth < 4)
                    {
                        foreach (string subDir in Directory.EnumerateDirectories(currentDir))
                        {
                            string dirName = Path.GetFileName(subDir);
                            if (!IgnoredSubfolders.Contains(dirName))
                            {
                                pendingDirs.Push((subDir, depth + 1));
                            }
                        }
                    }
                }
                catch
                {
                    // Ignore inaccessible directories
                }
            }
        }
        catch
        {
            // Ignore root traversal issues
        }

        return new AiProviderUsage
        {
            ProviderName = providerName,
            SessionsToday = sessionsToday,
            InteractionsToday = interactionsToday,
            StorageKb = Math.Max(0L, totalBytes / 1024L),
            IsActiveNow = recentlyModified || processActive
        };
    }

    private static int EstimateFileInteractions(FileInfo fileInfo)
    {
        try
        {
            if (fileInfo.Extension.Equals(".jsonl", StringComparison.OrdinalIgnoreCase) &&
                fileInfo.Length > 0 &&
                fileInfo.Length <= 8 * 1024 * 1024)
            {
                int lines = 0;
                using var stream = new FileStream(fileInfo.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                while (reader.ReadLine() != null)
                {
                    lines++;
                }

                return Math.Max(1, lines);
            }
        }
        catch
        {
            // Fallback to 1 interaction if file is locked
        }

        return 1;
    }

    private static HashSet<string> GetRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (Process proc in Process.GetProcesses())
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(proc.ProcessName))
                    {
                        names.Add(proc.ProcessName);
                    }
                }
                catch
                {
                    // Ignore inaccessible process
                }
                finally
                {
                    proc.Dispose();
                }
            }
        }
        catch
        {
            // Ignore process enumeration errors
        }

        return names;
    }
}
