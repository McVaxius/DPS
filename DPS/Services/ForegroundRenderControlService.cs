using FFXIVClientStructs.FFXIV.Client.Graphics.Render;

namespace DPS.Services;

public sealed unsafe class ForegroundRenderControlService : IDisposable
{
    private const int ActiveRenderFlagOffset = 0x38358;
    private const long UnavailableWarningIntervalMs = 30_000;
    private const byte RenderOnByte = 0;
    private const byte RenderOffByte = 1;

    private readonly BackgroundRenderGateService renderGate;
    private Configuration? configuration;
    private bool disabledByDps;
    private bool disposed;
    private bool displayRecoveryBypassActive;
    private long nextUnavailableWarningTick;
    private long nextRewriteInformationTick;

    public ForegroundRenderControlService(BackgroundRenderGateService renderGate)
    {
        this.renderGate = renderGate;
    }

    public bool RenderDisabledByDps
        => !disposed
        && !displayRecoveryBypassActive
        && (disabledByDps
            || (SafeModeRequested && renderGate.HooksActive && !renderGate.InitializationFailed));

    public string Status { get; private set; } = "Foreground no-render disabled.";
    public bool DisplayRecoveryBypassActive => displayRecoveryBypassActive;

    private bool SafeModeRequested
        => configuration?.PluginEnabled == true
        && configuration.ForegroundNoRenderEnabled
        && configuration.ForegroundNoRenderMode == ForegroundNoRenderMode.SafeFrozenFrame;

    public void RefreshState(Configuration configuration)
    {
        if (disposed)
            return;

        this.configuration = configuration;

        if (displayRecoveryBypassActive)
        {
            RestoreRender("foreground display recovery refresh");
            UpdateStatus();
            return;
        }

        if (ShouldUseLegacyRenderByte(configuration))
        {
            DisableRender("configuration");
            return;
        }

        if (disabledByDps)
        {
            RestoreRender("configuration");
            return;
        }

        UpdateStatus();
    }

    public void Tick(Configuration configuration)
    {
        if (disposed)
            return;

        this.configuration = configuration;

        if (displayRecoveryBypassActive)
        {
            RestoreRender("foreground display recovery");
            UpdateStatus();
            return;
        }

        if (ShouldUseLegacyRenderByte(configuration))
        {
            DisableRender("framework tick");
            return;
        }

        if (disabledByDps)
        {
            RestoreRender("framework tick");
            return;
        }

        UpdateStatus();
    }

    public void SetDisplayRecoveryBypass(bool active)
    {
        if (displayRecoveryBypassActive == active)
            return;

        displayRecoveryBypassActive = active;
        UpdateStatus();
    }

    public bool DisableRender(string source)
    {
        if (disposed)
            return false;

        var wasDisabledByDps = disabledByDps;

        // Live-observed semantics: 1 = render off, 0 = render on.
        if (!TryWriteRenderByte(RenderOffByte, out var before, out var after, out var error))
        {
            Status = $"Foreground no-render unavailable: {error}";
            WarnUnavailableThrottled($"Could not disable foreground render via {source}: {error}");
            return false;
        }

        disabledByDps = after == RenderOffByte;

        Status = after == RenderOffByte
            ? "Foreground no-render ACTIVE. Render byte is 1."
            : $"Foreground no-render armed, but render byte is {FormatByte(after)}.";

        if (!wasDisabledByDps || before != RenderOffByte)
            LogDisableWrite(source, before, after, wasDisabledByDps && before != RenderOffByte);

        return after == RenderOffByte;
    }

    public bool RestoreRender(string source)
    {
        if (disposed)
            return false;

        var wasDisabledByDps = disabledByDps;
        var shouldWriteRenderByte = disabledByDps
                                 || configuration?.ForegroundNoRenderMode == ForegroundNoRenderMode.LegacyBlackScreen;
        if (!shouldWriteRenderByte)
        {
            UpdateStatus();
            return true;
        }

        if (!TryWriteRenderByte(RenderOnByte, out var before, out var after, out var error))
        {
            Status = $"Foreground no-render restore pending: {error}";
            WarnUnavailableThrottled($"Could not restore foreground render via {source}: {error}");
            return false;
        }

        if (after == RenderOnByte)
            disabledByDps = false;

        Status = after == RenderOnByte
            ? "Foreground no-render disabled. Render byte restored to 0."
            : $"Foreground no-render restore requested, but render byte is still {FormatByte(after)}.";

        if (wasDisabledByDps || before != RenderOnByte || after != RenderOnByte)
        {
            Plugin.Log.Information("[DPS] Foreground render byte restore via {Source}: before={Before} requested=0 after={After}.",
                source, FormatByte(before), FormatByte(after));
        }

        return after == RenderOnByte;
    }

