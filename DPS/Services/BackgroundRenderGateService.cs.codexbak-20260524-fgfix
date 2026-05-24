using System;
using System.Runtime.InteropServices;
using System.Threading;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.System.Framework;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace DPS.Services;

public sealed unsafe class BackgroundRenderGateService : IDisposable
{
    private const string DeviceDx11PostTickSignature = "48 89 5C 24 ?? 48 89 6C 24 ?? 48 89 74 24 ?? 57 41 54 41 55 41 56 41 57 B8 ?? ?? ?? ?? E8 ?? ?? ?? ?? 48 2B E0 48 8B 05 ?? ?? ?? ?? 48 33 C4 48 89 84 24 ?? ?? ?? ?? 8B 15";
    private const string NamePlateDrawSignature = "0F B7 81 ?? ?? ?? ?? 81 A1 ?? ?? ?? ?? ?? ?? ?? ?? 81 A1 ?? ?? ?? ?? ?? ?? ?? ?? 66 C1 E0 06 0F B7 D0 66 89 91 ?? ?? ?? ?? C1 E2 0D 09 91 ?? ?? ?? ?? 09 91 ?? ?? ?? ?? E9 ?? ?? ?? ?? CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC CC 33 C0";
    private delegate void DeviceDx11PostTickDelegate(nint instance);
    private delegate void NamePlateDrawDelegate(AtkUnitBase* addon);

    private Hook<DeviceDx11PostTickDelegate>? deviceDx11PostTickHook;
    private Hook<NamePlateDrawDelegate>? namePlateDrawHook;
    private bool initialized;
    private bool initializationAttempted;
    private bool armed;
    private bool onlyWhenMinimized;
    private long nextSafetyRenderTick;
    private int safetyFrameIntervalMs = 5_000;
    private int backgroundThrottleSleepMs = 50;

    public bool HooksActive => initialized && deviceDx11PostTickHook?.IsEnabled == true;
    public bool IsNoRenderActive { get; private set; }
    public string Status { get; private set; } = "Background no-render idle.";
    public int SafetyFrameIntervalMs => safetyFrameIntervalMs;
    public int BackgroundThrottleSleepMs => backgroundThrottleSleepMs;
    public double SafetyFramesPerMinute => safetyFrameIntervalMs <= 0 ? 0d : 60_000d / safetyFrameIntervalMs;

    public void RefreshState(Configuration configuration)
    {
        safetyFrameIntervalMs = Math.Clamp(configuration.BackgroundSafetyFrameIntervalSeconds, 1, 60) * 1_000;
        backgroundThrottleSleepMs = Math.Clamp(configuration.BackgroundThrottleSleepMs, 0, 500);
        onlyWhenMinimized = configuration.BackgroundNoRenderOnlyWhenMinimized;
        armed = configuration.PluginEnabled && configuration.BackgroundNoRenderEnabled;

        if (armed && !initializationAttempted)
            InitializeHooks();

        if (armed && initialized)
        {
            EnableHooks();
            SetIdleStatus();
            return;
        }

        DisableHooks();

        if (!configuration.BackgroundNoRenderEnabled)
            Status = "Background no-render disabled.";
        else if (!configuration.PluginEnabled)
            Status = "Background no-render waiting for plugin enable.";
        else if (!initializationAttempted)
            Status = "Background no-render idle.";
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
            Plugin.Log.Information("[DPS] Background no-render hooks initialized.");
        }
        catch (Exception ex)
        {
            initialized = false;
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

        if (IsNoRenderActive)
            Plugin.Log.Information("[DPS] Background no-render mode exited.");

        IsNoRenderActive = false;
        nextSafetyRenderTick = 0;
    }

    private void DeviceDx11PostTickDetour(nint instance)
    {
        var framework = Framework.Instance();
        if (!armed || framework == null || !Plugin.ClientState.IsLoggedIn)
        {
            SetNoRenderActive(false);
            deviceDx11PostTickHook!.Original(instance);
            return;
        }

        if (!ShouldSuppressRender(framework))
        {
            SetNoRenderActive(false);
            deviceDx11PostTickHook!.Original(instance);
            return;
        }

        SetNoRenderActive(true);

        var currentTick = Environment.TickCount64;
        if (nextSafetyRenderTick - currentTick < 0)
        {
            nextSafetyRenderTick = currentTick + safetyFrameIntervalMs;
            deviceDx11PostTickHook!.Original(instance);
            return;
        }

        var uiModule = UIModule.Instance();
        if (uiModule != null && uiModule->ShouldLimitFps() && backgroundThrottleSleepMs > 0)
            Thread.Sleep(backgroundThrottleSleepMs);
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

    private void SetNoRenderActive(bool active)
    {
        if (IsNoRenderActive == active)
        {
            if (!active && armed)
                SetIdleStatus();
            return;
        }

        IsNoRenderActive = active;
        if (active)
        {
            Status = onlyWhenMinimized
                ? "Background no-render ACTIVE. Window is minimized/iconic."
                : "Background no-render ACTIVE. Window is inactive."
            ;
            Plugin.Log.Information("[DPS] Background no-render mode entered.");
            return;
        }

        SetIdleStatus();
        Plugin.Log.Information("[DPS] Background no-render mode exited.");
    }

    private void SetIdleStatus()
    {
        if (!armed)
            return;

        Status = onlyWhenMinimized
            ? "Background no-render armed. Minimize the window to trigger it."
            : "Background no-render armed. Alt-tab away to trigger it.";
    }

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint windowHandle);
}
