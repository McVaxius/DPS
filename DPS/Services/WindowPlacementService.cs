using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DPS.Services;

public sealed class WindowPlacementService
{
    private const int ErrorSuccess = 0;
    private const uint MonitorInfoPrimary = 0x00000001;
    private const uint MonitorDefaultToNearest = 0x00000002;
    private const uint QdcOnlyActivePaths = 0x00000002;
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
            MonitorDevicePath = FindMonitorDevicePath(snapshot.MonitorDeviceName),
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

    public bool TryRestoreSizeWithReadback(SavedWindowPlacement placement, out string status)
    {
        if (placement.Width <= 0 || placement.Height <= 0)
        {
            status = "Saved game window size is unavailable.";
            return false;
        }

        if (!TryResolveWindowHandle(out var windowHandle, out status))
            return false;

        if (!SetWindowPos(windowHandle, nint.Zero, 0, 0, placement.Width, placement.Height, SwpNoMove | SwpNoZOrder | SwpNoActivate))
        {
            status = LastWin32Error("SetWindowPos");
            return false;
        }

        if (!TryReadWindowRect(windowHandle, out var rect, out var readStatus))
        {
            status = $"Requested game window size {placement.Width}x{placement.Height}, but readback failed: {readStatus}";
            return false;
        }

        var actualWidth = Math.Max(0, rect.Right - rect.Left);
        var actualHeight = Math.Max(0, rect.Bottom - rect.Top);
        if (actualWidth != placement.Width || actualHeight != placement.Height)
        {
            status = $"Requested game window size {placement.Width}x{placement.Height}, but readback is {actualWidth}x{actualHeight}.";
            return false;
        }

        status = $"Loaded game window size {placement.Width}x{placement.Height}; readback verified.";
        return true;
    }

    public bool TryRestorePositionAndSize(SavedWindowPlacement placement, out string status)
    {
        if (!TryRestorePosition(placement, out var positionStatus))
        {
            status = $"Window + size load failed before size restore: {positionStatus}";
            return false;
        }

        if (!TryRestoreSize(placement, out var sizeStatus))
        {
            status = $"Loaded saved window position, but size restore failed: {sizeStatus}";
            return false;
        }

        status = $"Loaded saved window position and size. {positionStatus} {sizeStatus}";
        return true;
    }

    public static string FormatMonitor(string? deviceName)
        => string.IsNullOrWhiteSpace(deviceName) ? "unknown monitor" : deviceName;

    public static string FormatBounds(int left, int top, int right, int bottom)
        => $"{left},{top} - {right},{bottom}";

    internal bool TryMoveSavedPlacementToMonitor(
        SavedWindowPlacement placement,
        string monitorDevicePath,
        out WindowPlacementMonitor? connectedTarget,
        out int translatedX,
        out int translatedY,
        out string status)
    {
        connectedTarget = null;
        translatedX = placement.X;
        translatedY = placement.Y;

        if (string.IsNullOrWhiteSpace(monitorDevicePath))
        {
            status = "The selected monitor does not expose a Windows device path.";
            return false;
        }

        connectedTarget = EnumerateAvailableMonitors().FirstOrDefault(candidate =>
            string.Equals(candidate.MonitorDevicePath, monitorDevicePath, StringComparison.OrdinalIgnoreCase));
        if (connectedTarget == null)
        {
            status = "The selected monitor is no longer available; window placement was not changed.";
            return false;
        }

        if (!TryResolveWindowHandle(out var windowHandle, out status))
            return false;

        if (!TryReadWindowRect(windowHandle, out var currentRect, out status))
            return false;

        var offsetX = placement.X - placement.MonitorLeft;
        var offsetY = placement.Y - placement.MonitorTop;
        var targetX = connectedTarget.Left + offsetX;
        var targetY = connectedTarget.Top + offsetY;
        translatedX = ClampTopLeft(targetX, connectedTarget.Left, connectedTarget.Right);
        translatedY = ClampTopLeft(targetY, connectedTarget.Top, connectedTarget.Bottom);

        if (!SetWindowPos(windowHandle, nint.Zero, translatedX, translatedY, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate))
        {
            status = LastWin32Error("SetWindowPos");
            connectedTarget = null;
            translatedX = placement.X;
            translatedY = placement.Y;
            return false;
        }

        var sizeText = $"{Math.Max(0, currentRect.Right - currentRect.Left)}x{Math.Max(0, currentRect.Bottom - currentRect.Top)}";
        status = $"Moved game window to X/Y {translatedX}, {translatedY} on {connectedTarget.DisplayLabel}; size preserved ({sizeText}).";
        if (translatedX != targetX || translatedY != targetY)
            status += " Target top-left was clamped into monitor bounds.";

        return true;
    }

