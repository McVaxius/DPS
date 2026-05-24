using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Bindings.ImGui;
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
    [PluginService] internal static IKeyState KeyState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
    [PluginService] internal static ISigScanner SigScanner { get; private set; } = null!;

    public static Plugin PluginInstance { get; private set; } = null!;
    public Configuration Configuration { get; }
    public ActorSuppressionService ActorSuppressionService { get; }
    public TextureRedirectService TextureRedirectService { get; }
    public BackgroundRenderGateService BackgroundRenderGateService { get; }
    public ForegroundRenderControlService ForegroundRenderControlService { get; }
    public bool DebugModeEnabled { get; private set; }

    public WindowSystem WindowSystem { get; } = new(PluginInfo.InternalName);
    private readonly MainWindow mainWindow;
    private readonly Random backgroundRecoveryRandom = new();
    private IDtrBarEntry? dtrEntry;
    private DateTime? nextBackgroundRecoveryUtc;
    private DateTime? backgroundRecoveryResumeUtc;
    private bool foregroundHotkeyDown;
    private bool backgroundHotkeyDown;
    private bool crowdHotkeyDown;
    private bool allOffHotkeyDown;

    public Plugin()
    {
        PluginInstance = this;
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        MigrateConfiguration();
        ActorSuppressionService = new ActorSuppressionService();
        TextureRedirectService = new TextureRedirectService();
        BackgroundRenderGateService = new BackgroundRenderGateService();
        ForegroundRenderControlService = new ForegroundRenderControlService();

        mainWindow = new MainWindow(this);
        WindowSystem.AddWindow(mainWindow);

        CommandManager.AddHandler(PluginInfo.Command, new CommandInfo(OnCommand)
        {
            HelpMessage = $"Open {PluginInfo.DisplayName}. Use '/dps roff' and '/dps ron' for background no-render, '/dps foff' for foreground render OFF, '/dps fon' for foreground render ON, '/dps ws' to reset the main window, '/dps j' to randomize the main window, and '/dps debug' to expose the paused experimental texture lab for this session.",
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

    private void MigrateConfiguration()
    {
        var changed = false;

        if (Configuration.Version < 2)
        {
            Configuration.CleanDisableExperimentalRenderHack = true;
            Configuration.Version = 2;
            changed = true;
        }

        if (Configuration.Version < 3)
        {
            Configuration.ForegroundNoRenderEnabled = false;
            Configuration.Version = 3;
            changed = true;
        }

        if (Configuration.Version < 4)
        {
            Configuration.CrowdSuppressionEnabled = Configuration.HideNonPartyPlayers
                                                 || Configuration.HideNonPartyPets
                                                 || Configuration.HideNonPartyChocobos
                                                 || Configuration.HideNonPartyMinions;
            Configuration.Version = 4;
            changed = true;
        }

        if (changed)
            Configuration.Save();
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ForegroundRenderControlService.Dispose();
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
    public void ToggleConfigUi() => mainWindow.OpenHotkeysTab();
    public void ResetWindowPositions()
    {
        mainWindow.QueueTopLeftPlacement();
    }

    public void RandomizeWindowPositions()
    {
        mainWindow.QueueRandomPlacement();
    }

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
            ForegroundRenderControlService.RefreshState(Configuration);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DPS] Foreground no-render refresh failed.");
        }

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
        if (Configuration.ForegroundNoRenderEnabled || ForegroundRenderControlService.RenderDisabledByDps)
        {
            Configuration.ForegroundNoRenderEnabled = false;
            ForegroundRenderControlService.RestoreRender($"background no-render arm via {source}");
        }

        Configuration.PluginEnabled = true;
        Configuration.BackgroundNoRenderEnabled = true;
        Configuration.Save();
        ApplyConfiguration();
        UpdateDtrBar();
        Log.Information("[DPS] Background no-render armed via {Source}.", source);
    }

    public void ArmForegroundNoRender(string source)
    {
        Configuration.PluginEnabled = true;
        Configuration.BackgroundNoRenderEnabled = false;
        CancelBackgroundRecovery(source);

        try
        {
            BackgroundRenderGateService.RefreshState(Configuration);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DPS] Background no-render refresh failed while arming foreground no-render.");
        }

        Configuration.ForegroundNoRenderEnabled = true;
        Configuration.Save();
        ApplyConfiguration();
        UpdateDtrBar();
        Log.Information("[DPS] Foreground no-render armed via {Source}.", source);
    }

    public void DisableBackgroundNoRender(string source)
    {
        CancelBackgroundRecovery(source);
        var cleanDisable = Configuration.CleanDisableExperimentalRenderHack;
        Configuration.BackgroundNoRenderEnabled = false;
        if (cleanDisable)
        {
            Configuration.ForegroundNoRenderEnabled = false;
            ForegroundRenderControlService.RestoreRender($"background no-render clean-disable via {source}");
            Configuration.PluginEnabled = false;
            ActorSuppressionService.ShowAll();
        }

        Configuration.Save();
        ApplyConfiguration();
        UpdateDtrBar();
        Log.Information("[DPS] Background no-render disabled via {Source}{Mode}.", source, cleanDisable ? " with clean-disable" : string.Empty);
    }

    public void DisableForegroundNoRender(string source)
    {
        CancelBackgroundRecovery(source);
        var cleanDisable = Configuration.CleanDisableExperimentalRenderHack;
        Configuration.ForegroundNoRenderEnabled = false;
        ForegroundRenderControlService.RestoreRender($"foreground no-render disable via {source}");
        if (cleanDisable)
        {
            Configuration.BackgroundNoRenderEnabled = false;
            Configuration.PluginEnabled = false;
            ActorSuppressionService.ShowAll();
        }

        Configuration.Save();
        ApplyConfiguration();
        UpdateDtrBar();
        Log.Information("[DPS] Foreground no-render disabled via {Source}{Mode}.", source, cleanDisable ? " with clean-disable" : string.Empty);
    }

    public void ToggleBackgroundNoRenderHotkey()
    {
        if (Configuration.BackgroundNoRenderEnabled)
        {
            CancelBackgroundRecovery("background hotkey");
            Configuration.BackgroundNoRenderEnabled = false;
            Configuration.Save();
            ApplyConfiguration();
            UpdateDtrBar();
            Log.Information("[DPS] Background no-render toggled off via hotkey.");
            return;
        }

        ArmBackgroundNoRender("background hotkey");
    }

    public void ToggleForegroundNoRenderHotkey()
    {
        if (Configuration.ForegroundNoRenderEnabled || ForegroundRenderControlService.RenderDisabledByDps)
        {
            CancelBackgroundRecovery("foreground hotkey");
            Configuration.ForegroundNoRenderEnabled = false;
            ForegroundRenderControlService.RestoreRender("foreground hotkey");
            Configuration.Save();
            ApplyConfiguration();
            UpdateDtrBar();
            Log.Information("[DPS] Foreground no-render toggled off via hotkey.");
            return;
        }

        ArmForegroundNoRender("foreground hotkey");
    }

    public void SetCrowdSuppressionEnabled(bool enabled, string source, bool enablePluginOnEnable = false)
    {
        Configuration.CrowdSuppressionEnabled = enabled;
        if (enabled && enablePluginOnEnable)
            Configuration.PluginEnabled = true;
        if (!enabled)
            ActorSuppressionService.ShowAll();

        Configuration.Save();
        ApplyConfiguration();
        UpdateDtrBar();
        Log.Information("[DPS] Crowd suppression {State} via {Source}.", enabled ? "enabled" : "disabled", source);
    }

    public void ToggleCrowdSuppressionHotkey()
        => SetCrowdSuppressionEnabled(!Configuration.CrowdSuppressionEnabled, "crowd hotkey", enablePluginOnEnable: true);

    public void AllOff(string source)
    {
        CancelBackgroundRecovery(source);
        Configuration.BackgroundNoRenderEnabled = false;
        Configuration.ForegroundNoRenderEnabled = false;
        Configuration.CrowdSuppressionEnabled = false;
        ForegroundRenderControlService.RestoreRender($"all off via {source}");
        ActorSuppressionService.ShowAll();
        Configuration.Save();
        ApplyConfiguration();
        UpdateDtrBar();
        Log.Information("[DPS] All operation modes disabled via {Source}.", source);
    }

    public void SetPluginEnabled(bool enabled, string source, bool showAllOnDisable = false)
    {
        if (!enabled)
        {
            CancelBackgroundRecovery(source);
            Configuration.BackgroundNoRenderEnabled = false;
            Configuration.ForegroundNoRenderEnabled = false;
            ForegroundRenderControlService.RestoreRender($"plugin disable via {source}");
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

        if (trimmedArguments.Equals("foff", StringComparison.OrdinalIgnoreCase))
        {
            ArmForegroundNoRender("slash command");
            mainWindow.IsOpen = true;
            return;
        }

        if (trimmedArguments.Equals("fon", StringComparison.OrdinalIgnoreCase))
        {
            DisableForegroundNoRender("slash command");
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

        if (trimmedArguments.Equals("ws", StringComparison.OrdinalIgnoreCase))
        {
            ResetWindowPositions();
            Log.Information("[DPS] Main window moved to 1,1.");
            return;
        }

        if (trimmedArguments.Equals("j", StringComparison.OrdinalIgnoreCase))
        {
            RandomizeWindowPositions();
            Log.Information("[DPS] Main window randomized within viewport.");
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
        TickHotkeys();

        try
        {
            ForegroundRenderControlService.Tick(Configuration);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "[DPS] Foreground no-render tick failed.");
        }

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

    private void TickHotkeys()
    {
        var inputCaptured = IsKeyboardInputCaptured();
        TickHotkey(Configuration.ForegroundToggleHotkey, ref foregroundHotkeyDown, ToggleForegroundNoRenderHotkey, inputCaptured);
        TickHotkey(Configuration.BackgroundToggleHotkey, ref backgroundHotkeyDown, ToggleBackgroundNoRenderHotkey, inputCaptured);
        TickHotkey(Configuration.CrowdToggleHotkey, ref crowdHotkeyDown, ToggleCrowdSuppressionHotkey, inputCaptured);
        TickHotkey(Configuration.AllOffHotkey, ref allOffHotkeyDown, () => AllOff("all off hotkey"), inputCaptured);
    }

    private void TickHotkey(HotkeyBinding binding, ref bool wasDown, Action action, bool inputCaptured)
    {
        if (!IsUsableHotkey(binding))
        {
            wasDown = false;
            return;
        }

        var isDown = IsHotkeyDown(binding);
        if (inputCaptured)
        {
            wasDown = isDown;
            return;
        }

        if (isDown && !wasDown)
            action();

        wasDown = isDown;
    }

    private bool IsKeyboardInputCaptured()
    {
        if (mainWindow.IsCapturingHotkey)
            return true;

        try
        {
            var io = ImGui.GetIO();
            return io.WantCaptureKeyboard || io.WantTextInput;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUsableHotkey(HotkeyBinding binding)
        => binding.Enabled
        && binding.KeyCode != 0
        && !IsModifierKey(binding.KeyCode)
        && KeyState.IsVirtualKeyValid(binding.KeyCode);

    private static bool IsHotkeyDown(HotkeyBinding binding)
        => IsVirtualKeyPressed(binding.KeyCode)
        && binding.Ctrl == IsCtrlDown()
        && binding.Alt == IsAltDown()
        && binding.Shift == IsShiftDown();

    internal static bool IsVirtualKeyPressed(int keyCode)
        => KeyState.IsVirtualKeyValid(keyCode) && KeyState[keyCode];

    internal static bool IsModifierKey(int keyCode)
        => keyCode == (int)VirtualKey.SHIFT
        || keyCode == (int)VirtualKey.LSHIFT
        || keyCode == (int)VirtualKey.RSHIFT
        || keyCode == (int)VirtualKey.CONTROL
        || keyCode == (int)VirtualKey.LCONTROL
        || keyCode == (int)VirtualKey.RCONTROL
        || keyCode == (int)VirtualKey.MENU
        || keyCode == (int)VirtualKey.LMENU
        || keyCode == (int)VirtualKey.RMENU
        || keyCode == (int)VirtualKey.LWIN
        || keyCode == (int)VirtualKey.RWIN;

    internal static bool IsCtrlDown()
        => IsVirtualKeyPressed((int)VirtualKey.CONTROL)
        || IsVirtualKeyPressed((int)VirtualKey.LCONTROL)
        || IsVirtualKeyPressed((int)VirtualKey.RCONTROL);

    internal static bool IsAltDown()
        => IsVirtualKeyPressed((int)VirtualKey.MENU)
        || IsVirtualKeyPressed((int)VirtualKey.LMENU)
        || IsVirtualKeyPressed((int)VirtualKey.RMENU);

    internal static bool IsShiftDown()
        => IsVirtualKeyPressed((int)VirtualKey.SHIFT)
        || IsVirtualKeyPressed((int)VirtualKey.LSHIFT)
        || IsVirtualKeyPressed((int)VirtualKey.RSHIFT);

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
            Configuration.ForegroundNoRenderEnabled = false;
            ForegroundRenderControlService.RestoreRender("automatic background recovery pulse");
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
