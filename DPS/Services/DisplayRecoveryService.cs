using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DPS.Services;

[Flags]
public enum DisplayRecoveryCause
{
    None = 0,
    NoMonitors = 1 << 0,
    NoCurrentMonitor = 1 << 1,
    MissingWindow = 1 << 2,
    HiddenWindow = 1 << 3,
    MinimizedWindow = 1 << 4,
    InvalidWindowSize = 1 << 5,
    MonitorTopologyChanged = 1 << 6,
    CurrentMonitorChanged = 1 << 7,
    WindowMoved = 1 << 8,
    WindowResized = 1 << 9,
}

public sealed class DisplayRecoveryService
{
    private const int PollIntervalSeconds = 15;
    private const int PersistentBadStateLogSeconds = 60;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private DisplayTopologySnapshot? baselineSnapshot;
    private DateTime nextPollUtc = DateTime.MinValue;
    private DateTime nextPersistentBadStateLogUtc = DateTime.MinValue;
    private readonly Dictionary<DisplayRecoveryCause, DateTime> lastTriggerUtc = new();
    private DisplayRecoveryCause enabledCauses;
    private DisplayRecoveryCause currentInvalidCauses;
    private bool startupConfigLogged;
    private bool recoveryActive;
    private int currentStableSeconds = 30;
    private int currentPauseSeconds = 180;

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

            if (currentInvalidCauses != DisplayRecoveryCause.None)
                return $"waiting for enabled conditions to clear: {currentInvalidCauses}";