    private static bool TryResolveTargetMonitor(
        SavedWindowPlacement placement,
        nint currentWindowHandle,
        out MonitorSnapshot monitor,
        out string monitorSource,
        out string status)
    {
        var monitors = EnumerateMonitors();

        if (!string.IsNullOrWhiteSpace(placement.MonitorDevicePath))
        {
            var connectedTarget = EnumerateAvailableMonitors(monitors).FirstOrDefault(candidate =>
                string.Equals(candidate.MonitorDevicePath, placement.MonitorDevicePath, StringComparison.OrdinalIgnoreCase));
            if (connectedTarget != null)
            {
                var byDevicePath = monitors.FirstOrDefault(candidate =>
                    string.Equals(candidate.DeviceName, connectedTarget.GdiDeviceName, StringComparison.OrdinalIgnoreCase)
                    && candidate.Bounds.Left == connectedTarget.Left
                    && candidate.Bounds.Top == connectedTarget.Top
                    && candidate.Bounds.Right == connectedTarget.Right
                    && candidate.Bounds.Bottom == connectedTarget.Bottom);
                if (byDevicePath.Handle != nint.Zero)
                {
                    monitor = byDevicePath;
                    monitorSource = "saved monitor device path";
                    status = "Saved monitor device path found.";
                    return true;
                }
            }

            monitor = default;
            monitorSource = "saved monitor device path";
            status = "The saved physical monitor is not currently available; window placement was not changed.";
            return false;
        }

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

    internal static IReadOnlyList<WindowPlacementMonitor> EnumerateAvailableMonitors()
        => EnumerateAvailableMonitors(EnumerateMonitors());

    private static IReadOnlyList<WindowPlacementMonitor> EnumerateAvailableMonitors(IReadOnlyList<MonitorSnapshot> monitors)
    {
        if (monitors.Count == 0
            || GetDisplayConfigBufferSizes(QdcOnlyActivePaths, out var pathCount, out var modeCount) != ErrorSuccess
            || pathCount == 0)
        {
            return Array.Empty<WindowPlacementMonitor>();
        }

        var paths = new DisplayConfigPathInfo[(int)pathCount];
        var modes = new DisplayConfigModeInfo[(int)modeCount];
        if (QueryDisplayConfig(QdcOnlyActivePaths, ref pathCount, paths, ref modeCount, modes, nint.Zero) != ErrorSuccess)
            return Array.Empty<WindowPlacementMonitor>();

        var targets = new List<DisplayTargetCandidate>();
        for (var index = 0; index < pathCount; index++)
        {
            var path = paths[index];
            if (!path.TargetInfo.TargetAvailable)
                continue;

            var sourceName = new DisplayConfigSourceDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = DisplayConfigDeviceInfoType.GetSourceName,
                    Size = (uint)Marshal.SizeOf<DisplayConfigSourceDeviceName>(),
                    AdapterId = path.SourceInfo.AdapterId,
                    Id = path.SourceInfo.Id,
                },
            };
            if (DisplayConfigGetDeviceInfo(ref sourceName) != ErrorSuccess)
                continue;

            var monitor = monitors.FirstOrDefault(candidate =>
                string.Equals(candidate.DeviceName, sourceName.ViewGdiDeviceName, StringComparison.OrdinalIgnoreCase));
            if (monitor.Handle == nint.Zero)
                continue;

            var targetName = new DisplayConfigTargetDeviceName
            {
                Header = new DisplayConfigDeviceInfoHeader
                {
                    Type = DisplayConfigDeviceInfoType.GetTargetName,
                    Size = (uint)Marshal.SizeOf<DisplayConfigTargetDeviceName>(),
                    AdapterId = path.TargetInfo.AdapterId,
                    Id = path.TargetInfo.Id,
                },
            };
            if (DisplayConfigGetDeviceInfo(ref targetName) != ErrorSuccess
                || string.IsNullOrWhiteSpace(targetName.MonitorDevicePath))
            {
                continue;
            }

            targets.Add(new DisplayTargetCandidate(monitor, targetName));
        }

