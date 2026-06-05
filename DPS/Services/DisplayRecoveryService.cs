using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DPS.Services;

public sealed class DisplayRecoveryService
{
    private const int PollIntervalSeconds = 15;
    private const int PersistentBadStateLogSeconds = 60;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private DisplayTopologySnapshot? baselineSnapshot;
    private DateTime nextPollUtc = DateTime.MinValue;
    private DateTime nextPersistentBadStateLogUtc = DateTime.MinValue;
    private DateTime? recoveryPauseUntilUtc;
    private bool startupConfigLogged;
    private bool recoveryActive;
    private int currentStableSeconds = 30;

    public DisplayTopologySnapshot? CurrentSnapshot { get; private set; }
    public DateTime? LastChangeUtc { get; private set; }
    public string TriggerReason { get; private set; } = "none";
    public string Status { get; private set; } = "Display recovery guard idle.";
    public bool RecoveryActive => recoveryActive;
    public int PollInterval => PollIntervalSeconds;

    public string LastChangeText
        => LastChangeUtc?.ToString("u") ?? "none";

    public string RearmEtaText
    {
        get
        {
            if (!recoveryActive)
                return "not scheduled";

            if (CurrentSnapshot?.IsInvalid == true)
                return "waiting for valid display state";

            var now = DateTime.UtcNow;
            var rearmUtc = GetRearmUtc(now, currentStableSeconds);
            if (rearmUtc == null)
                return "pending";

            return now >= rearmUtc.Value ? "ready" : FormatDuration(rearmUtc.Value - now);
        }
    }

    public string CurrentSnapshotText
        => CurrentSnapshot?.ToDisplayText() ?? "No display snapshot yet.";

    public void LogStartupConfig(Configuration configuration)
    {
        if (startupConfigLogged)
            return;

        startupConfigLogged = true;
        Plugin.Log.Information(
            "[DPS] Foreground display recovery guard config: enabled={Enabled}; pollSeconds={PollSeconds}; pauseSeconds={PauseSeconds}; stableSeconds={StableSeconds}.",
            configuration.ForegroundDisplayRecoveryGuardEnabled,
            PollIntervalSeconds,
            ClampPauseSeconds(configuration.ForegroundDisplayRecoveryPauseSeconds),
            ClampStableSeconds(configuration.ForegroundDisplayRecoveryStableSeconds));
    }

    public bool RefreshConfiguration(Configuration configuration)
    {
        currentStableSeconds = ClampStableSeconds(configuration.ForegroundDisplayRecoveryStableSeconds);
        if (!ShouldWatch(configuration))
        {
            ResetInactive(GetInactiveStatus(configuration));
            return false;
        }

        UpdateRecoveryStatus(configuration, DateTime.UtcNow);
        return recoveryActive;
    }

    public bool Tick(Configuration configuration)
    {
        var now = DateTime.UtcNow;
        currentStableSeconds = ClampStableSeconds(configuration.ForegroundDisplayRecoveryStableSeconds);
        if (!ShouldWatch(configuration))
        {
            ResetInactive(GetInactiveStatus(configuration));
            return false;
        }

        if (now >= nextPollUtc)
        {
            nextPollUtc = now.AddSeconds(PollIntervalSeconds);
            Poll(configuration, now);
        }

        UpdateRecoveryStatus(configuration, now);
        return recoveryActive;
    }

    private void Poll(Configuration configuration, DateTime now)
    {
        var snapshot = DisplayTopologySnapshot.Capture();
        CurrentSnapshot = snapshot;

        if (baselineSnapshot == null)
        {
            baselineSnapshot = snapshot;
            if (snapshot.IsInvalid)
                StartOrExtendRecovery(configuration, now, $"initial invalid display state: {snapshot.InvalidReason}", snapshot, extending: false);
            return;
        }

        if (!string.Equals(snapshot.Signature, baselineSnapshot.Signature, StringComparison.Ordinal))
        {
            var reason = BuildChangeReason(baselineSnapshot, snapshot);
            Plugin.Log.Information(
                "[DPS] Display topology changed during foreground no-render: {Reason}; previous={Previous}; current={Current}.",
                reason,
                baselineSnapshot.ToDisplayText(),
                snapshot.ToDisplayText());

            baselineSnapshot = snapshot;
            StartOrExtendRecovery(configuration, now, reason, snapshot, recoveryActive);
            return;
        }

        if (snapshot.IsInvalid)
        {
            if (!recoveryActive)
                StartOrExtendRecovery(configuration, now, $"invalid display state: {snapshot.InvalidReason}", snapshot, extending: false);

            LogPersistentBadStateThrottled(now, snapshot);
        }
    }

