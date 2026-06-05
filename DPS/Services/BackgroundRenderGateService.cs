using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DPS.Services;

public sealed unsafe class BackgroundRenderGateService : IDisposable
{
    private const string DeviceDx11PostTickSignature = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 54 41 55 41 56 41 57 B8 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 2B E0 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 8B 15";
    private const string NamePlateDrawSignature = "0F B7 81 ?? ?? ?? ?? 81 A1 ?? ?? ?? ?? ?? ?? ?? ?? 81 A1 ?? ?? ?? ?? ?? ?? ?? ?? 66 C1 E0 06 0F B7 D0 66 89 91 ?? ?? ?? ?? C1 E2 0D 09 91 ?? ?? ?? ?? 09 91 ?? ?? ?? ?? E9 ?? ?? ?? ?? CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 33 C0";
    private const long HitchThresholdMs = 100;
    private const long HitchLogIntervalMs = 5_000;
    private const long TransitionDiagnosticsIntervalMs = 5_000;
    private delegate void DeviceDx11PostTickDelegate(nint instance);
    private delegate void NamePlateDrawDelegate(AtkUnitBase* addon);

    private enum RenderGateMode
    {
        None,
        Background,
        Foreground,
    }

    private Hook<DeviceDx11PostTickDelegate>? deviceDx11PostTickHook;
    private Hook<NamePlateDrawDelegate>? namePlateDrawHook;
    private bool initialized;
    private bool initializationAttempted;
    private bool pluginEnabled;
    private bool backgroundConfigured;
    private bool backgroundArmed;
    private bool foregroundArmed;
    private bool foregroundDisplayRecoveryBypass;
    private bool onlyWhenMinimized;
    private string? initializationError;
    private RenderGateMode activeMode;
    private long nextSafetyRenderTick;
    private long nextHitchLogTick;
    private long nextTransitionDiagnosticTick;
    private bool lastTransitionBypassActive;
    private double maxRenderHookDelayMs;
    private int safetyFrameIntervalMs = 5_000;
    private int backgroundThrottleSleepMs;

    public bool HooksActive => initialized && deviceDx11PostTickHook?.IsEnabled == true;
    public bool IsNoRenderActive => activeMode != RenderGateMode.None;
    public bool IsBackgroundNoRenderActive => activeMode == RenderGateMode.Background;
    public bool IsForegroundNoRenderActive => activeMode == RenderGateMode.Foreground;
    public bool IsForegroundNoRenderArmed => foregroundArmed;
    public bool ForegroundDisplayRecoveryBypassActive => foregroundDisplayRecoveryBypass;
    public bool InitializationAttempted => initializationAttempted;
    public bool InitializationFailed => initializationAttempted && !initialized;
    public string? InitializationError => initializationError;
    public string Status { get; private set; } = "Background no-render idle.";
    public int SafetyFrameIntervalMs => safetyFrameIntervalMs;
    public int BackgroundThrottleSleepMs => backgroundThrottleSleepMs;
    public double SafetyFramesPerMinute => safetyFrameIntervalMs <= 0 ? 0d : 60_000d / safetyFrameIntervalMs;
    public bool TransitionBypassActive { get; private set; }
    public double MaxRenderHookDelayMs => maxRenderHookDelayMs;
    public string ActiveGateMode => activeMode.ToString();

    public void RefreshState(Configuration configuration)
    {
        safetyFrameIntervalMs = Math.Clamp(configuration.BackgroundSafetyFrameIntervalSeconds, 1, 60) * 1_000;
        backgroundThrottleSleepMs = Math.Clamp(configuration.BackgroundThrottleSleepMs, 0, 500);
        onlyWhenMinimized = configuration.BackgroundNoRenderOnlyWhenMinimized;
        pluginEnabled = configuration.PluginEnabled;
        backgroundConfigured = configuration.BackgroundNoRenderEnabled;
        backgroundArmed = pluginEnabled && backgroundConfigured;
        foregroundArmed = pluginEnabled
                       && configuration.ForegroundNoRenderEnabled
                       && configuration.ForegroundNoRenderMode == ForegroundNoRenderMode.SafeFrozenFrame;

        var anyRenderGateArmed = backgroundArmed || foregroundArmed;

        if (activeMode == RenderGateMode.Foreground && !foregroundArmed)
            SetNoRenderActive(RenderGateMode.None);
        else if (activeMode == RenderGateMode.Background && !backgroundArmed)
            SetNoRenderActive(RenderGateMode.None);

        if (anyRenderGateArmed && !initializationAttempted)
            InitializeHooks();

        if (anyRenderGateArmed && initialized)
        {
            EnableHooks();
            SetIdleStatus();
            return;
        }

        DisableHooks();
        SetInactiveStatus();
    }

