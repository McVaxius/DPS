using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DPS.Services;

namespace DPS.Windows;

public sealed class AdvancedWindow : Window
{
    private readonly Plugin plugin;

    public AdvancedWindow(Plugin plugin)
        : base($"{PluginInfo.DisplayName} Advanced##DPSAdvanced")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560f, 390f),
            MaximumSize = new Vector2(1120f, 1000f),
        };
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        UiHelpers.Wrapped("Automatic rendering exceptions are opt-in. Changes save and apply immediately.");

        UiHelpers.SectionHeader("Foreground Display Recovery");
        UiHelpers.Wrapped("Applies to both foreground modes. Only selected conditions can pause no-render.");
        DrawToggle("Enable foreground display recovery", cfg.ForegroundDisplayRecoveryGuardEnabled,
            value => cfg.ForegroundDisplayRecoveryGuardEnabled = value);
        if (cfg.ForegroundDisplayRecoveryGuardEnabled)
        {
            DrawToggle("No monitors", cfg.DisplayRecoveryNoMonitors, value => cfg.DisplayRecoveryNoMonitors = value);
            DrawToggle("No current monitor", cfg.DisplayRecoveryNoCurrentMonitor, value => cfg.DisplayRecoveryNoCurrentMonitor = value);
            DrawToggle("Missing window", cfg.DisplayRecoveryMissingWindow, value => cfg.DisplayRecoveryMissingWindow = value);
            DrawToggle("Hidden window", cfg.DisplayRecoveryHiddenWindow, value => cfg.DisplayRecoveryHiddenWindow = value);
            DrawToggle("Minimized window", cfg.DisplayRecoveryMinimizedWindow, value => cfg.DisplayRecoveryMinimizedWindow = value);
            DrawToggle("Zero or negative window dimensions", cfg.DisplayRecoveryInvalidWindowSize, value => cfg.DisplayRecoveryInvalidWindowSize = value);
            DrawToggle("Monitor topology changes", cfg.DisplayRecoveryMonitorTopologyChanges, value => cfg.DisplayRecoveryMonitorTopologyChanges = value);
            DrawToggle("Current-monitor changes", cfg.DisplayRecoveryCurrentMonitorChanges, value => cfg.DisplayRecoveryCurrentMonitorChanges = value);
            DrawToggle("Window movement", cfg.DisplayRecoveryWindowMovement, value => cfg.DisplayRecoveryWindowMovement = value);
            DrawToggle("Window resizing", cfg.DisplayRecoveryWindowResizing, value => cfg.DisplayRecoveryWindowResizing = value);

            if (DisplayRecoveryService.GetEnabledCauses(cfg) != DisplayRecoveryCause.None)
            {
                DrawInt("Recovery pause seconds", cfg.ForegroundDisplayRecoveryPauseSeconds, 15, 900,
                    value => cfg.ForegroundDisplayRecoveryPauseSeconds = value);
                DrawInt("Stable seconds", cfg.ForegroundDisplayRecoveryStableSeconds, 5, 300,
                    value => cfg.ForegroundDisplayRecoveryStableSeconds = value);
            }
        }
        UiHelpers.Wrapped(plugin.DisplayRecoveryService.Status);

        UiHelpers.SectionHeader("Frozen-frame and Background Exceptions");
        DrawToggle("Render during area transitions", cfg.RenderDuringAreaTransitions, value => cfg.RenderDuringAreaTransitions = value);
        DrawToggle("Render while logged out", cfg.RenderWhileLoggedOut, value => cfg.RenderWhileLoggedOut = value);
        DrawToggle("Allow periodic frames", cfg.PeriodicRenderFramesEnabled, value => cfg.PeriodicRenderFramesEnabled = value);
        if (cfg.PeriodicRenderFramesEnabled)
            DrawInt("Periodic frame interval (sec)", cfg.BackgroundSafetyFrameIntervalSeconds, 1, 60,
                value => cfg.BackgroundSafetyFrameIntervalSeconds = value);

        UiHelpers.SectionHeader("Background Recovery");
        DrawToggle("Automatic background recovery pulses", cfg.BackgroundRecoveryLoopEnabled, value => cfg.BackgroundRecoveryLoopEnabled = value);
        if (cfg.BackgroundRecoveryLoopEnabled)
        {
            DrawInt("Minimum minutes", cfg.BackgroundRecoveryMinMinutes, 1, 120, value =>
            {
                cfg.BackgroundRecoveryMinMinutes = value;
                cfg.BackgroundRecoveryMaxMinutes = Math.Max(value, cfg.BackgroundRecoveryMaxMinutes);
            });
            DrawInt("Maximum minutes", cfg.BackgroundRecoveryMaxMinutes, cfg.BackgroundRecoveryMinMinutes, 120,
                value => cfg.BackgroundRecoveryMaxMinutes = value);
            DrawInt("Pulse seconds", cfg.BackgroundRecoveryPulseSeconds, 1, 30,
                value => cfg.BackgroundRecoveryPulseSeconds = value);
        }
        UiHelpers.Wrapped(plugin.BackgroundRecoveryStatus);

        UiHelpers.SectionHeader("AutoRetainer");
        DrawToggle("Automatically resolve foreground rendering conflicts", cfg.AutoRetainerRenderConflictResolutionEnabled,
            value => cfg.AutoRetainerRenderConflictResolutionEnabled = value);
        UiHelpers.Wrapped("Allows DPS to turn off AutoRetainer's MultiDisableRender setting when foreground no-render is requested. Turning this option off leaves AutoRetainer's current setting as-is.");

        UiHelpers.SectionHeader("Background Throttle");
        var throttleSleepMs = cfg.BackgroundThrottleSleepMs;
        if (ImGui.SliderInt("Throttle sleep while gated (ms)", ref throttleSleepMs, 0, 200))
        {
            cfg.BackgroundThrottleSleepMs = throttleSleepMs;
            SaveAndApply();
        }
        UiHelpers.Wrapped("0 is recommended. Sleeping inside the render hook can hitch area changes.");
    }

    private void DrawToggle(string label, bool value, Action<bool> setValue)
    {
        if (!ImGui.Checkbox(label, ref value))
            return;

        setValue(value);
        SaveAndApply();
    }

    private void DrawInt(string label, int value, int minimum, int maximum, Action<int> setValue)
    {
        if (!ImGui.InputInt(label, ref value))
            return;

        setValue(Math.Clamp(value, minimum, maximum));
        SaveAndApply();
    }

    private void SaveAndApply()
    {
        plugin.Configuration.Save();
        plugin.ApplyConfiguration();
        plugin.UpdateDtrBar();
    }
}