    private void StartOrExtendRecovery(
        Configuration configuration,
        DateTime now,
        string reason,
        DisplayTopologySnapshot snapshot,
        bool extending)
    {
        recoveryActive = true;
        LastChangeUtc = now;
        TriggerReason = reason;
        recoveryPauseUntilUtc = now.AddSeconds(ClampPauseSeconds(configuration.ForegroundDisplayRecoveryPauseSeconds));
        nextPersistentBadStateLogUtc = now.AddSeconds(PersistentBadStateLogSeconds);

        Plugin.Log.Information(
            "[DPS] Foreground display recovery {Action}: reason={Reason}; pauseSeconds={PauseSeconds}; stableSeconds={StableSeconds}; snapshot={Snapshot}.",
            extending ? "extended" : "started",
            reason,
            ClampPauseSeconds(configuration.ForegroundDisplayRecoveryPauseSeconds),
            ClampStableSeconds(configuration.ForegroundDisplayRecoveryStableSeconds),
            snapshot.ToDisplayText());
    }

    private void UpdateRecoveryStatus(Configuration configuration, DateTime now)
    {
        if (!recoveryActive)
        {
            Status = "Display recovery guard watching foreground no-render.";
            return;
        }

        if (CurrentSnapshot?.IsInvalid == true)
        {
            Status = $"Display recovery active; waiting for valid display state: {CurrentSnapshot.InvalidReason}.";
            return;
        }

        var rearmUtc = GetRearmUtc(now, ClampStableSeconds(configuration.ForegroundDisplayRecoveryStableSeconds));
        if (rearmUtc == null)
        {
            Status = "Display recovery active; rearm pending.";
            return;
        }

        if (now < rearmUtc.Value)
        {
            Status = $"Display recovery active; foreground no-render re-arms in {FormatDuration(rearmUtc.Value - now)}.";
            return;
        }

        recoveryActive = false;
        recoveryPauseUntilUtc = null;
        Status = "Display recovery complete; foreground no-render re-armed.";
        Plugin.Log.Information(
            "[DPS] Foreground display recovery ended; foreground no-render re-armed. reason={Reason}; snapshot={Snapshot}.",
            TriggerReason,
            CurrentSnapshot?.ToDisplayText() ?? "none");
    }

    private DateTime? GetRearmUtc(DateTime now, int? foregroundStableSeconds)
    {
        if (!recoveryActive)
            return null;

        var rearmUtc = recoveryPauseUntilUtc ?? now;
        if (LastChangeUtc != null)
        {
            var stableSeconds = foregroundStableSeconds ?? 0;
            var stableUtc = LastChangeUtc.Value.AddSeconds(stableSeconds);
            if (stableUtc > rearmUtc)
                rearmUtc = stableUtc;
        }

        return rearmUtc;
    }

    private void LogPersistentBadStateThrottled(DateTime now, DisplayTopologySnapshot snapshot)
    {
        if (now < nextPersistentBadStateLogUtc)
            return;

        nextPersistentBadStateLogUtc = now.AddSeconds(PersistentBadStateLogSeconds);
        Plugin.Log.Warning(
            "[DPS] Foreground display recovery still waiting on valid display state: reason={Reason}; snapshot={Snapshot}.",
            snapshot.InvalidReason,
            snapshot.ToDisplayText());
    }

    private void ResetInactive(string status)
    {
        if (recoveryActive)
        {
            Plugin.Log.Information(
                "[DPS] Foreground display recovery cancelled: {Status}; lastReason={Reason}.",
                status,
                TriggerReason);
        }

        recoveryActive = false;
        recoveryPauseUntilUtc = null;
        baselineSnapshot = null;
        CurrentSnapshot = null;
        LastChangeUtc = null;
        TriggerReason = "none";
        Status = status;
        nextPollUtc = DateTime.MinValue;
        nextPersistentBadStateLogUtc = DateTime.MinValue;
    }

    private static bool ShouldWatch(Configuration configuration)
        => configuration.ForegroundDisplayRecoveryGuardEnabled
        && configuration.PluginEnabled
        && configuration.ForegroundNoRenderEnabled;

    private static string GetInactiveStatus(Configuration configuration)
    {
        if (!configuration.ForegroundDisplayRecoveryGuardEnabled)
            return "Display recovery guard disabled.";

        if (!configuration.PluginEnabled)
            return "Display recovery waiting for plugin enable.";

        if (!configuration.ForegroundNoRenderEnabled)
            return "Display recovery waiting for foreground no-render.";

        return "Display recovery guard idle.";
    }

