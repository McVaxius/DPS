using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DPS.Services;

public sealed class WindowPlacementService
{
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    public string Status { get; private set; } = "Window XY idle.";

    public void SetStatus(string status)
        => Status = status;

    public bool TryResolveWindowHandle(out nint windowHandle, out string status)
    {
        windowHandle = nint.Zero;

        using var process = Process.GetCurrentProcess();
        process.Refresh();

        if (IsUsableWindow(process.MainWindowHandle))
        {
            windowHandle = process.MainWindowHandle;
            status = "Game window found from current process main window.";
            return true;
        }

        var processId = (uint)process.Id;
        var found = nint.Zero;
        EnumWindows((candidate, parameter) =>
        {
            GetWindowThreadProcessId(candidate, out var candidateProcessId);
            if (candidateProcessId != processId || !IsUsableWindow(candidate))
                return true;

            found = candidate;
            return false;
        }, nint.Zero);

        if (found != nint.Zero)
        {
            windowHandle = found;
            status = "Game window found by enumerating process windows.";
            return true;
        }

        status = "Game window handle not available yet.";
        return false;
    }

    public bool TryReadCurrentPlacement(out WindowPlacementSnapshot snapshot, out string status)
    {
        snapshot = WindowPlacementSnapshot.Empty;

        if (!TryResolveWindowHandle(out var windowHandle, out status))
            return false;

        if (!TryReadWindowRect(windowHandle, out var windowRect, out status))
            return false;

        var monitorHandle = MonitorFromWindow(windowHandle, MonitorDefaultToNearest);
        if (monitorHandle == nint.Zero || !TryReadMonitor(monitorHandle, out var monitor, out status))
            return false;

        snapshot = new WindowPlacementSnapshot(
            windowRect.Left,
            windowRect.Top,
            Math.Max(0, windowRect.Right - windowRect.Left),
            Math.Max(0, windowRect.Bottom - windowRect.Top),
            monitor.DeviceName,
            monitor.Bounds.Left,
            monitor.Bounds.Top,
            monitor.Bounds.Right,
            monitor.Bounds.Bottom);
        status = "Current game window placement read.";
        return true;
    }

    public bool TryCreateSavedPlacement(out SavedWindowPlacement? placement, out string status)
    {
        placement = null;

        if (!TryReadCurrentPlacement(out var snapshot, out status))
            return false;

        placement = new SavedWindowPlacement
        {
            X = snapshot.X,
            Y = snapshot.Y,
            Width = snapshot.Width,
            Height = snapshot.Height,
            MonitorDeviceName = snapshot.MonitorDeviceName,
            MonitorLeft = snapshot.MonitorLeft,
            MonitorTop = snapshot.MonitorTop,
            MonitorRight = snapshot.MonitorRight,
            MonitorBottom = snapshot.MonitorBottom,
            SavedUtc = DateTime.UtcNow,
        };

        status = $"Saved game window at X/Y {placement.X}, {placement.Y}, size {placement.Width}x{placement.Height} on {FormatMonitor(placement.MonitorDeviceName)}.";
        return true;
    }

    public bool TryMove(int x, int y, out string status)
    {
        if (!TryResolveWindowHandle(out var windowHandle, out status))
            return false;

        if (!SetWindowPos(windowHandle, nint.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate))
        {
            status = LastWin32Error("SetWindowPos");
            return false;
        }

        status = $"Moved game window to exact X/Y {x}, {y}.";
        return true;
    }

    public bool TryResize(int width, int height, out string status)
    {
        if (width <= 0 || height <= 0)
        {
            status = "Game window size must be positive.";
            return false;
        }

        if (!TryResolveWindowHandle(out var windowHandle, out status))
            return false;

        if (!SetWindowPos(windowHandle, nint.Zero, 0, 0, width, height, SwpNoMove | SwpNoZOrder | SwpNoActivate))
        {
            status = LastWin32Error("SetWindowPos");
            return false;
        }

        status = $"Resized game window to {width}x{height}.";
        return true;
    }

    public bool TryRestorePosition(SavedWindowPlacement placement, out string status)
    {
        if (!TryResolveWindowHandle(out var windowHandle, out status))
            return false;

        if (!TryReadWindowRect(windowHandle, out var currentRect, out status))
            return false;

        if (!TryResolveTargetMonitor(placement, windowHandle, out var monitor, out var monitorSource, out status))
            return false;

        var offsetX = placement.X - placement.MonitorLeft;
        var offsetY = placement.Y - placement.MonitorTop;
        var targetX = monitor.Bounds.Left + offsetX;
        var targetY = monitor.Bounds.Top + offsetY;
        var clampedX = ClampTopLeft(targetX, monitor.Bounds.Left, monitor.Bounds.Right);
        var clampedY = ClampTopLeft(targetY, monitor.Bounds.Top, monitor.Bounds.Bottom);
        var wasClamped = clampedX != targetX || clampedY != targetY;

        if (!SetWindowPos(windowHandle, nint.Zero, clampedX, clampedY, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate))
        {
            status = LastWin32Error("SetWindowPos");
            return false;
        }

        var sizeText = $"{Math.Max(0, currentRect.Right - currentRect.Left)}x{Math.Max(0, currentRect.Bottom - currentRect.Top)}";
        status = $"Loaded game window to X/Y {clampedX}, {clampedY} on {FormatMonitor(monitor.DeviceName)} via {monitorSource}; size preserved ({sizeText}).";
        if (wasClamped)
            status += " Target top-left was clamped into monitor bounds.";

        return true;
    }