    public void SetForegroundDisplayRecoveryBypass(bool active)
    {
        if (foregroundDisplayRecoveryBypass == active)
            return;

        foregroundDisplayRecoveryBypass = active;
        if (active && activeMode == RenderGateMode.Foreground)
            SetNoRenderActive(RenderGateMode.None);
    }

    public void Dispose()
    {
        DisableHooks();
        deviceDx11PostTickHook?.Dispose();
        namePlateDrawHook?.Dispose();
    }

    private void InitializeHooks()
    {
        initializationAttempted = true;
        initializationError = null;

        try
        {
            var deviceDx11PostTickAddress = Plugin.SigScanner.ScanText(DeviceDx11PostTickSignature);
            deviceDx11PostTickHook = Plugin.GameInteropProvider.HookFromAddress<DeviceDx11PostTickDelegate>(
                deviceDx11PostTickAddress,
                DeviceDx11PostTickDetour);

            try
            {
                var namePlateDrawAddress = Plugin.SigScanner.ScanText(NamePlateDrawSignature);
                namePlateDrawHook = Plugin.GameInteropProvider.HookFromAddress<NamePlateDrawDelegate>(
                    namePlateDrawAddress,
                    NamePlateDrawDetour);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[DPS] Background no-render nameplate hook initialization failed. Continuing with render gate only.");
            }

            initialized = true;
            Status = namePlateDrawHook == null
                ? "Background no-render hook ready. Nameplate hook unavailable."
                : "Background no-render hooks ready.";
        }
        catch (Exception ex)
        {
            initialized = false;
            initializationError = ex.Message;
            Status = $"Background no-render hook init failed: {ex.Message}";
            Plugin.Log.Warning(ex, "[DPS] Background no-render hook initialization failed.");
        }
    }

    private void EnableHooks()
    {
        deviceDx11PostTickHook?.Enable();
        namePlateDrawHook?.Enable();
    }

    private void DisableHooks()
    {
        deviceDx11PostTickHook?.Disable();
        namePlateDrawHook?.Disable();

        if (activeMode == RenderGateMode.Background)
            Plugin.Log.Information("[DPS] Background no-render mode exited.");

        activeMode = RenderGateMode.None;
        nextSafetyRenderTick = 0;
    }

    private void DeviceDx11PostTickDetour(nint instance)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        var transitionBypass = false;