    private static string BuildChangeReason(DisplayTopologySnapshot previous, DisplayTopologySnapshot current)
    {
        var reasons = new List<string>();
        if (previous.MonitorSignature != current.MonitorSignature)
            reasons.Add("monitors changed");
        if (previous.WindowSignature != current.WindowSignature)
            reasons.Add("window changed");
        if (previous.CurrentMonitorDeviceName != current.CurrentMonitorDeviceName)
            reasons.Add("current monitor changed");
        if (previous.IsInvalid != current.IsInvalid)
            reasons.Add(current.IsInvalid ? "state became invalid" : "state became valid");

        return reasons.Count == 0 ? "display snapshot changed" : string.Join(", ", reasons);
    }

    private static int ClampPauseSeconds(int value)
        => Math.Clamp(value, 15, 900);

    private static int ClampStableSeconds(int value)
        => Math.Clamp(value, 5, 300);

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return "0s";

        if (duration.TotalMinutes >= 1d)
            return $"{Math.Ceiling(duration.TotalMinutes):0}m";

        return $"{Math.Ceiling(duration.TotalSeconds):0}s";
    }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clipRect, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint windowHandle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint windowHandle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint windowHandle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(nint monitorHandle, ref MonitorInfoEx monitorInfo);

    private delegate bool EnumWindowsProc(nint windowHandle, nint parameter);
    private delegate bool MonitorEnumProc(nint monitorHandle, nint hdcMonitor, ref Rect monitorRect, nint data);

    internal static bool TryResolveGameWindow(out nint windowHandle, out string status)
    {
        windowHandle = nint.Zero;

        using var process = Process.GetCurrentProcess();
        process.Refresh();

        if (process.MainWindowHandle != nint.Zero && IsWindow(process.MainWindowHandle))
        {
            windowHandle = process.MainWindowHandle;
            status = "process main window";
            return true;
        }

        var processId = (uint)process.Id;
        var fallback = nint.Zero;
        EnumWindows((candidate, parameter) =>
        {
            GetWindowThreadProcessId(candidate, out var candidateProcessId);
            if (candidateProcessId != processId || !IsWindow(candidate))
                return true;

            fallback = candidate;
            if (IsWindowVisible(candidate) && TryReadWindowRect(candidate, out var rect, out _) && rect.Right > rect.Left && rect.Bottom > rect.Top)
                return false;

            return true;
        }, nint.Zero);

        if (fallback != nint.Zero)
        {
            windowHandle = fallback;
            status = "enumerated process window";
            return true;
        }

        status = "game window handle unavailable";
        return false;
    }

    internal static List<DisplayMonitorSnapshot> EnumerateMonitors()
    {
        var monitors = new List<DisplayMonitorSnapshot>();
        MonitorEnumProc callback = (nint monitorHandle, nint hdcMonitor, ref Rect monitorRect, nint data) =>
        {
            if (TryReadMonitor(monitorHandle, out var monitor, out _))
                monitors.Add(monitor);

            return true;
        };

        _ = EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);
        return monitors
            .OrderBy(monitor => monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(monitor => monitor.Left)
            .ThenBy(monitor => monitor.Top)
            .ToList();
    }

    internal static bool TryReadWindow(nint windowHandle, out DisplayWindowSnapshot window, out string status)
    {
        window = DisplayWindowSnapshot.Missing;

        if (windowHandle == nint.Zero || !IsWindow(windowHandle))
        {
            status = "game window handle unavailable";
            return false;
        }

        var visible = IsWindowVisible(windowHandle);
        var minimized = IsIconic(windowHandle);
        if (!TryReadWindowRect(windowHandle, out var rect, out status))
        {
            window = new DisplayWindowSnapshot(true, visible, minimized, 0, 0, 0, 0);
            return false;
        }

        window = new DisplayWindowSnapshot(
            true,
            visible,
            minimized,
            rect.Left,
            rect.Top,
            Math.Max(0, rect.Right - rect.Left),
            Math.Max(0, rect.Bottom - rect.Top));
        status = "game window read";
        return true;
    }

    internal static bool TryReadCurrentMonitor(nint windowHandle, out DisplayMonitorSnapshot monitor, out string status)
    {
        monitor = DisplayMonitorSnapshot.Empty;
        if (windowHandle == nint.Zero)
        {
            status = "game window handle unavailable";
            return false;
        }

        var monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitorHandle == nint.Zero)
        {
            status = "current monitor unavailable";
            return false;
        }

        return TryReadMonitor(monitorHandle, out monitor, out status);
    }

    private static bool TryReadWindowRect(nint windowHandle, out Rect rect, out string status)
    {
        if (GetWindowRect(windowHandle, out rect))
        {
            status = "window rect read";
            return true;
        }

        status = LastWin32Error("GetWindowRect");
        return false;
    }

    private static bool TryReadMonitor(nint monitorHandle, out DisplayMonitorSnapshot monitor, out string status)
    {
        var info = new MonitorInfoEx
        {
            Size = Marshal.SizeOf<MonitorInfoEx>(),
        };

        if (!GetMonitorInfo(monitorHandle, ref info))
        {
            monitor = DisplayMonitorSnapshot.Empty;
            status = LastWin32Error("GetMonitorInfo");
            return false;
        }

        monitor = new DisplayMonitorSnapshot(info.DeviceName, info.Monitor.Left, info.Monitor.Top, info.Monitor.Right, info.Monitor.Bottom);
        status = "monitor info read";
        return true;
    }

    private static string LastWin32Error(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        return $"{operation} failed (Win32 {error}: {new Win32Exception(error).Message}).";
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect WorkArea;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
    }
}

