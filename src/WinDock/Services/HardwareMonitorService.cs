using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WinDock.Models;

namespace WinDock.Services;

public sealed class HardwareMonitorService : IHardwareMonitorService, IDisposable
{
    private readonly object _cpuLock = new();
    private ulong _prevIdleTime;
    private ulong _prevKernelTime;
    private ulong _prevUserTime;
    private double _lastCpuPercent;

    private double _cachedGpuPercent;
    private DateTime _lastGpuSampleUtc = DateTime.MinValue;
    private int _isSamplingGpu;
    private bool _disposed;

    public HardwareMonitorService()
    {
        TrySampleCpuDelta(out _);
        TriggerGpuSampleIfNeeded();
    }

    public HardwareMetrics GetCurrentMetrics()
    {
        double cpuPercent = GetCpuUsagePercent();
        (double ramPercent, double ramUsedGb, double ramTotalGb) = GetMemoryMetrics();
        TriggerGpuSampleIfNeeded();
        double gpuPercent = Volatile.Read(ref _cachedGpuPercent);

        return new HardwareMetrics
        {
            CpuPercent = Math.Round(cpuPercent, 1),
            GpuPercent = Math.Round(gpuPercent, 1),
            RamPercent = Math.Round(ramPercent, 1),
            RamUsedGb = Math.Round(ramUsedGb, 1),
            RamTotalGb = Math.Round(ramTotalGb, 1)
        };
    }

    private double GetCpuUsagePercent()
    {
        lock (_cpuLock)
        {
            if (TrySampleCpuDelta(out double cpu))
            {
                _lastCpuPercent = cpu;
            }
            return _lastCpuPercent;
        }
    }

    private bool TrySampleCpuDelta(out double cpuPercent)
    {
        cpuPercent = _lastCpuPercent;
        if (!GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime))
        {
            return false;
        }

        ulong idle = idleTime.ToUInt64();
        ulong kernel = kernelTime.ToUInt64();
        ulong user = userTime.ToUInt64();

        ulong idleDelta = idle - _prevIdleTime;
        ulong kernelDelta = kernel - _prevKernelTime;
        ulong userDelta = user - _prevUserTime;

        _prevIdleTime = idle;
        _prevKernelTime = kernel;
        _prevUserTime = user;

        ulong totalSystem = kernelDelta + userDelta;
        if (totalSystem == 0)
        {
            return false;
        }

        ulong busy = totalSystem > idleDelta ? totalSystem - idleDelta : 0;
        cpuPercent = Math.Clamp((double)busy * 100.0 / totalSystem, 0.0, 100.0);
        return true;
    }

    private static (double RamPercent, double RamUsedGb, double RamTotalGb) GetMemoryMetrics()
    {
        var memStatus = new MEMORYSTATUSEX
        {
            dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>()
        };

        if (!GlobalMemoryStatusEx(ref memStatus) || memStatus.ullTotalPhys == 0)
        {
            return (0.0, 0.0, 0.0);
        }

        const double bytesPerGb = 1024.0 * 1024.0 * 1024.0;
        double totalGb = memStatus.ullTotalPhys / bytesPerGb;
        double availGb = memStatus.ullAvailPhys / bytesPerGb;
        double usedGb = Math.Max(0.0, totalGb - availGb);
        double ramPercent = Math.Clamp((usedGb / totalGb) * 100.0, 0.0, 100.0);

        return (ramPercent, usedGb, totalGb);
    }

    private void TriggerGpuSampleIfNeeded()
    {
        if (_disposed)
        {
            return;
        }

        if ((DateTime.UtcNow - _lastGpuSampleUtc).TotalSeconds < 2.5)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _isSamplingGpu, 1, 0) != 0)
        {
            return;
        }

        _lastGpuSampleUtc = DateTime.UtcNow;

        Task.Run(async () =>
        {
            try
            {
                double gpu = await SampleGpuEngineUtilizationAsync().ConfigureAwait(false);
                Volatile.Write(ref _cachedGpuPercent, gpu);
            }
            catch
            {
                // Ignore counter errors if GPU Engine category is unavailable
            }
            finally
            {
                _lastGpuSampleUtc = DateTime.UtcNow;
                Interlocked.Exchange(ref _isSamplingGpu, 0);
            }
        });
    }

    private static async Task<double> SampleGpuEngineUtilizationAsync()
    {
        if (!PerformanceCounterCategory.Exists("GPU Engine"))
        {
            return 0.0;
        }

        var category = new PerformanceCounterCategory("GPU Engine");
        string[] instanceNames = category.GetInstanceNames();
        if (instanceNames.Length == 0)
        {
            return 0.0;
        }

        var counters = new List<PerformanceCounter>( capacity: Math.Min(instanceNames.Length, 256));
        try
        {
            foreach (string instance in instanceNames)
            {
                if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase) &&
                    !instance.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase) &&
                    !instance.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase) &&
                    !instance.Contains("engtype_Copy", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    var counter = new PerformanceCounter("GPU Engine", "Utilization Percentage", instance, readOnly: true);
                    _ = counter.NextValue();
                    counters.Add(counter);
                }
                catch
                {
                    // Process instance may have exited between GetInstanceNames and NextValue
                }
            }

            if (counters.Count == 0)
            {
                return 0.0;
            }

            await Task.Delay(150).ConfigureAwait(false);

            float totalUtilization = 0f;
            foreach (PerformanceCounter counter in counters)
            {
                try
                {
                    totalUtilization += counter.NextValue();
                }
                catch
                {
                    // Ignore transient instance teardown
                }
            }

            return Math.Clamp(totalUtilization, 0.0, 100.0);
        }
        finally
        {
            foreach (PerformanceCounter counter in counters)
            {
                try
                {
                    counter.Dispose();
                }
                catch
                {
                    // Ignore dispose errors
                }
            }
        }
    }

    public void Dispose()
    {
        _disposed = true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint dwLowDateTime;
        public uint dwHighDateTime;

        public readonly ulong ToUInt64() => ((ulong)dwHighDateTime << 32) | dwLowDateTime;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out FILETIME lpIdleTime,
        out FILETIME lpKernelTime,
        out FILETIME lpUserTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
}