            var now = DateTime.UtcNow;
            var rearmUtc = GetRearmUtc();
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
            "[DPS] Foreground display recovery guard config: enabled={Enabled}; causes={Causes}; pollSeconds={PollSeconds}; pauseSeconds={PauseSeconds}; stableSeconds={StableSeconds}.",
            configuration.ForegroundDisplayRecoveryGuardEnabled,
            GetEnabledCauses(configuration),
            PollIntervalSeconds,
            ClampPauseSeconds(configuration.ForegroundDisplayRecoveryPauseSeconds),
            ClampStableSeconds(configuration.ForegroundDisplayRecoveryStableSeconds));
    }

    public bool RefreshConfiguration(Configuration configuration)
    {
        UpdateOptions(configuration);
        if (!ShouldWatch(configuration))
        {
            ResetInactive(GetInactiveStatus(configuration));
            return false;
        }

        UpdateRecoveryStatus(DateTime.UtcNow);
        return recoveryActive;
    }

    public bool Tick(Configuration configuration)
    {
        var now = DateTime.UtcNow;
        UpdateOptions(configuration);
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

        UpdateRecoveryStatus(now);
        return recoveryActive;
    }

    private void UpdateOptions(Configuration configuration)
    {
        currentStableSeconds = ClampStableSeconds(configuration.ForegroundDisplayRecoveryStableSeconds);
        currentPauseSeconds = ClampPauseSeconds(configuration.ForegroundDisplayRecoveryPauseSeconds);
        enabledCauses = GetEnabledCauses(configuration);
        // Remove each disabled cause's timer, including any extension it contributed.
        foreach (var cause in lastTriggerUtc.Keys.ToArray())
        {
            if ((enabledCauses & cause) == 0)
                lastTriggerUtc.Remove(cause);
        }

        currentInvalidCauses = CurrentSnapshot == null
            ? DisplayRecoveryCause.None
            : GetInvalidCauses(CurrentSnapshot) & enabledCauses;
        if (recoveryActive && lastTriggerUtc.Count == 0)
        {
            recoveryActive = false;
            LastChangeUtc = null;
            TriggerReason = "none";
        }
        else if (lastTriggerUtc.Count > 0)
        {
            LastChangeUtc = lastTriggerUtc.Values.Max();
            TriggerReason = ActiveCauses().ToString();
        }
    }

    private void Poll(Configuration configuration, DateTime now)
    {
        var snapshot = DisplayTopologySnapshot.Capture();
        CurrentSnapshot = snapshot;

        currentInvalidCauses = GetInvalidCauses(snapshot) & enabledCauses;
        var triggers = baselineSnapshot == null
            ? currentInvalidCauses
            : GetChangedCauses(baselineSnapshot, snapshot) & enabledCauses;
        // A newly enabled persistent condition also needs its own timer.
        triggers |= currentInvalidCauses & ~ActiveCauses();
        baselineSnapshot = snapshot;

        if (triggers != DisplayRecoveryCause.None)
            StartOrExtendRecovery(configuration, now, triggers, snapshot, recoveryActive);

        if (currentInvalidCauses != DisplayRecoveryCause.None)
            LogPersistentBadStateThrottled(now, snapshot);
    }

    private void StartOrExtendRecovery(
        Configuration configuration,
        DateTime now,
        DisplayRecoveryCause causes,
        DisplayTopologySnapshot snapshot,
        bool extending)
    {
        recoveryActive = true;
        foreach (var cause in Enum.GetValues<DisplayRecoveryCause>())
        {
            if (cause != DisplayRecoveryCause.None && (causes & cause) != 0)
                lastTriggerUtc[cause] = now;
        }
        LastChangeUtc = now;
        TriggerReason = ActiveCauses().ToString();
        nextPersistentBadStateLogUtc = now.AddSeconds(PersistentBadStateLogSeconds);

        Plugin.Log.Information(
            "[DPS] Foreground display recovery {Action}: reason={Reason}; pauseSeconds={PauseSeconds}; stableSeconds={StableSeconds}; snapshot={Snapshot}.",
            extending ? "extended" : "started",
            causes,
            ClampPauseSeconds(configuration.ForegroundDisplayRecoveryPauseSeconds),
            ClampStableSeconds(configuration.ForegroundDisplayRecoveryStableSeconds),
            snapshot.ToDisplayText());
    }

    private void UpdateRecoveryStatus(DateTime now)
    {
        if (!recoveryActive)
        {
            Status = $"Display recovery watching enabled foreground exceptions: {enabledCauses}.";
            return;
        }

        if (currentInvalidCauses != DisplayRecoveryCause.None)
        {
            Status = $"Display recovery active; waiting for enabled conditions to clear: {currentInvalidCauses}.";
            return;
        }

        var rearmUtc = GetRearmUtc();
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
        lastTriggerUtc.Clear();
        Status = "Display recovery complete; foreground no-render re-armed.";
        Plugin.Log.Information(
            "[DPS] Foreground display recovery ended; foreground no-render re-armed. reason={Reason}; snapshot={Snapshot}.",
            TriggerReason,
            CurrentSnapshot?.ToDisplayText() ?? "none");
    }

    private DateTime? GetRearmUtc()
        => recoveryActive && LastChangeUtc != null
            ? LastChangeUtc.Value.AddSeconds(Math.Max(currentPauseSeconds, currentStableSeconds))
            : null;

    private DisplayRecoveryCause ActiveCauses()
        => lastTriggerUtc.Keys.Aggregate(DisplayRecoveryCause.None, (causes, cause) => causes | cause);

    private void LogPersistentBadStateThrottled(DateTime now, DisplayTopologySnapshot snapshot)
    {
        if (now < nextPersistentBadStateLogUtc)
            return;

        nextPersistentBadStateLogUtc = now.AddSeconds(PersistentBadStateLogSeconds);
        Plugin.Log.Warning(
            "[DPS] Foreground display recovery waiting on enabled conditions: causes={Causes}; snapshot={Snapshot}.",
            currentInvalidCauses,
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
        lastTriggerUtc.Clear();
        currentInvalidCauses = DisplayRecoveryCause.None;
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
        && GetEnabledCauses(configuration) != DisplayRecoveryCause.None
        && configuration.PluginEnabled
        && configuration.ForegroundNoRenderEnabled;

    private static string GetInactiveStatus(Configuration configuration)
    {
        if (!configuration.ForegroundDisplayRecoveryGuardEnabled)
            return "Display recovery guard disabled.";

        if (GetEnabledCauses(configuration) == DisplayRecoveryCause.None)
            return "Display recovery has no enabled exceptions.";

        if (!configuration.PluginEnabled)
            return "Display recovery waiting for plugin enable.";

        if (!configuration.ForegroundNoRenderEnabled)
            return "Display recovery waiting for foreground no-render.";

        return "Display recovery guard idle.";
    }

    public static DisplayRecoveryCause GetEnabledCauses(Configuration configuration)
    {
        if (!configuration.ForegroundDisplayRecoveryGuardEnabled)
            return DisplayRecoveryCause.None;

        var causes = DisplayRecoveryCause.None;
        if (configuration.DisplayRecoveryNoMonitors) causes |= DisplayRecoveryCause.NoMonitors;
        if (configuration.DisplayRecoveryNoCurrentMonitor) causes |= DisplayRecoveryCause.NoCurrentMonitor;
        if (configuration.DisplayRecoveryMissingWindow) causes |= DisplayRecoveryCause.MissingWindow;
        if (configuration.DisplayRecoveryHiddenWindow) causes |= DisplayRecoveryCause.HiddenWindow;
        if (configuration.DisplayRecoveryMinimizedWindow) causes |= DisplayRecoveryCause.MinimizedWindow;
        if (configuration.DisplayRecoveryInvalidWindowSize) causes |= DisplayRecoveryCause.InvalidWindowSize;
        if (configuration.DisplayRecoveryMonitorTopologyChanges) causes |= DisplayRecoveryCause.MonitorTopologyChanged;
        if (configuration.DisplayRecoveryCurrentMonitorChanges) causes |= DisplayRecoveryCause.CurrentMonitorChanged;
        if (configuration.DisplayRecoveryWindowMovement) causes |= DisplayRecoveryCause.WindowMoved;
        if (configuration.DisplayRecoveryWindowResizing) causes |= DisplayRecoveryCause.WindowResized;
        return causes;
    }

    private static DisplayRecoveryCause GetInvalidCauses(DisplayTopologySnapshot snapshot)
    {
        var causes = DisplayRecoveryCause.None;
        if (snapshot.Monitors.Count == 0) causes |= DisplayRecoveryCause.NoMonitors;
        if (!HasCurrentMonitor(snapshot)) causes |= DisplayRecoveryCause.NoCurrentMonitor;
        if (!snapshot.Window.Exists)
            return causes | DisplayRecoveryCause.MissingWindow;

        if (!snapshot.Window.Visible) causes |= DisplayRecoveryCause.HiddenWindow;
        if (snapshot.Window.Minimized) causes |= DisplayRecoveryCause.MinimizedWindow;
        if (snapshot.Window.Width <= 0 || snapshot.Window.Height <= 0) causes |= DisplayRecoveryCause.InvalidWindowSize;
        return causes;
    }

    private static DisplayRecoveryCause GetChangedCauses(DisplayTopologySnapshot previous, DisplayTopologySnapshot current)
    {
        var causes = GetInvalidCauses(previous) ^ GetInvalidCauses(current);
        if (!previous.Window.Exists || !current.Window.Exists)
            causes &= ~(DisplayRecoveryCause.HiddenWindow | DisplayRecoveryCause.MinimizedWindow | DisplayRecoveryCause.InvalidWindowSize);
        // Loss/return of all monitors or the current monitor has its own opt-in.
        if (previous.Monitors.Count > 0 && current.Monitors.Count > 0
            && HasCurrentMonitor(previous) && HasCurrentMonitor(current)
            && previous.MonitorSignature != current.MonitorSignature)
            causes |= DisplayRecoveryCause.MonitorTopologyChanged;
        if (previous.Monitors.Count > 0 && current.Monitors.Count > 0
            && HasCurrentMonitor(previous) && HasCurrentMonitor(current)
            && previous.CurrentMonitorDeviceName != current.CurrentMonitorDeviceName)
            causes |= DisplayRecoveryCause.CurrentMonitorChanged;

        // Missing/hidden/minimized/empty windows are separate conditions, not moves or resizes.
        if (HasOrdinaryWindow(previous.Window) && HasOrdinaryWindow(current.Window))
        {
            if (previous.Window.X != current.Window.X || previous.Window.Y != current.Window.Y)
                causes |= DisplayRecoveryCause.WindowMoved;
            if (previous.Window.Width != current.Window.Width || previous.Window.Height != current.Window.Height)
                causes |= DisplayRecoveryCause.WindowResized;
        }
        return causes;
    }

    private static bool HasCurrentMonitor(DisplayTopologySnapshot snapshot)
        => !string.Equals(snapshot.CurrentMonitorDeviceName, "none", StringComparison.OrdinalIgnoreCase);

    private static bool HasOrdinaryWindow(DisplayWindowSnapshot window)
        => window.Exists && window.Visible && !window.Minimized && window.Width > 0 && window.Height > 0;

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
    // Ordinary moves/resizes stay out of this snapshot key; recovery compares geometry only with explicit opt-ins.
    public string Signature => $"{Exists}:{Visible}:{Minimized}";
    public string ToDisplayText() => Exists ? $"{X},{Y} {Width}x{Height}; visible={Visible}; minimized={Minimized}" : "missing";
}