public sealed record DisplayTopologySnapshot(
    IReadOnlyList<DisplayMonitorSnapshot> Monitors,
    DisplayWindowSnapshot Window,
    string CurrentMonitorDeviceName,
    string WindowStatus,
    string CurrentMonitorStatus)
{
    public static DisplayTopologySnapshot Capture()
    {
        var monitors = DisplayRecoveryService.EnumerateMonitors();

        _ = DisplayRecoveryService.TryResolveGameWindow(out var windowHandle, out var resolveStatus);
        var windowStatus = resolveStatus;

        if (!DisplayRecoveryService.TryReadWindow(windowHandle, out var window, out var readStatus))
            windowStatus = $"{windowStatus}; {readStatus}";

        var currentMonitorDeviceName = "none";
        var currentMonitorStatus = "current monitor unavailable";
        if (DisplayRecoveryService.TryReadCurrentMonitor(windowHandle, out var currentMonitor, out currentMonitorStatus))
            currentMonitorDeviceName = currentMonitor.DeviceName;

        return new DisplayTopologySnapshot(monitors, window, currentMonitorDeviceName, windowStatus, currentMonitorStatus);
    }

    public bool IsInvalid
        => Monitors.Count == 0
        || !Window.Exists
        || !Window.Visible
        || Window.Minimized
        || Window.Width <= 0
        || Window.Height <= 0
        || string.Equals(CurrentMonitorDeviceName, "none", StringComparison.OrdinalIgnoreCase);

    public string InvalidReason
    {
        get
        {
            var reasons = new List<string>();
            if (Monitors.Count == 0)
                reasons.Add("no monitors");
            if (!Window.Exists)
                reasons.Add("window missing");
            if (!Window.Visible)
                reasons.Add("window hidden");
            if (Window.Minimized)
                reasons.Add("window minimized");
            if (Window.Width <= 0 || Window.Height <= 0)
                reasons.Add("window rect empty");
            if (string.Equals(CurrentMonitorDeviceName, "none", StringComparison.OrdinalIgnoreCase))
                reasons.Add("current monitor missing");

            return reasons.Count == 0 ? "valid" : string.Join(", ", reasons);
        }
    }

    public string MonitorSignature
        => string.Join("|", Monitors.Select(monitor => monitor.Signature));

    public string WindowSignature
        => Window.Signature;

    public string Signature
        => $"{MonitorSignature}::{WindowSignature}::current={CurrentMonitorDeviceName}::invalid={IsInvalid}";

    public string ToDisplayText()
    {
        var monitorText = Monitors.Count == 0
            ? "none"
            : string.Join("; ", Monitors.Select(monitor => monitor.ToDisplayText()));

        return $"monitors={Monitors.Count} [{monitorText}]; window={Window.ToDisplayText()} ({WindowStatus}); currentMonitor={CurrentMonitorDeviceName} ({CurrentMonitorStatus}); valid={!IsInvalid}";
    }
}

public sealed record DisplayMonitorSnapshot(string DeviceName, int Left, int Top, int Right, int Bottom)
{
    public static readonly DisplayMonitorSnapshot Empty = new("none", 0, 0, 0, 0);
    public string Signature => $"{DeviceName}:{Left},{Top},{Right},{Bottom}";
    public string ToDisplayText() => $"{DeviceName} {Left},{Top}-{Right},{Bottom}";
}

public sealed record DisplayWindowSnapshot(bool Exists, bool Visible, bool Minimized, int X, int Y, int Width, int Height)
{
    public static readonly DisplayWindowSnapshot Missing = new(false, false, false, 0, 0, 0, 0);
    public string Signature => $"{Exists}:{Visible}:{Minimized}:{X},{Y},{Width},{Height}";
    public string ToDisplayText() => Exists ? $"{X},{Y} {Width}x{Height}; visible={Visible}; minimized={Minimized}" : "missing";
}