    public ForegroundRenderDiagnostics GetDiagnostics()
        => CreateDiagnostics();

    public string GetDiagnosticsLine()
    {
        var diagnostics = GetDiagnostics();
        var error = string.IsNullOrWhiteSpace(diagnostics.Error)
            ? string.Empty
            : $"; Error={diagnostics.Error}";

        return $"DPS DLL={diagnostics.DpsAssemblyPath}; Mode={diagnostics.ModeText}; Intent={diagnostics.IntentText}; " +
               $"Gate={diagnostics.GateText}; DisplayRecoveryBypass={diagnostics.DisplayRecoveryBypassActive}; ClientStructs DLL={diagnostics.ClientStructsAssemblyPath}; " +
               $"ClientStructs version={diagnostics.ClientStructsAssemblyVersion}; Manager={diagnostics.ManagerAddressText}; " +
               $"Flag={diagnostics.RenderFlagAddressText}; Byte={diagnostics.CurrentByteText}; Status={diagnostics.Status}{error}";
    }

    public void Dispose()
    {
        if (disposed)
            return;

        if (disabledByDps)
            RestoreRender("dispose");

        disposed = true;
        Status = "Foreground no-render disabled.";
    }

    private static bool ShouldUseLegacyRenderByte(Configuration configuration)
        => configuration.PluginEnabled
        && configuration.ForegroundNoRenderEnabled
        && configuration.ForegroundNoRenderMode == ForegroundNoRenderMode.LegacyBlackScreen;

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