        var logicalTargets = targets
            .GroupBy(target => target.Monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderBy(target => target.TargetName.MonitorDevicePath, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(target => target.Monitor.Bounds.Left)
            .ThenBy(target => target.Monitor.Bounds.Top)
            .ThenBy(target => target.Monitor.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var primary = monitors.FirstOrDefault(monitor => monitor.IsPrimary);
        var primaryCenterX = primary.Handle == nint.Zero
            ? 0L
            : (long)primary.Bounds.Left + primary.Bounds.Right;

        return logicalTargets
            .Select(target =>
            {
                var monitor = target.Monitor;
                var targetName = target.TargetName;
                var positionLabel = monitor.IsPrimary
                    ? "Primary"
                    : (long)monitor.Bounds.Left + monitor.Bounds.Right < primaryCenterX ? "Left" : "Right";
                var friendlyModel = string.IsNullOrWhiteSpace(targetName.MonitorFriendlyDeviceName)
                    ? "Unknown model"
                    : targetName.MonitorFriendlyDeviceName;
                var connectorNumber = (ulong)targetName.ConnectorInstance + 1;
                var connectorLabel = $"{FormatConnector(targetName.OutputTechnology)} #{connectorNumber}";

                return new WindowPlacementMonitor(
                    targetName.MonitorDevicePath,
                    monitor.DeviceName,
                    friendlyModel,
                    connectorLabel,
                    positionLabel,
                    monitor.Bounds.Left,
                    monitor.Bounds.Top,
                    monitor.Bounds.Right,
                    monitor.Bounds.Bottom);
            })
            .ToArray();
    }

    private static string? FindMonitorDevicePath(string gdiDeviceName)
        => EnumerateAvailableMonitors().FirstOrDefault(candidate =>
            string.Equals(candidate.GdiDeviceName, gdiDeviceName, StringComparison.OrdinalIgnoreCase))?.MonitorDevicePath;

    private static string FormatConnector(DisplayConfigVideoOutputTechnology outputTechnology)
        => outputTechnology switch
        {
            DisplayConfigVideoOutputTechnology.Hd15 => "VGA",
            DisplayConfigVideoOutputTechnology.SVideo => "S-Video",
            DisplayConfigVideoOutputTechnology.CompositeVideo => "Composite",
            DisplayConfigVideoOutputTechnology.ComponentVideo => "Component",
            DisplayConfigVideoOutputTechnology.Dvi => "DVI",
            DisplayConfigVideoOutputTechnology.Hdmi => "HDMI",
            DisplayConfigVideoOutputTechnology.Lvds => "LVDS",
            DisplayConfigVideoOutputTechnology.Djpn => "D-JPN",
            DisplayConfigVideoOutputTechnology.Sdi => "SDI",
            DisplayConfigVideoOutputTechnology.DisplayPortExternal or DisplayConfigVideoOutputTechnology.DisplayPortEmbedded => "DisplayPort",
            DisplayConfigVideoOutputTechnology.UdiExternal or DisplayConfigVideoOutputTechnology.UdiEmbedded => "UDI",
            DisplayConfigVideoOutputTechnology.SdtvDongle => "SDTV",
            DisplayConfigVideoOutputTechnology.Miracast => "Miracast",
            DisplayConfigVideoOutputTechnology.IndirectWired => "Indirect wired",
            DisplayConfigVideoOutputTechnology.IndirectVirtual => "Indirect virtual",
            DisplayConfigVideoOutputTechnology.Internal => "Internal",
            _ => "Unknown connector",
        };

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

        monitor = new MonitorSnapshot(
            monitorHandle,
            info.DeviceName,
            (info.Flags & MonitorInfoPrimary) != 0,
            info.Monitor);
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

    private readonly record struct MonitorSnapshot(
        nint Handle,
        string DeviceName,
        bool IsPrimary,
        Rect Bounds);

    private readonly record struct DisplayTargetCandidate(
        MonitorSnapshot Monitor,
        DisplayConfigTargetDeviceName TargetName);

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

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathInfo
    {
        public DisplayConfigPathSourceInfo SourceInfo;
        public DisplayConfigPathTargetInfo TargetInfo;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathSourceInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigPathTargetInfo
    {
        public Luid AdapterId;
        public uint Id;
        public uint ModeInfoIdx;
        public DisplayConfigVideoOutputTechnology OutputTechnology;
        public DisplayConfigRotation Rotation;
        public DisplayConfigScaling Scaling;
        public DisplayConfigRational RefreshRate;
        public DisplayConfigScanLineOrdering ScanLineOrdering;

        [MarshalAs(UnmanagedType.Bool)]
        public bool TargetAvailable;

        public uint StatusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigRational
    {
        public uint Numerator;
        public uint Denominator;
    }

    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct DisplayConfigModeInfo
    {
        [FieldOffset(0)]
        private byte data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayConfigDeviceInfoHeader
    {
        public DisplayConfigDeviceInfoType Type;
        public uint Size;
        public Luid AdapterId;
        public uint Id;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigSourceDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string ViewGdiDeviceName;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DisplayConfigTargetDeviceName
    {
        public DisplayConfigDeviceInfoHeader Header;
        public uint Flags;
        public DisplayConfigVideoOutputTechnology OutputTechnology;
        public ushort EdidManufactureId;
        public ushort EdidProductCodeId;
        public uint ConnectorInstance;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string MonitorFriendlyDeviceName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string MonitorDevicePath;
    }

    private enum DisplayConfigDeviceInfoType : uint
    {
        GetSourceName = 1,
        GetTargetName = 2,
    }

    private enum DisplayConfigRotation : uint
    {
        Identity = 1,
        Rotate90 = 2,
        Rotate180 = 3,
        Rotate270 = 4,
    }

    private enum DisplayConfigScaling : uint
    {
        Identity = 1,
        Centered = 2,
        Stretched = 3,
        AspectRatioCenteredMax = 4,
        Custom = 5,
        Preferred = 128,
    }

    private enum DisplayConfigScanLineOrdering : uint
    {
        Unspecified = 0,
        Progressive = 1,
        Interlaced = 2,
        InterlacedUpperFieldFirst = Interlaced,
        InterlacedLowerFieldFirst = 3,
    }

    private enum DisplayConfigVideoOutputTechnology : uint
    {
        Other = 0xFFFFFFFF,
        Hd15 = 0,
        SVideo = 1,
        CompositeVideo = 2,
        ComponentVideo = 3,
        Dvi = 4,
        Hdmi = 5,
        Lvds = 6,
        Djpn = 8,
        Sdi = 9,
        DisplayPortExternal = 10,
        DisplayPortEmbedded = 11,
        UdiExternal = 12,
        UdiEmbedded = 13,
        SdtvDongle = 14,
        Miracast = 15,
        IndirectWired = 16,
        IndirectVirtual = 17,
        Internal = 0x80000000,
    }

    private delegate bool EnumWindowsProc(nint windowHandle, nint parameter);
    private delegate bool MonitorEnumProc(nint monitorHandle, nint hdcMonitor, ref Rect monitorRect, nint data);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(nint hdc, nint clipRect, MonitorEnumProc callback, nint data);

    [DllImport("user32.dll")]
    private static extern int GetDisplayConfigBufferSizes(uint flags, out uint pathCount, out uint modeCount);

    [DllImport("user32.dll")]
    private static extern int QueryDisplayConfig(
        uint flags,
        ref uint pathCount,
        [Out] DisplayConfigPathInfo[] paths,
        ref uint modeCount,
        [Out] DisplayConfigModeInfo[] modes,
        nint currentTopologyId);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigSourceDeviceName requestPacket);

    [DllImport("user32.dll")]
    private static extern int DisplayConfigGetDeviceInfo(ref DisplayConfigTargetDeviceName requestPacket);

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

internal sealed record WindowPlacementMonitor(
    string MonitorDevicePath,
    string GdiDeviceName,
    string FriendlyModel,
    string ConnectorLabel,
    string PositionLabel,
    int Left,
    int Top,
    int Right,
    int Bottom)
{
    public string DisplayLabel
    {
        get
        {
            var displayName = GdiDeviceName.StartsWith(@"\\.\", StringComparison.Ordinal)
                ? GdiDeviceName[4..]
                : GdiDeviceName;
            return $"{FriendlyModel} — {ConnectorLabel} — {PositionLabel} — {Math.Max(0, Right - Left)}x{Math.Max(0, Bottom - Top)} at {Left},{Top} [{displayName}]";
        }
    }
}
