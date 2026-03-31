using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DPS.Services;
using DPS.Windows;
using System.Linq;

namespace DPS;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;

    public static Plugin PluginInstance { get; private set; } = null!;
    public Configuration Configuration { get; }
    public ActorSuppressionService ActorSuppressionService { get; }
    public TextureRedirectService TextureRedirectService { get; }
    public BackgroundRenderGateService BackgroundRenderGateService { get; }
    public bool DebugModeEnabled { get; private set; }

    public WindowSystem WindowSystem { get; } = new(PluginInfo.InternalName);
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private readonly Random backgroundRecoveryRandom = new();
    private IDtrBarEntry? dtrEntry;
    private DateTime? nextBackgroundRecoveryUtc;
    private DateTime? backgroundRecoveryResumeUtc;

    public Plugin()
    {
        PluginInstance = this;
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ActorSuppressionService = new ActorSuppressionService();
        TextureRedirectService = new TextureRedirectService();
        BackgroundRenderGateService = new BackgroundRenderGateService();

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(PluginInfo.Command, new CommandInfo(OnCommand)
        {
            HelpMessage = $"Open {PluginInfo.DisplayName}. Use '/dps roff' to arm background no-render, '/dps ron' to disable it, and '/dps debug' to expose the paused experimental texture lab for this session.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        Framework.Update += OnFrameworkUpdate;

        SetupDtrBar();
        ApplyConfiguration();
        UpdateDtrBar();
        Log.Information("[DPS] Plugin loaded.");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        BackgroundRenderGateService.Dispose();
        TextureRedirectService.Dispose();
        ActorSuppressionService.ShowAll();
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        CommandManager.RemoveHandler(PluginInfo.Command);
        WindowSystem.RemoveAllWindows();
        dtrEntry?.Remove();
    }

    public void ToggleMainUi() => mainWindow.Toggle();
    public void ToggleConfigUi() => configWindow.Toggle();
    public string BackgroundRecoveryStatus { get; private set; } = "Automatic recovery pulse disabled.";
    public void SetDebugMode(bool enabled)
    {
        if (DebugModeEnabled == enabled)
            return;

        DebugModeEnabled = enabled;
        ApplyConfiguration();
    }

    public void ApplyConfiguration()
    {
        try
        {
            BackgroundRenderGateService.RefreshState(Configuration);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DPS] Background no-render refresh failed.");
        }

        try
        {
            TextureRedirectService.RefreshState(Configuration, DebugModeEnabled);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DPS] Texture redirect refresh failed.");
        }

        Framework.RunOnTick(() =>
        {
            try
            {
                ActorSuppressionService.Update(Configuration);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[DPS] Actor suppression update failed during apply.");
            }
        });

        RefreshBackgroundRecoveryStatus();
    }

    public void UpdateDtrBar()
    {
        if (dtrEntry == null)
            return;

        dtrEntry.Shown = Configuration.DtrBarEnabled;
        if (!Configuration.DtrBarEnabled)
            return;

        var icon = Configuration.PluginEnabled ? Configuration.DtrIconEnabled : Configuration.DtrIconDisabled;
        var status = Configuration.PluginEnabled ? "On" : "Off";
        dtrEntry.Text = Configuration.DtrBarMode switch
        {
            1 => new SeString(new TextPayload($"{icon} DPS")),
            2 => new SeString(new TextPayload(icon)),
            _ => new SeString(new TextPayload($"DPS: {status}")),
        };
        dtrEntry.Tooltip = new SeString(new TextPayload($"{PluginInfo.DisplayName} {status}. Click to toggle."));
    }

    private void SetupDtrBar()
    {
        dtrEntry = DtrBar.Get(PluginInfo.DisplayName);
        dtrEntry.OnClick = _ =>
        {
            var enabled = !Configuration.PluginEnabled;
            SetPluginEnabled(enabled, "DTR", showAllOnDisable: !enabled);
        };
    }

    public bool IsCustomResolutionInstalled()
    {
        try
        {
            return PluginInterface.InstalledPlugins.Any(plugin =>
                plugin.InternalName.StartsWith("CustomResolution", StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    public void ArmBackgroundNoRender(string source)
    {
        CancelBackgroundRecovery(source);
        Configuration.PluginEnabled = true;
        Configuration.BackgroundNoRenderEnabled = true;
        Configuration.Save();
        ApplyConfiguration();
        UpdateDtrBar();
        Log.Information("[DPS] Background no-render armed via {Source}.", source);
    }

    public void DisableBackgroundNoRender(string source)
    {
        CancelBackgroundRecovery(source);
        var cleanDisable = Configuration.CleanDisableExperimentalRenderHack;
        Configuration.BackgroundNoRenderEnabled = false;
        if (cleanDisable)
        {
            Configuration.PluginEnabled = false;
            ActorSuppressionService.ShowAll();
        }

        Configuration.Save();
        ApplyConfiguration();
        UpdateDtrBar();
        Log.Information("[DPS] Background no-render disabled via {Source}{Mode}.", source, cleanDisable ? " with clean-disable" : string.Empty);
    }

    public void SetPluginEnabled(bool enabled, string source, bool showAllOnDisable = false)
    {
        if (!enabled)
        {
            CancelBackgroundRecovery(source);
            Configuration.BackgroundNoRenderEnabled = false;
        }

        Configuration.PluginEnabled = enabled;
        Configuration.Save();
        ApplyConfiguration();
        UpdateDtrBar();

        if (!enabled && showAllOnDisable)
            ActorSuppressionService.ShowAll();

        Log.Information("[DPS] Plugin {State} via {Source}.", enabled ? "enabled" : "disabled", source);
    }

    private void OnCommand(string command, string arguments)
    {
        var trimmedArguments = arguments?.Trim() ?? string.Empty;
        if (trimmedArguments.Equals("roff", StringComparison.OrdinalIgnoreCase))
        {
            ArmBackgroundNoRender("slash command");
            mainWindow.IsOpen = true;
            return;
        }

        if (trimmedArguments.Equals("ron", StringComparison.OrdinalIgnoreCase))
        {
            DisableBackgroundNoRender("slash command");
            mainWindow.IsOpen = true;
            return;
        }

        if (trimmedArguments.Equals("debug", StringComparison.OrdinalIgnoreCase))
        {
            if (!DebugModeEnabled)
            {
                DebugModeEnabled = true;
                ApplyConfiguration();
                Log.Information("[DPS] Debug mode enabled for this session.");
            }

            mainWindow.IsOpen = true;
            return;
        }

        if (trimmedArguments.Equals("debug off", StringComparison.OrdinalIgnoreCase) ||
            trimmedArguments.Equals("nodebug", StringComparison.OrdinalIgnoreCase))
        {
            if (DebugModeEnabled)
            {
                DebugModeEnabled = false;
                ApplyConfiguration();
                Log.Information("[DPS] Debug mode disabled for this session.");
            }

            mainWindow.IsOpen = true;
            return;
        }

        ToggleMainUi();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        try
        {
            ActorSuppressionService.Update(Configuration);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DPS] Actor suppression update failed during framework tick.");
        }

        TickBackgroundRecoveryLoop();
        UpdateDtrBar();
    }

    private void TickBackgroundRecoveryLoop()
    {
        if (!Configuration.BackgroundRecoveryLoopEnabled)
        {
            if (nextBackgroundRecoveryUtc != null || backgroundRecoveryResumeUtc != null || BackgroundRecoveryStatus != "Automatic recovery pulse disabled.")
                ResetBackgroundRecoveryState("Automatic recovery pulse disabled.");
            return;
        }

        var now = DateTime.UtcNow;
        if (backgroundRecoveryResumeUtc is { } resumeUtc)
        {
            if (now >= resumeUtc)
            {
                FinishBackgroundRecoveryPulse();
            }
            else
            {
                BackgroundRecoveryStatus = $"Automatic recovery pulse active for another {FormatDuration(resumeUtc - now)}.";
            }

            return;
        }

        if (!Configuration.BackgroundNoRenderEnabled)
        {
            ResetBackgroundRecoveryState("Automatic recovery pulse waiting for background no-render.");
            return;
        }

        if (!Configuration.PluginEnabled)
        {
            ResetBackgroundRecoveryState("Automatic recovery pulse waiting for plugin enable.");
            return;
        }

        if (nextBackgroundRecoveryUtc == null)
        {
            ScheduleNextBackgroundRecovery();
            return;
        }

        if (now >= nextBackgroundRecoveryUtc.Value)
        {
            if (BackgroundRenderGateService.IsNoRenderActive)
            {
                StartBackgroundRecoveryPulse();
            }
            else
            {
                BackgroundRecoveryStatus = "Automatic recovery pulse is due and will run on the next active background no-render cycle.";
            }

            return;
        }

        BackgroundRecoveryStatus = $"Automatic recovery pulse queued in {FormatDuration(nextBackgroundRecoveryUtc.Value - now)}.";
    }

    private void StartBackgroundRecoveryPulse()
    {
        var pulseSeconds = Math.Clamp(Configuration.BackgroundRecoveryPulseSeconds, 1, 30);
        backgroundRecoveryResumeUtc = DateTime.UtcNow.AddSeconds(pulseSeconds);
        Configuration.BackgroundNoRenderEnabled = false;

        if (Configuration.CleanDisableExperimentalRenderHack)
        {
            Configuration.PluginEnabled = false;
            ActorSuppressionService.ShowAll();
        }

        Configuration.Save();
        ApplyConfiguration();
        UpdateDtrBar();
        BackgroundRecoveryStatus = $"Automatic recovery pulse active for {pulseSeconds}s.";
        Log.Information("[DPS] Automatic background recovery pulse started for {PulseSeconds}s.", pulseSeconds);
    }

    private void FinishBackgroundRecoveryPulse()
    {
        backgroundRecoveryResumeUtc = null;
        Configuration.PluginEnabled = true;
        Configuration.BackgroundNoRenderEnabled = true;
        Configuration.Save();
        ApplyConfiguration();
        UpdateDtrBar();
        ScheduleNextBackgroundRecovery();
        Log.Information("[DPS] Automatic background recovery pulse ended; background no-render re-armed.");
    }

    private void CancelBackgroundRecovery(string source)
    {
        if (nextBackgroundRecoveryUtc == null && backgroundRecoveryResumeUtc == null && !Configuration.BackgroundRecoveryLoopEnabled)
            return;

        if (backgroundRecoveryResumeUtc != null || nextBackgroundRecoveryUtc != null)
            Log.Information("[DPS] Automatic background recovery pulse cancelled via {Source}.", source);

        RefreshBackgroundRecoveryStatus(resetSchedule: true);
    }

    private void RefreshBackgroundRecoveryStatus(bool resetSchedule = false)
    {
        if (resetSchedule)
        {
            nextBackgroundRecoveryUtc = null;
            backgroundRecoveryResumeUtc = null;
        }

        if (!Configuration.BackgroundRecoveryLoopEnabled)
        {
            BackgroundRecoveryStatus = "Automatic recovery pulse disabled.";
            return;
        }

        if (backgroundRecoveryResumeUtc != null)
            return;

        if (!Configuration.BackgroundNoRenderEnabled)
        {
            BackgroundRecoveryStatus = "Automatic recovery pulse waiting for background no-render.";
            return;
        }

        if (!Configuration.PluginEnabled)
        {
            BackgroundRecoveryStatus = "Automatic recovery pulse waiting for plugin enable.";
            return;
        }

        if (nextBackgroundRecoveryUtc == null)
        {
            ScheduleNextBackgroundRecovery();
            return;
        }

        BackgroundRecoveryStatus = $"Automatic recovery pulse queued in {FormatDuration(nextBackgroundRecoveryUtc.Value - DateTime.UtcNow)}.";
    }

    private void ResetBackgroundRecoveryState(string status)
    {
        nextBackgroundRecoveryUtc = null;
        backgroundRecoveryResumeUtc = null;
        BackgroundRecoveryStatus = status;
    }

    private void ScheduleNextBackgroundRecovery()
    {
        var minMinutes = Math.Clamp(Configuration.BackgroundRecoveryMinMinutes, 1, 120);
        var maxMinutes = Math.Clamp(Configuration.BackgroundRecoveryMaxMinutes, minMinutes, 120);
        var nextMinutes = backgroundRecoveryRandom.Next(minMinutes, maxMinutes + 1);
        nextBackgroundRecoveryUtc = DateTime.UtcNow.AddMinutes(nextMinutes);
        BackgroundRecoveryStatus = $"Automatic recovery pulse queued in about {nextMinutes}m.";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
            return "0s";

        if (duration.TotalMinutes >= 1d)
            return $"{Math.Ceiling(duration.TotalMinutes):0}m";

        return $"{Math.Ceiling(duration.TotalSeconds):0}s";
    }
}