        try
        {
            var framework = Framework.Instance();
            transitionBypass = IsAreaTransitionActive();
            TransitionBypassActive = transitionBypass;
            if ((!backgroundArmed && !foregroundArmed) || framework == null || !Plugin.ClientState.IsLoggedIn)
            {
                SetNoRenderActive(RenderGateMode.None);
                deviceDx11PostTickHook!.Original(instance);
                return;
            }

            if (transitionBypass)
            {
                SetNoRenderActive(RenderGateMode.None);
                deviceDx11PostTickHook!.Original(instance);
                return;
            }

            if (foregroundArmed)
            {
                if (foregroundDisplayRecoveryBypass)
                {
                    SetNoRenderActive(RenderGateMode.None);
                    deviceDx11PostTickHook!.Original(instance);
                    return;
                }

                SetNoRenderActive(RenderGateMode.Foreground);
                RenderSafetyFrameIfDue(instance);
                return;
            }

            if (!ShouldSuppressRender(framework))
            {
                SetNoRenderActive(RenderGateMode.None);
                deviceDx11PostTickHook!.Original(instance);
                return;
            }

            SetNoRenderActive(RenderGateMode.Background);

            if (RenderSafetyFrameIfDue(instance))
                return;

            var uiModule = UIModule.Instance();
            if (uiModule != null && uiModule->ShouldLimitFps() && backgroundThrottleSleepMs > 0)
                Thread.Sleep(backgroundThrottleSleepMs);
        }
        finally
        {
            RecordHookDelay(startTimestamp, transitionBypass);
        }
    }

    private void NamePlateDrawDetour(AtkUnitBase* addon)
    {
        if (IsNoRenderActive)
            return;

        namePlateDrawHook!.Original(addon);
    }

    private bool ShouldSuppressRender(Framework* framework)
    {
        if (onlyWhenMinimized)
            return framework->GameWindow != null && IsIconic(framework->GameWindow->WindowHandle);

        return framework->WindowInactive;
    }

    private bool RenderSafetyFrameIfDue(nint instance)
    {
        var currentTick = Environment.TickCount64;
        if (nextSafetyRenderTick - currentTick >= 0)
            return false;

        nextSafetyRenderTick = currentTick + safetyFrameIntervalMs;
        deviceDx11PostTickHook!.Original(instance);
        return true;
    }

    private static bool IsAreaTransitionActive()
        => Plugin.Condition[ConditionFlag.BetweenAreas]
        || Plugin.Condition[ConditionFlag.BetweenAreas51];

    private void RecordHookDelay(long startTimestamp, bool transitionBypass)
    {
        var elapsedMs = Stopwatch.GetElapsedTime(startTimestamp).TotalMilliseconds;
        if (elapsedMs > maxRenderHookDelayMs)
            maxRenderHookDelayMs = elapsedMs;

        var now = Environment.TickCount64;
        if (elapsedMs >= HitchThresholdMs && (nextHitchLogTick == 0 || nextHitchLogTick - now <= 0))
        {
            nextHitchLogTick = now + HitchLogIntervalMs;
            Plugin.Log.Warning(
                "[DPS][HITCH] render hook slow elapsedMs={ElapsedMs:0.0}; mode={Mode}; activeGate={ActiveGate}; hooksActive={HooksActive}; transition={Transition}; maxRenderHookDelayMs={MaxDelayMs:0.0}; throttleSleepMs={ThrottleSleepMs}.",
                elapsedMs,
                BuildConfiguredModeText(),
                activeMode,
                HooksActive,
                transitionBypass,
                maxRenderHookDelayMs,
                backgroundThrottleSleepMs);
        }

        if (transitionBypass != lastTransitionBypassActive
            || (transitionBypass && (nextTransitionDiagnosticTick == 0 || nextTransitionDiagnosticTick - now <= 0)))
        {
            lastTransitionBypassActive = transitionBypass;
            nextTransitionDiagnosticTick = now + TransitionDiagnosticsIntervalMs;
            Plugin.Log.Information(
                "[DPS][HITCH] transition render bypass active={Transition}; mode={Mode}; activeGate={ActiveGate}; hooksActive={HooksActive}; maxRenderHookDelayMs={MaxDelayMs:0.0}.",
                transitionBypass,
                BuildConfiguredModeText(),
                activeMode,
                HooksActive,
                maxRenderHookDelayMs);
        }
    }

    private string BuildConfiguredModeText()
    {
        if (foregroundArmed)
            return foregroundDisplayRecoveryBypass
                ? "foreground-safe-frozen-recovery-bypass"
                : "foreground-safe-frozen";

        if (backgroundArmed)
            return "background";

        return "none";
    }

    private void SetNoRenderActive(RenderGateMode mode)
    {
        if (activeMode == mode)
        {
            if (mode == RenderGateMode.None)
                SetIdleStatus();
            return;
        }

        var wasBackgroundActive = activeMode == RenderGateMode.Background;
        activeMode = mode;

        if (wasBackgroundActive && mode != RenderGateMode.Background)
            Plugin.Log.Information("[DPS] Background no-render mode exited.");

        if (mode == RenderGateMode.Background)
        {
            Status = onlyWhenMinimized
                ? "Background no-render ACTIVE. Window is minimized/iconic."
                : "Background no-render ACTIVE. Window is inactive."
            ;
            Plugin.Log.Information("[DPS] Background no-render mode entered.");
            return;
        }

        if (mode == RenderGateMode.Foreground)
        {
            if (backgroundArmed)
                Status = "Background no-render paused while foreground no-render is active.";
            else
                SetIdleStatus();

            return;
        }

        SetIdleStatus();
    }

    private void SetIdleStatus()
    {
        if (!backgroundArmed)
        {
            SetInactiveStatus();
            return;
        }

        Status = onlyWhenMinimized
            ? "Background no-render armed. Minimize the window to trigger it."
            : "Background no-render armed. Alt-tab away to trigger it.";
    }

    private void SetInactiveStatus()
    {
        if (!backgroundConfigured)
        {
            Status = "Background no-render disabled.";
            return;
        }

        if (!pluginEnabled)
        {
            Status = "Background no-render waiting for plugin enable.";
            return;
        }

        if (InitializationFailed)
        {
            Status = $"Background no-render hook init failed: {initializationError ?? "unknown error"}";
            return;
        }

        Status = "Background no-render idle.";
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint windowHandle);
}
