using FFXIVClientStructs.FFXIV.Client.Graphics.Render;

namespace DPS.Services;

public sealed unsafe class ForegroundRenderControlService : IDisposable
{
    private const int ActiveRenderFlagOffset = 0x38358;
    private const long UnavailableWarningIntervalMs = 30_000;

    private bool disabledByDps;
    private bool disposed;
    private long nextUnavailableWarningTick;

    public bool RenderDisabledByDps => disabledByDps;
    public string Status { get; private set; } = "Foreground no-render disabled.";

    public void RefreshState(Configuration configuration)
    {
        if (disposed)
            return;

        if (configuration.PluginEnabled && configuration.ForegroundNoRenderEnabled)
        {
            DisableRender("configuration");
            return;
        }

        if (disabledByDps)
        {
            RestoreRender("configuration");
            return;
        }

        Status = configuration.ForegroundNoRenderEnabled
            ? "Foreground no-render waiting for plugin enable."
            : "Foreground no-render disabled.";
    }

    public bool DisableRender(string source)
    {
        if (disposed)
            return false;

        var wasDisabledByDps = disabledByDps;

        // 0 = render off, 1 = render on.
        if (!TryWriteRenderByte(0, out var before, out var after, out var error))
        {
            Status = $"Foreground no-render unavailable: {error}";
            WarnUnavailableThrottled($"Could not disable foreground render via {source}: {error}");
            return false;
        }

        if (after == 0)
            disabledByDps = true;

        Status = after == 0
            ? "Foreground no-render ACTIVE. Render byte is 0."
            : $"Foreground no-render armed, but render byte is {FormatByte(after)}.";

        if (!wasDisabledByDps || before != 0)
            Plugin.Log.Information("[DPS] Foreground render byte write via {Source}: before={Before} requested=0 after={After}.",
                source, FormatByte(before), FormatByte(after));

        return after == 0;
    }

    public bool RestoreRender(string source)
    {
        if (disposed)
            return false;

        if (!disabledByDps)
        {
            Status = "Foreground no-render disabled.";
            return true;
        }

        if (!TryWriteRenderByte(1, out var before, out var after, out var error))
        {
            Status = $"Foreground no-render restore pending: {error}";
            WarnUnavailableThrottled($"Could not restore foreground render via {source}: {error}");
            return false;
        }

        if (after != 0)
            disabledByDps = false;

        Status = after != 0
            ? "Foreground no-render disabled. Render byte restored to 1."
            : "Foreground no-render restore requested, but render byte is still 0.";

        Plugin.Log.Information("[DPS] Foreground render byte restore via {Source}: before={Before} requested=1 after={After}.",
            source, FormatByte(before), FormatByte(after));

        return after != 0;
    }

    public ForegroundRenderDiagnostics GetDiagnostics()
        => CreateDiagnostics();

    public string GetDiagnosticsLine()
    {
        var diagnostics = GetDiagnostics();
        var error = string.IsNullOrWhiteSpace(diagnostics.Error)
            ? string.Empty
            : $"; Error={diagnostics.Error}";

        return $"DPS DLL={diagnostics.DpsAssemblyPath}; ClientStructs DLL={diagnostics.ClientStructsAssemblyPath}; " +
               $"ClientStructs version={diagnostics.ClientStructsAssemblyVersion}; Manager={diagnostics.ManagerAddressText}; " +
               $"Flag={diagnostics.RenderFlagAddressText}; Byte={diagnostics.CurrentByteText}{error}";
    }

    public void Dispose()
    {
        if (disposed)
            return;

        if (disabledByDps)
            RestoreRender("dispose");

        disposed = true;
    }