    public bool TryRestoreSize(SavedWindowPlacement placement, out string status)
    {
        if (placement.Width <= 0 || placement.Height <= 0)
        {
            status = "Saved game window size is unavailable.";
            return false;
        }

        if (!TryResize(placement.Width, placement.Height, out status))
            return false;

        status = $"Loaded game window size {placement.Width}x{placement.Height}.";
        return true;
    }

    public static string FormatMonitor(string? deviceName)
        => string.IsNullOrWhiteSpace(deviceName) ? "unknown monitor" : deviceName;

    public static string FormatBounds(int left, int top, int right, int bottom)
        => $"{left},{top} - {right},{bottom}";

    private static bool TryResolveTargetMonitor(
        SavedWindowPlacement placement,
        nint currentWindowHandle,
        out MonitorSnapshot monitor,
        out string monitorSource,
        out string status)
    {
        var monitors = EnumerateMonitors();

        if (!string.IsNullOrWhiteSpace(placement.MonitorDeviceName))
        {
            var byDevice = monitors.FirstOrDefault(candidate =>
                string.Equals(candidate.DeviceName, placement.MonitorDeviceName, StringComparison.OrdinalIgnoreCase));
            if (byDevice.Handle != nint.Zero)
            {
                monitor = byDevice;
                monitorSource = "saved monitor device";
                status = "Saved monitor device found.";
                return true;
            }
        }

        var byBounds = monitors.FirstOrDefault(candidate =>
            candidate.Bounds.Left == placement.MonitorLeft
            && candidate.Bounds.Top == placement.MonitorTop
            && candidate.Bounds.Right == placement.MonitorRight
            && candidate.Bounds.Bottom == placement.MonitorBottom);
        if (byBounds.Handle != nint.Zero)
        {
            monitor = byBounds;
            monitorSource = "saved monitor bounds";
            status = "Saved monitor bounds found.";
            return true;
        }

        var currentMonitorHandle = MonitorFromWindow(currentWindowHandle, MonitorDefaultToNearest);
        if (currentMonitorHandle != nint.Zero && TryReadMonitor(currentMonitorHandle, out monitor, out status))
        {
            monitorSource = "current monitor fallback";
            return true;
        }

        monitor = default;
        monitorSource = "none";
        status = "No usable monitor found for window placement restore.";
        return false;
    }

    private static List<MonitorSnapshot> EnumerateMonitors()
    {
        var monitors = new List<MonitorSnapshot>();
        MonitorEnumProc callback = (nint monitorHandle, nint hdcMonitor, ref Rect monitorRect, nint data) =>
        {
            if (TryReadMonitor(monitorHandle, out var monitor, out _))
                monitors.Add(monitor);

            return true;
        };

        _ = EnumDisplayMonitors(nint.Zero, nint.Zero, callback, nint.Zero);
        return monitors;
    }

    private static bool IsUsableWindow(nint windowHandle)
        => windowHandle != nint.Zero
        && IsWindow(windowHandle)
        && IsWindowVisible(windowHandle)
        && GetWindowRect(windowHandle, out var rect)
        && rect.Right > rect.Left
        && rect.Bottom > rect.Top;

    private static bool TryReadWindowRect(nint windowHandle, out Rect rect, out string status)
    {
        if (GetWindowRect(windowHandle, out rect))
        {
            if (rect.Right > rect.Left && rect.Bottom > rect.Top)
            {
                status = "Window rect read.";
                return true;
            }

            status = "Game window rect is empty.";
            return false;
        }

        status = LastWin32Error("GetWindowRect");
        return false;
    }

    private static bool TryReadMonitor(nint monitorHandle, out MonitorSnapshot monitor, out string status)
    {
        var info = new MonitorInfoEx
        {
            Size = Marshal.SizeOf<MonitorInfoEx>(),
        };

        if (!GetMonitorInfo(monitorHandle, ref info))
        {
            monitor = default;
            status = LastWin32Error("GetMonitorInfo");
            return false;
        }

        monitor = new MonitorSnapshot(monitorHandle, info.DeviceName, info.Monitor);
        status = "Monitor info read.";
        return true;
    }

    private static int ClampTopLeft(int value, int min, int maxExclusive)
    {
        var max = Math.Max(min, maxExclusive - 1);
        return Math.Clamp(value, min, max);
    }

    private static string LastWin32Error(string operation)
    {
        var error = Marshal.GetLastWin32Error();
        return $"{operation} failed (Win32 {error}: {new Win32Exception(error).Message}).";
    }

    private readonly record struct MonitorSnapshot(nint Handle, string DeviceName, Rect Bounds);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
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

    private delegate bool EnumWindowsProc(nint windowHandle, nint parameter);
    private delegate bool MonitorEnumProc(nint monitorHandle, nint hdcMonitor, ref Rect monitorRect, nint data);

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

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint windowHandle, out Rect rect);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint windowHandle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetMonitorInfo(nint monitorHandle, ref MonitorInfoEx monitorInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint windowHandle, nint insertAfter, int x, int y, int cx, int cy, uint flags);
}

public sealed class WindowPlacementSnapshot
{
    public static readonly WindowPlacementSnapshot Empty = new(0, 0, 0, 0, string.Empty, 0, 0, 0, 0);

    public WindowPlacementSnapshot(
        int x,
        int y,
        int width,
        int height,
        string monitorDeviceName,
        int monitorLeft,
        int monitorTop,
        int monitorRight,
        int monitorBottom)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        MonitorDeviceName = monitorDeviceName;
        MonitorLeft = monitorLeft;
        MonitorTop = monitorTop;
        MonitorRight = monitorRight;
        MonitorBottom = monitorBottom;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }
    public string MonitorDeviceName { get; }
    public int MonitorLeft { get; }
    public int MonitorTop { get; }
    public int MonitorRight { get; }
    public int MonitorBottom { get; }
}
