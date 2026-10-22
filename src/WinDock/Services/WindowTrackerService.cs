using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WinDock.Interop;
using WinDock.Models;

namespace WinDock.Services;

public sealed class WindowTrackerService : IWindowTrackerService
{
    private sealed class CachedWindowMetadata
    {
        public required string ProcessName { get; init; }
        public string ExecutablePath { get; init; } = string.Empty;
        public ImageSource? Icon { get; init; }
    }

    [DllImport("user32.dll")]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    private static readonly HashSet<string> ExcludedClassNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        Win32Interop.ShellTrayWndClassName,
        Win32Interop.ShellSecondaryTrayWndClassName,
        "Windows.UI.Core.CoreWindow",
        "Shell_LightDismissOverlay"
    };

    private readonly Dictionary<IntPtr, CachedWindowMetadata> _metadataCache = new();
    private readonly uint _currentProcessId = (uint)Environment.ProcessId;

    public IReadOnlyList<AppWindowInfo> GetOpenWindows(IntPtr excludeWindowHandle)
    {
        var results = new List<AppWindowInfo>();
        var activeHandles = new HashSet<IntPtr>();
        IntPtr foregroundHwnd = Win32Interop.GetForegroundWindow();

        Win32Interop.EnumWindows((hWnd, _) =>
        {
            if (hWnd == IntPtr.Zero || hWnd == excludeWindowHandle)
            {
                return true;
            }

            if (!Win32Interop.IsWindowVisible(hWnd))
            {
                return true;
            }

            int exStyle = Win32Interop.GetWindowLong(hWnd, Win32Interop.GWL_EXSTYLE);
            if ((exStyle & Win32Interop.WS_EX_TOOLWINDOW) != 0)
            {
                return true;
            }

            IntPtr owner = Win32Interop.GetWindow(hWnd, Win32Interop.GW_OWNER);
            if (owner != IntPtr.Zero && (exStyle & Win32Interop.WS_EX_APPWINDOW) == 0)
            {
                return true;
            }

            if (IsWindowCloaked(hWnd))
            {
                return true;
            }

            if (!Win32Interop.IsIconic(hWnd))
            {
                if (Win32Interop.GetWindowRect(hWnd, out Win32Interop.RECT r))
                {
                    if ((r.right - r.left) <= 0 || (r.bottom - r.top) <= 0)
                    {
                        return true;
                    }
                }
            }

            string title = GetWindowTitle(hWnd);
            if (string.IsNullOrWhiteSpace(title))
            {
                return true;
            }

            string className = GetWindowClassName(hWnd);
            if (ExcludedClassNames.Contains(className))
            {
                return true;
            }

            Win32Interop.GetWindowThreadProcessId(hWnd, out uint pid);
            if (pid == 0 || pid == _currentProcessId)
            {
                return true;
            }

            activeHandles.Add(hWnd);
            var metadata = GetOrExtractMetadata(hWnd, pid);

            results.Add(new AppWindowInfo
            {
                Handle = hWnd,
                Title = title,
                ProcessName = metadata.ProcessName,
                ExecutablePath = metadata.ExecutablePath,
                Icon = metadata.Icon,
                IsFocused = hWnd == foregroundHwnd,
                IsMinimized = Win32Interop.IsIconic(hWnd)
            });

            return true;
        }, IntPtr.Zero);

        PruneCache(activeHandles);
        return results;
    }

    public void ToggleOrActivateWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !Win32Interop.IsWindow(handle))
        {
            return;
        }

        IntPtr foregroundHwnd = Win32Interop.GetForegroundWindow();
        if (foregroundHwnd == handle && !Win32Interop.IsIconic(handle))
        {
            MinimizeWindow(handle);
        }
        else
        {
            ActivateWindow(handle);
        }
    }

    public void MinimizeWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !Win32Interop.IsWindow(handle))
        {
            return;
        }

        try
        {
            Win32Interop.ShowWindow(handle, Win32Interop.SW_MINIMIZE);
            Win32Interop.ShowWindowAsync(handle, Win32Interop.SW_MINIMIZE);
        }
        catch
        {
            // Ignore minimize errors
        }
    }

    public void ActivateWindow(IntPtr handle)
    {
        if (handle == IntPtr.Zero || !Win32Interop.IsWindow(handle))
        {
            return;
        }

        // 1) Se IsIconic(handle), ShowWindow(handle, SW_RESTORE); senão ShowWindow(handle, SW_SHOW).
        if (Win32Interop.IsIconic(handle))
        {
            Win32Interop.ShowWindow(handle, Win32Interop.SW_RESTORE);
        }
        else
        {
            Win32Interop.ShowWindow(handle, Win32Interop.SW_SHOW);
        }

        // 2) Obtenha foregroundHwnd. Se foregroundHwnd == handle, retorne.
        IntPtr foregroundHwnd = Win32Interop.GetForegroundWindow();
        if (foregroundHwnd == handle)
        {
            return;
        }

        // 3) Obtenha currentThreadId, foregroundThreadId e targetThreadId.
        uint currentThreadId = Win32Interop.GetCurrentThreadId();
        uint foregroundThreadId = Win32Interop.GetWindowThreadProcessId(foregroundHwnd, out _);
        uint targetThreadId = Win32Interop.GetWindowThreadProcessId(handle, out uint targetPid);

        // 4) Chame AllowSetForegroundWindow.
        try
        {
            Win32Interop.AllowSetForegroundWindow((int)targetPid);
            Win32Interop.AllowSetForegroundWindow(Win32Interop.ASFW_ANY);
            Win32Interop.LockSetForegroundWindow(Win32Interop.LSFW_UNLOCK);
        }
        catch { }

        // 5) Faça AttachThreadInput(currentThreadId, foregroundThreadId, true) e AttachThreadInput(currentThreadId, targetThreadId, true).
        bool attachedForeground = false;
        bool attachedTarget = false;

        if (foregroundThreadId != 0 && foregroundThreadId != currentThreadId)
        {
            attachedForeground = Win32Interop.AttachThreadInput(currentThreadId, foregroundThreadId, true);
        }
        if (targetThreadId != 0 && targetThreadId != currentThreadId)
        {
            attachedTarget = Win32Interop.AttachThreadInput(currentThreadId, targetThreadId, true);
        }

        try
        {
            // 6) Fure a pilha do DWM:
            Win32Interop.SetWindowPos(
                handle,
                Win32Interop.HWND_TOPMOST,
                0, 0, 0, 0,
                Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE | Win32Interop.SWP_SHOWWINDOW);

            bool ok = Win32Interop.SetForegroundWindow(handle);

            Win32Interop.SetWindowPos(
                handle,
                Win32Interop.HWND_NOTOPMOST,
                0, 0, 0, 0,
                Win32Interop.SWP_NOMOVE | Win32Interop.SWP_NOSIZE);

            Win32Interop.BringWindowToTop(handle);

            if (!ok)
            {
                Win32Interop.SwitchToThisWindow(handle, true);
            }
        }
        catch
        {
            try
            {
                Win32Interop.SwitchToThisWindow(handle, true);
            }
            catch { }
        }
        finally
        {
            // 7) No bloco finally: desanexe os threads em ordem inversa.
            if (attachedTarget)
            {
                try { Win32Interop.AttachThreadInput(currentThreadId, targetThreadId, false); } catch { }
            }
            if (attachedForeground)
            {
                try { Win32Interop.AttachThreadInput(currentThreadId, foregroundThreadId, false); } catch { }
            }
        }

        // 8) Fallback: se GetForegroundWindow() != handle, simule VK_MENU (Alt) down e up, e chame SetForegroundWindow e SwitchToThisWindow.
        if (Win32Interop.GetForegroundWindow() != handle)
        {
            try
            {
                Win32Interop.keybd_event(Win32Interop.VK_MENU, 0, 0, UIntPtr.Zero);
                Win32Interop.keybd_event(Win32Interop.VK_MENU, 0, Win32Interop.KEYEVENTF_KEYUP, UIntPtr.Zero);
                Win32Interop.SetForegroundWindow(handle);
                Win32Interop.SwitchToThisWindow(handle, true);
            }
            catch { }
        }
    }

    public bool IsForegroundWindowFullscreen()
    {
        return IsForegroundWindowFullscreen(IntPtr.Zero);
    }

    public bool IsForegroundWindowFullscreen(IntPtr excludeWindowHandle)
    {
        IntPtr fg = Win32Interop.GetForegroundWindow();
        if (fg == IntPtr.Zero || !Win32Interop.IsWindow(fg))
        {
            return false;
        }

        if (excludeWindowHandle != IntPtr.Zero && fg == excludeWindowHandle)
        {
            return false;
        }

        Win32Interop.GetWindowThreadProcessId(fg, out uint fgPid);
        if (fgPid == _currentProcessId)
        {
            return false;
        }

        IntPtr shell = Win32Interop.GetShellWindow();
        IntPtr desktop = Win32Interop.GetDesktopWindow();
        if (fg == shell || fg == desktop)
        {
            return false;
        }

        string className = GetWindowClassName(fg);
        if (ExcludedClassNames.Contains(className))
        {
            return false;
        }

        if (IsWindowCloaked(fg) || Win32Interop.IsIconic(fg) || !Win32Interop.IsWindowVisible(fg))
        {
            return false;
        }

        if (Win32Interop.GetWindowRect(fg, out var rect))
        {
            int screenWidth = Win32Interop.GetSystemMetrics(Win32Interop.SM_CXSCREEN);
            int screenHeight = Win32Interop.GetSystemMetrics(Win32Interop.SM_CYSCREEN);

            if (rect.left == 0 &&
                rect.top == 0 &&
                rect.right == screenWidth &&
                rect.bottom == screenHeight)
            {
                return true;
            }
        }

        return false;
    }

    public ImageSource? ExtractIconForPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            string cleanPath = Environment.ExpandEnvironmentVariables(filePath.Trim().Trim('\"'));
            if (!File.Exists(cleanPath))
            {
                int exeIdx = cleanPath.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
                if (exeIdx > 0)
                {
                    string candidate = cleanPath.Substring(0, exeIdx + 4);
                    if (File.Exists(candidate))
                    {
                        cleanPath = candidate;
                    }
                }
            }

            if (!File.Exists(cleanPath))
            {
                return null;
            }

            using var associatedIcon = System.Drawing.Icon.ExtractAssociatedIcon(cleanPath);
            if (associatedIcon != null)
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    associatedIcon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch
        {
            // Ignore extraction errors
        }

        return null;
    }

    private static bool IsWindowCloaked(IntPtr hWnd)
    {
        int hr = Win32Interop.DwmGetWindowAttribute(
            hWnd,
            Win32Interop.DWMWA_CLOAKED,
            out int cloaked,
            sizeof(int));

        return hr == 0 && cloaked != 0;
    }

    private static string GetWindowTitle(IntPtr hWnd)
    {
        int length = Win32Interop.GetWindowTextLength(hWnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 1);
        int read = Win32Interop.GetWindowText(hWnd, sb, sb.Capacity);
        return read > 0 ? sb.ToString() : string.Empty;
    }

    private static string GetWindowClassName(IntPtr hWnd)
    {
        var sb = new StringBuilder(256);
        int read = Win32Interop.GetClassName(hWnd, sb, sb.Capacity);
        return read > 0 ? sb.ToString() : string.Empty;
    }

    private CachedWindowMetadata GetOrExtractMetadata(IntPtr hWnd, uint pid)
    {
        if (_metadataCache.TryGetValue(hWnd, out var cached))
        {
            return cached;
        }

        string? exePath = TryGetExecutablePath(pid, out string processName);
        ImageSource? icon = TryExtractWindowIcon(hWnd, exePath);

        var metadata = new CachedWindowMetadata
        {
            ProcessName = processName,
            ExecutablePath = exePath ?? string.Empty,
            Icon = icon
        };

        _metadataCache[hWnd] = metadata;
        return metadata;
    }

    private static string? TryGetExecutablePath(uint pid, out string processName)
    {
        processName = "App";
        string? exePath = null;

        IntPtr hProcess = Win32Interop.OpenProcess(Win32Interop.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess != IntPtr.Zero)
        {
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                if (Win32Interop.QueryFullProcessImageName(hProcess, 0, sb, ref size) && size > 0)
                {
                    exePath = sb.ToString();
                    string fileName = Path.GetFileNameWithoutExtension(exePath);
                    if (!string.IsNullOrWhiteSpace(fileName))
                    {
                        processName = fileName;
                    }
                }
            }
            finally
            {
                Win32Interop.CloseHandle(hProcess);
            }
        }

        if (exePath == null || processName == "App")
        {
            try
            {
                using var process = Process.GetProcessById((int)pid);
                if (!string.IsNullOrWhiteSpace(process.ProcessName))
                {
                    processName = process.ProcessName;
                }

                exePath ??= process.MainModule?.FileName;
            }
            catch
            {
                // Ignore access denied for protected system processes
            }
        }

        return exePath;
    }

    private ImageSource? TryExtractWindowIcon(IntPtr hWnd, string? exePath)
    {
        try
        {
            IntPtr hIcon = QueryWindowHIcon(hWnd);
            if (hIcon != IntPtr.Zero)
            {
                var bitmap = Imaging.CreateBitmapSourceFromHIcon(
                    hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bitmap.Freeze();
                return bitmap;
            }
        }
        catch
        {
            // Fall through to executable icon extraction
        }

        if (!string.IsNullOrWhiteSpace(exePath))
        {
            return ExtractIconForPath(exePath);
        }

        return null;
    }

    private static IntPtr QueryWindowHIcon(IntPtr hWnd)
    {
        IntPtr hIcon = IntPtr.Zero;

        Win32Interop.SendMessageTimeout(
            hWnd,
            Win32Interop.WM_GETICON,
            new IntPtr(Win32Interop.ICON_BIG),
            IntPtr.Zero,
            Win32Interop.SMTO_ABORTIFHUNG,
            50,
            out hIcon);

        if (hIcon == IntPtr.Zero)
        {
            Win32Interop.SendMessageTimeout(
                hWnd,
                Win32Interop.WM_GETICON,
                new IntPtr(Win32Interop.ICON_SMALL2),
                IntPtr.Zero,
                Win32Interop.SMTO_ABORTIFHUNG,
                50,
                out hIcon);
        }

        if (hIcon == IntPtr.Zero)
        {
            Win32Interop.SendMessageTimeout(
                hWnd,
                Win32Interop.WM_GETICON,
                new IntPtr(Win32Interop.ICON_SMALL),
                IntPtr.Zero,
                Win32Interop.SMTO_ABORTIFHUNG,
                50,
                out hIcon);
        }

        if (hIcon == IntPtr.Zero)
        {
            hIcon = Win32Interop.GetClassLongPtr(hWnd, Win32Interop.GCL_HICON);
        }

        if (hIcon == IntPtr.Zero)
        {
            hIcon = Win32Interop.GetClassLongPtr(hWnd, Win32Interop.GCL_HICONSM);
        }

        return hIcon;
    }

    private void PruneCache(HashSet<IntPtr> activeHandles)
    {
        if (_metadataCache.Count <= activeHandles.Count)
        {
            return;
        }

        var staleKeys = new List<IntPtr>();
        foreach (IntPtr key in _metadataCache.Keys)
        {
            if (!activeHandles.Contains(key))
            {
                staleKeys.Add(key);
            }
        }

        foreach (IntPtr key in staleKeys)
        {
            _metadataCache.Remove(key);
        }
    }
}