    private static bool TryWriteRenderByte(byte value, out byte? before, out byte? after, out string error)
    {
        before = null;
        after = null;

        if (!TryGetRenderFlagPointer(out _, out var flagPointer, out error))
            return false;

        try
        {
            before = *flagPointer;
            *flagPointer = value;
            after = *flagPointer;
            return true;
        }
        catch (Exception ex)
        {
            error = $"render flag write failed: {ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }

    private static bool TryGetRenderFlagPointer(out Manager* manager, out byte* flagPointer, out string error)
    {
        manager = null;
        flagPointer = null;
        error = string.Empty;

        try
        {
            manager = Manager.Instance();
        }
        catch (Exception ex)
        {
            error = $"Render.Manager.Instance() threw {ex.GetType().Name}: {ex.Message}";
            return false;
        }

        if (manager == null)
        {
            error = "Render.Manager.Instance() returned null.";
            return false;
        }

        flagPointer = (byte*)manager + ActiveRenderFlagOffset;
        return true;
    }

    private static ForegroundRenderDiagnostics CreateDiagnostics()
    {
        var dpsAssemblyPath = NormalizeAssemblyPath(typeof(ForegroundRenderControlService).Assembly.Location);
        var clientStructsAssembly = typeof(Manager).Assembly;
        var clientStructsAssemblyPath = NormalizeAssemblyPath(clientStructsAssembly.Location);
        var clientStructsAssemblyVersion = clientStructsAssembly.GetName().Version?.ToString() ?? "(unknown)";

        Manager* manager = null;
        byte* flagPointer = null;
        byte? currentByte = null;
        string? error = null;

        if (TryGetRenderFlagPointer(out manager, out flagPointer, out var pointerError))
        {
            try
            {
                currentByte = *flagPointer;
            }
            catch (Exception ex)
            {
                error = $"render flag read failed: {ex.GetType().Name}: {ex.Message}";
            }
        }
        else
        {
            error = pointerError;
        }

        return new ForegroundRenderDiagnostics(
            dpsAssemblyPath,
            clientStructsAssemblyPath,
            clientStructsAssemblyVersion,
            (nint)(void*)manager,
            (nint)flagPointer,
            currentByte,
            error);
    }

    private static string NormalizeAssemblyPath(string path)
        => string.IsNullOrWhiteSpace(path) ? "(dynamic or unavailable)" : path;

    private void WarnUnavailableThrottled(string message)
    {
        var now = Environment.TickCount64;
        if (nextUnavailableWarningTick != 0 && nextUnavailableWarningTick - now > 0)
            return;

        nextUnavailableWarningTick = now + UnavailableWarningIntervalMs;
        var diagnostics = CreateDiagnostics();
        Plugin.Log.Warning("[DPS] {Message} DPS={Dps}; ClientStructs={ClientStructs}; ClientStructsVersion={ClientStructsVersion}; Manager={Manager}; Flag={Flag}; Byte={Byte}.",
            message,
            diagnostics.DpsAssemblyPath,
            diagnostics.ClientStructsAssemblyPath,
            diagnostics.ClientStructsAssemblyVersion,
            diagnostics.ManagerAddressText,
            diagnostics.RenderFlagAddressText,
            diagnostics.CurrentByteText);
    }

    private static string FormatByte(byte? value)
        => value.HasValue ? $"{value.Value} (0x{value.Value:X2})" : "(unavailable)";
}

public sealed record ForegroundRenderDiagnostics(
    string DpsAssemblyPath,
    string ClientStructsAssemblyPath,
    string ClientStructsAssemblyVersion,
    nint ManagerAddress,
    nint RenderFlagAddress,
    byte? CurrentByte,
    string? Error)
{
    public string ManagerAddressText => FormatAddress(ManagerAddress);
    public string RenderFlagAddressText => FormatAddress(RenderFlagAddress);
    public string CurrentByteText => CurrentByte.HasValue ? $"{CurrentByte.Value} (0x{CurrentByte.Value:X2})" : "(unavailable)";

    private static string FormatAddress(nint address)
    {
        if (address == nint.Zero)
            return "0x0";

        if (IntPtr.Size == 8)
            return $"0x{unchecked((ulong)address.ToInt64()):X}";

        return $"0x{unchecked((uint)address.ToInt32()):X}";
    }
}