    private ForegroundRenderDiagnostics CreateDiagnostics()
    {
        var dpsAssemblyPath = NormalizeAssemblyPath(typeof(ForegroundRenderControlService).Assembly.Location);
        var clientStructsAssembly = typeof(Manager).Assembly;
        var clientStructsAssemblyPath = NormalizeAssemblyPath(clientStructsAssembly.Location);
        var clientStructsAssemblyVersion = clientStructsAssembly.GetName().Version?.ToString() ?? "(unknown)";
        var mode = configuration?.ForegroundNoRenderMode ?? ForegroundNoRenderMode.SafeFrozenFrame;
        var pluginEnabled = configuration?.PluginEnabled == true;
        var foregroundEnabled = configuration?.ForegroundNoRenderEnabled == true;
        var byteModeUsed = mode == ForegroundNoRenderMode.LegacyBlackScreen || disabledByDps;

        Manager* manager = null;
        byte* flagPointer = null;
        byte? currentByte = null;
        string? error = null;

        if (mode == ForegroundNoRenderMode.SafeFrozenFrame && renderGate.InitializationFailed)
            error = renderGate.InitializationError ?? "Render gate hook initialization failed.";

        if (byteModeUsed)
        {
            if (TryGetRenderFlagPointer(out manager, out flagPointer, out var pointerError))
            {
                try
                {
                    currentByte = *flagPointer;
                }
                catch (Exception ex)
                {
                    error = AppendError(error, $"render flag read failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
            else
            {
                error = AppendError(error, pointerError);
            }
        }

        return new ForegroundRenderDiagnostics(
            dpsAssemblyPath,
            clientStructsAssemblyPath,
            clientStructsAssemblyVersion,
            mode,
            pluginEnabled,
            foregroundEnabled,
            renderGate.InitializationAttempted,
            renderGate.InitializationFailed,
            renderGate.HooksActive,
            renderGate.IsForegroundNoRenderArmed,
            renderGate.IsForegroundNoRenderActive,
            displayRecoveryBypassActive,
            byteModeUsed,
            (nint)(void*)manager,
            (nint)flagPointer,
            currentByte,
            Status,
            error);
    }

    private void UpdateStatus()
    {
        if (configuration == null || !configuration.ForegroundNoRenderEnabled)
        {
            Status = "Foreground no-render disabled.";
            return;
        }

        if (!configuration.PluginEnabled)
        {
            Status = "Foreground no-render waiting for plugin enable.";
            return;
        }

        if (displayRecoveryBypassActive)
        {
            Status = "Foreground no-render paused for display recovery. Normal rendering allowed.";
            return;
        }

        if (configuration.ForegroundNoRenderMode == ForegroundNoRenderMode.LegacyBlackScreen)
        {
            Status = disabledByDps
                ? "Foreground no-render ACTIVE. Render byte is 1."
                : "Foreground no-render armed. Legacy render byte mode ready.";
            return;
        }

        if (configuration.ForegroundNoRenderMode != ForegroundNoRenderMode.SafeFrozenFrame)
        {
            Status = $"Foreground no-render unavailable: unsupported mode {configuration.ForegroundNoRenderMode}.";
            return;
        }

        if (renderGate.InitializationFailed)
        {
            Status = $"Foreground no-render unavailable: {renderGate.InitializationError ?? "render gate hook initialization failed"}";
            return;
        }

        if (!renderGate.InitializationAttempted)
        {
            Status = "Foreground no-render armed. Render gate hook not initialized yet.";
            return;
        }

        if (!renderGate.HooksActive)
        {
            Status = "Foreground no-render unavailable: render gate hook is not active.";
            return;
        }

        Status = renderGate.IsForegroundNoRenderActive
            ? "Foreground no-render ACTIVE. Render gate is suppressing frames."
            : "Foreground no-render armed. Render gate ready.";
    }

    private static string NormalizeAssemblyPath(string path)
        => string.IsNullOrWhiteSpace(path) ? "(dynamic or unavailable)" : path;

    private static string AppendError(string? current, string next)
        => string.IsNullOrWhiteSpace(current) ? next : $"{current}; {next}";

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

    private void LogDisableWrite(string source, byte? before, byte? after, bool throttle)
    {
        if (throttle)
        {
            var now = Environment.TickCount64;
            if (nextRewriteInformationTick != 0 && nextRewriteInformationTick - now > 0)
                return;

            nextRewriteInformationTick = now + UnavailableWarningIntervalMs;
        }

        Plugin.Log.Information("[DPS] Foreground render byte write via {Source}: before={Before} requested=1 after={After}.",
            source, FormatByte(before), FormatByte(after));
    }

    private static string FormatByte(byte? value)
        => value.HasValue ? $"{value.Value} (0x{value.Value:X2})" : "(unavailable)";
}

public sealed record ForegroundRenderDiagnostics(
    string DpsAssemblyPath,
    string ClientStructsAssemblyPath,
    string ClientStructsAssemblyVersion,
    ForegroundNoRenderMode Mode,
    bool PluginEnabled,
    bool ForegroundNoRenderEnabled,
    bool HookInitializationAttempted,
    bool HookInitializationFailed,
    bool HooksActive,
    bool GateArmed,
    bool GateActive,
    bool DisplayRecoveryBypassActive,
    bool ByteModeUsed,
    nint ManagerAddress,
    nint RenderFlagAddress,
    byte? CurrentByte,
    string Status,
    string? Error)
{
    public string ModeText
        => Mode == ForegroundNoRenderMode.LegacyBlackScreen
            ? "legacy black screen"
            : "safe frozen frame";

    public string IntentText
    {
        get
        {
            if (!ForegroundNoRenderEnabled)
                return "ON";

            return PluginEnabled ? $"OFF requested ({ModeText})" : "OFF requested, waiting for plugin enable";
        }
    }

    public string GateText
    {
        get
        {
            if (Mode != ForegroundNoRenderMode.SafeFrozenFrame)
                return "NOT USED";

            if (DisplayRecoveryBypassActive)
                return "BYPASS";

            if (HookInitializationFailed)
                return "FAILED";

            if (GateActive)
                return "ACTIVE";

            if (GateArmed)
                return "ARMED";

            return HooksActive ? "READY" : HookInitializationAttempted ? "IDLE" : "NOT INITIALIZED";
        }
    }

    public string ManagerAddressText => ByteModeUsed ? FormatAddress(ManagerAddress) : "(not used)";
    public string RenderFlagAddressText => ByteModeUsed ? FormatAddress(RenderFlagAddress) : "(not used)";
    public string CurrentByteText => ByteModeUsed ? FormatByte(CurrentByte) : "(not used)";

    private static string FormatByte(byte? value)
        => value.HasValue ? $"{value.Value} (0x{value.Value:X2})" : "(unavailable)";

    private static string FormatAddress(nint address)
    {
        if (address == nint.Zero)
            return "0x0";

        if (IntPtr.Size == 8)
            return $"0x{unchecked((ulong)address.ToInt64()):X}";

        return $"0x{unchecked((uint)address.ToInt32()):X}";
    }
}
