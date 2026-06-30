using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Windowing;
using DPS.Services;

namespace DPS.Windows;

public sealed class MainWindow : Window
{
    private enum PendingPlacement
    {
        None,
        TopLeft,
        Random,
    }

    private enum HotkeyTarget
    {
        Foreground,
        Background,
        Crowd,
        AllOff,
        WindowPlacementAndSizeLoad,
    }

    private readonly Plugin plugin;
    private PendingPlacement pendingPlacement;
    private HotkeyTarget? hotkeyCaptureTarget;
    private bool selectHotkeysTab;

    public bool IsCapturingHotkey => hotkeyCaptureTarget != null;

    public MainWindow(Plugin plugin)
        : base($"{PluginInfo.DisplayName}##DPSMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(560f, 390f),
            MaximumSize = new Vector2(1120f, 900f),
        };
    }

    public void QueueTopLeftPlacement()
    {
        pendingPlacement = PendingPlacement.TopLeft;
        IsOpen = true;
    }

    public void QueueRandomPlacement()
    {
        pendingPlacement = PendingPlacement.Random;
        IsOpen = true;
    }

    public void OpenHotkeysTab()
    {
        selectHotkeysTab = true;
        IsOpen = true;
    }

    public override void Draw()
    {
        ApplyPendingPlacement();
        DrawTopBar();

        if (ImGui.BeginTabBar("##DpsMainTabs"))
        {
            if (ImGui.BeginTabItem("Render"))
            {
                DrawRenderTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Crowd"))
            {
                DrawCrowdTab();
                ImGui.EndTabItem();
            }

            var hotkeyTabFlags = selectHotkeysTab ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
            if (ImGui.BeginTabItem("Hotkeys", hotkeyTabFlags))
            {
                selectHotkeysTab = false;
                DrawHotkeysTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("DTR"))
            {
                DrawDtrTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Window XY"))
            {
                DrawWindowPlacementTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Diagnostics"))
            {
                DrawDiagnosticsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("About"))
            {
                DrawAboutTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void ApplyPendingPlacement()
    {
        if (pendingPlacement == PendingPlacement.None)
            return;

        if (pendingPlacement == PendingPlacement.TopLeft)
        {
            ImGui.SetWindowPos(new Vector2(1f, 1f));
            pendingPlacement = PendingPlacement.None;
            return;
        }

        var viewport = ImGui.GetMainViewport();
        var size = ImGui.GetWindowSize();
        var maxX = Math.Max(viewport.WorkPos.X, viewport.WorkPos.X + viewport.WorkSize.X - size.X);
        var maxY = Math.Max(viewport.WorkPos.Y, viewport.WorkPos.Y + viewport.WorkSize.Y - size.Y);
        var x = viewport.WorkPos.X + (float)(Random.Shared.NextDouble() * Math.Max(1f, maxX - viewport.WorkPos.X));
        var y = viewport.WorkPos.Y + (float)(Random.Shared.NextDouble() * Math.Max(1f, maxY - viewport.WorkPos.Y));
        ImGui.SetWindowPos(new Vector2(x, y));
        pendingPlacement = PendingPlacement.None;
    }

    private void DrawTopBar()
    {
        var cfg = plugin.Configuration;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

        ImGui.TextUnformatted($"{PluginInfo.DisplayName} v{version}");
        UiHelpers.SameLineIfFits(86f);
        UiHelpers.StatusPill("Plugin", cfg.PluginEnabled);
        UiHelpers.SameLineIfFits(92f);
        UiHelpers.StatusPill("BG", cfg.BackgroundNoRenderEnabled, plugin.BackgroundRenderGateService.IsBackgroundNoRenderActive ? "ACTIVE" : "ARMED");
        UiHelpers.SameLineIfFits(132f);
        UiHelpers.ForegroundRenderStatus(plugin, includeIntent: false);
        UiHelpers.SameLineIfFits(104f);
        UiHelpers.StatusPill("Crowd", cfg.CrowdSuppressionEnabled);

        ImGui.Spacing();
        if (UiHelpers.CompactButton(
                cfg.PluginEnabled ? "Stop" : "Run",
                58f,
                cfg.PluginEnabled ? "Stop DPS and restore visibility." : "Start DPS controls."))
        {
            plugin.SetPluginEnabled(!cfg.PluginEnabled, "main window top bar", showAllOnDisable: cfg.PluginEnabled);
        }

        UiHelpers.SameLineIfFits(78f);
        if (UiHelpers.CompactButton("All Off", 70f, "Disable foreground, background, and crowd suppression."))
            plugin.AllOff("main window top bar");
        UiHelpers.SameLineIfFits(100f);
        if (UiHelpers.CompactButton("Restore FG", 92f, "Restore foreground rendering."))
            plugin.DisableForegroundNoRender("main window top bar");
        UiHelpers.SameLineIfFits(100f);
        if (UiHelpers.CompactButton("Restore BG", 92f, "Restore background rendering."))
            plugin.DisableBackgroundNoRender("main window top bar");
        UiHelpers.SameLineIfFits(94f);
        if (UiHelpers.CompactButton("Show All", 86f, "Disable DPS and restore all hidden visuals."))
            plugin.SetPluginEnabled(false, "main window top bar show all", showAllOnDisable: true);
        UiHelpers.SameLineIfFits(80f);
        if (UiHelpers.CompactButton(cfg.DtrBarEnabled ? "DTR On" : "DTR Off", 72f, "Toggle the DPS DTR bar entry."))
        {
            cfg.DtrBarEnabled = !cfg.DtrBarEnabled;
            cfg.Save();
            plugin.UpdateDtrBar();
        }
        UiHelpers.SameLineIfFits(84f);
        if (UiHelpers.CompactButton("Hotkeys", 76f, "Open hotkey bindings."))
            OpenHotkeysTab();
        UiHelpers.SameLineIfFits(54f);
        UiHelpers.LinkButton("Ko-fi", PluginInfo.SupportUrl, "Open Ko-fi support page.");
        UiHelpers.SameLineIfFits(70f);
        UiHelpers.LinkButton("Discord", PluginInfo.DiscordUrl, "Open Discord community link.");
        UiHelpers.SameLineIfFits(58f);
        if (UiHelpers.SmallButton("Close", "Close this window."))
            IsOpen = false;

        ImGui.Separator();
    }

    private void DrawRenderTab()
    {
        var cfg = plugin.Configuration;

        UiHelpers.SectionHeader("Foreground");
        UiHelpers.HotkeyStatus("Hotkey", cfg.ForegroundToggleHotkey);
        UiHelpers.ForegroundRenderOffCheckbox(plugin, "main render tab");
        DrawForegroundNoRenderModeSelector();
        UiHelpers.ForegroundRenderStatus(plugin);
        UiHelpers.Wrapped(plugin.ForegroundRenderControlService.Status);
        DrawForegroundDisplayRecoveryControls();

        if (cfg.BackgroundNoRenderEnabled && cfg.ForegroundNoRenderEnabled)
            UiHelpers.WarningStrip("Mutual exclusion failed: both render modes are enabled.", danger: true);
        else
            UiHelpers.StatusPill("Mutual exclusion", "OK", UiHelpers.Good);

        ImGui.Spacing();
        ImGui.Separator();

        UiHelpers.SectionHeader("Background");
        UiHelpers.HotkeyStatus("Hotkey", cfg.BackgroundToggleHotkey);
        var backgroundEnabled = cfg.BackgroundNoRenderEnabled;
        if (ImGui.Checkbox("Background no-render", ref backgroundEnabled))
        {
            if (backgroundEnabled)
                plugin.ArmBackgroundNoRender("main render tab");
            else
                plugin.DisableBackgroundNoRender("main render tab");
        }

        var onlyWhenMinimized = cfg.BackgroundNoRenderOnlyWhenMinimized;
        if (ImGui.Checkbox("Only while minimized/iconic", ref onlyWhenMinimized))
        {
            cfg.BackgroundNoRenderOnlyWhenMinimized = onlyWhenMinimized;
            SaveAndApply();
        }

        var cleanDisable = cfg.CleanDisableExperimentalRenderHack;
        if (ImGui.Checkbox("Clean disable restores plugin state", ref cleanDisable))
        {
            cfg.CleanDisableExperimentalRenderHack = cleanDisable;
            SaveAndApply();
        }

        var safetyFrameInterval = cfg.BackgroundSafetyFrameIntervalSeconds;
        if (ImGui.SliderInt("Safety frame interval (sec)", ref safetyFrameInterval, 1, 60))
        {
            cfg.BackgroundSafetyFrameIntervalSeconds = safetyFrameInterval;
            SaveAndApply();
        }

        var throttleSleepMs = cfg.BackgroundThrottleSleepMs;
        if (ImGui.SliderInt("Advanced throttle sleep while gated (unsafe, ms)", ref throttleSleepMs, 0, 200))
        {
            cfg.BackgroundThrottleSleepMs = throttleSleepMs;
            SaveAndApply();
        }
        UiHelpers.Tooltip("0 is recommended. Sleeping inside the render hook can hitch area changes.");

        UiHelpers.StatusPill("Hook", plugin.BackgroundRenderGateService.HooksActive, "READY", "IDLE");
        ImGui.SameLine();
        UiHelpers.StatusPill("Gate", plugin.BackgroundRenderGateService.IsBackgroundNoRenderActive, "ACTIVE", "WAIT");
        ImGui.SameLine();
        UiHelpers.StatusPill("Transition", !plugin.BackgroundRenderGateService.TransitionBypassActive, "NORMAL", "BYPASS");
        UiHelpers.Wrapped(plugin.BackgroundRenderGateService.Status);

        UiHelpers.SectionHeader("Recovery");
        var recoveryLoopEnabled = cfg.BackgroundRecoveryLoopEnabled;
        if (ImGui.Checkbox("Automatic recovery pulse", ref recoveryLoopEnabled))
        {
            cfg.BackgroundRecoveryLoopEnabled = recoveryLoopEnabled;
            SaveAndApply();
        }

        if (cfg.BackgroundRecoveryLoopEnabled)
        {
            var recoveryMinMinutes = cfg.BackgroundRecoveryMinMinutes;
            if (ImGui.InputInt("Minimum minutes", ref recoveryMinMinutes))
            {
                cfg.BackgroundRecoveryMinMinutes = Math.Clamp(recoveryMinMinutes, 1, 120);
                if (cfg.BackgroundRecoveryMaxMinutes < cfg.BackgroundRecoveryMinMinutes)
                    cfg.BackgroundRecoveryMaxMinutes = cfg.BackgroundRecoveryMinMinutes;
                SaveAndApply();
            }

            var recoveryMaxMinutes = cfg.BackgroundRecoveryMaxMinutes;
            if (ImGui.InputInt("Maximum minutes", ref recoveryMaxMinutes))
            {
                cfg.BackgroundRecoveryMaxMinutes = Math.Clamp(recoveryMaxMinutes, cfg.BackgroundRecoveryMinMinutes, 120);
                SaveAndApply();
            }

            var recoveryPulseSeconds = cfg.BackgroundRecoveryPulseSeconds;
            if (ImGui.InputInt("Pulse seconds", ref recoveryPulseSeconds))
            {
                cfg.BackgroundRecoveryPulseSeconds = Math.Clamp(recoveryPulseSeconds, 1, 30);
                SaveAndApply();
            }
        }
        UiHelpers.Wrapped(plugin.BackgroundRecoveryStatus);
    }

    private void DrawForegroundNoRenderModeSelector()
    {
        var cfg = plugin.Configuration;
        var safeMode = cfg.ForegroundNoRenderMode == ForegroundNoRenderMode.SafeFrozenFrame;
        if (ImGui.RadioButton("De-render with occasional recovery frames", safeMode))
            plugin.SetForegroundNoRenderMode(ForegroundNoRenderMode.SafeFrozenFrame, "main render tab");
        UiHelpers.Tooltip("Uses the render gate path and leaves the last rendered frame visible.");

        var legacyMode = cfg.ForegroundNoRenderMode == ForegroundNoRenderMode.LegacyBlackScreen;
        if (ImGui.RadioButton("De-render with black screen", legacyMode))
            plugin.SetForegroundNoRenderMode(ForegroundNoRenderMode.LegacyBlackScreen, "main render tab");
        UiHelpers.Tooltip("Uses the legacy render-byte path and blanks foreground rendering.");
    }

    private void DrawForegroundDisplayRecoveryControls()
    {
        var cfg = plugin.Configuration;

        UiHelpers.SectionHeader("Display Recovery");
        var guardEnabled = cfg.ForegroundDisplayRecoveryGuardEnabled;
        if (ImGui.Checkbox("Display recovery guard", ref guardEnabled))
        {
            cfg.ForegroundDisplayRecoveryGuardEnabled = guardEnabled;
            SaveAndApply();
        }

        if (cfg.ForegroundDisplayRecoveryGuardEnabled)
        {
            var pauseSeconds = cfg.ForegroundDisplayRecoveryPauseSeconds;
            if (ImGui.InputInt("Recovery pause seconds", ref pauseSeconds))
            {
                cfg.ForegroundDisplayRecoveryPauseSeconds = Math.Clamp(pauseSeconds, 15, 900);
                SaveAndApply();
            }

            var stableSeconds = cfg.ForegroundDisplayRecoveryStableSeconds;
            if (ImGui.InputInt("Stable seconds", ref stableSeconds))
            {
                cfg.ForegroundDisplayRecoveryStableSeconds = Math.Clamp(stableSeconds, 5, 300);
                SaveAndApply();
            }
        }

        UiHelpers.StatusPill("Recovery", plugin.DisplayRecoveryService.RecoveryActive, "ACTIVE", "IDLE");
        ImGui.SameLine();
        UiHelpers.StatusPill("Bypass", plugin.ForegroundRenderControlService.DisplayRecoveryBypassActive, "ON", "OFF");
        UiHelpers.Wrapped(plugin.DisplayRecoveryService.Status);
    }

    private void DrawCrowdTab()
    {
        var cfg = plugin.Configuration;

        UiHelpers.SectionHeader("Visibility");
        var crowdEnabled = cfg.CrowdSuppressionEnabled;
        if (ImGui.Checkbox("Crowd suppression", ref crowdEnabled))
            plugin.SetCrowdSuppressionEnabled(crowdEnabled, "main crowd tab", enablePluginOnEnable: true);
        UiHelpers.HotkeyStatus("Hotkey", cfg.CrowdToggleHotkey);
        DrawToggle("Hide players", cfg.HideNonPartyPlayers, value => cfg.HideNonPartyPlayers = value);
        DrawToggle("Hide pets", cfg.HideNonPartyPets, value => cfg.HideNonPartyPets = value);
        DrawToggle("Hide chocobos", cfg.HideNonPartyChocobos, value => cfg.HideNonPartyChocobos = value);
        DrawToggle("Hide minions", cfg.HideNonPartyMinions, value => cfg.HideNonPartyMinions = value);
        DrawToggle("Keep target visible", cfg.KeepCurrentTargetVisible, value => cfg.KeepCurrentTargetVisible = value);

        UiHelpers.SectionHeader("Counts");
        if (ImGui.BeginTable("##DpsCrowdCounts", 2, ImGuiTableFlags.SizingStretchProp))
        {
            DrawCountRow("Players", plugin.ActorSuppressionService.HiddenPlayers);
            DrawCountRow("Pets", plugin.ActorSuppressionService.HiddenPets);
            DrawCountRow("Chocobos", plugin.ActorSuppressionService.HiddenChocobos);
            DrawCountRow("Minions", plugin.ActorSuppressionService.HiddenMinions);
            ImGui.EndTable();
        }

        UiHelpers.Wrapped(plugin.ActorSuppressionService.Status);
        if (UiHelpers.CompactButton("Show Everything Again", 170f, "Disable DPS and restore all hidden crowd actors."))
            plugin.SetPluginEnabled(false, "crowd tab", showAllOnDisable: true);
    }

    private void DrawHotkeysTab()
    {
        ProcessHotkeyCapture();

        UiHelpers.SectionHeader("Hotkeys");
        if (ImGui.BeginTable("##DpsHotkeyTable", 5, ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Action");
            ImGui.TableSetupColumn("Enable");
            ImGui.TableSetupColumn("Binding");
            ImGui.TableSetupColumn("Set");
            ImGui.TableSetupColumn("Clear");
            ImGui.TableHeadersRow();

            DrawHotkeyRow("Foreground", HotkeyTarget.Foreground, plugin.Configuration.ForegroundToggleHotkey);
            DrawHotkeyRow("Background", HotkeyTarget.Background, plugin.Configuration.BackgroundToggleHotkey);
            DrawHotkeyRow("Crowd", HotkeyTarget.Crowd, plugin.Configuration.CrowdToggleHotkey);
            DrawHotkeyRow("All Off", HotkeyTarget.AllOff, plugin.Configuration.AllOffHotkey);
            DrawHotkeyRow("Window + Size Load", HotkeyTarget.WindowPlacementAndSizeLoad, plugin.Configuration.WindowPlacementAndSizeLoadHotkey);

            ImGui.EndTable();
        }

        if (hotkeyCaptureTarget is { } target)
        {
            ImGui.Spacing();
            UiHelpers.StatusPill("Capture", $"{HotkeyTargetLabel(target)} listening", UiHelpers.Info);
        }
    }

    private void DrawHotkeyRow(string label, HotkeyTarget target, HotkeyBinding binding)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);

        ImGui.TableSetColumnIndex(1);
        var enabled = binding.Enabled;
        if (ImGui.Checkbox($"##{target}Enabled", ref enabled))
        {
            binding.Enabled = enabled;
            if (enabled)
                ClearDuplicateHotkeys(target, binding);
            SaveAndApply();
        }

        ImGui.TableSetColumnIndex(2);
        ImGui.TextUnformatted(UiHelpers.HotkeyStatusText(binding));

        ImGui.TableSetColumnIndex(3);
        if (UiHelpers.SmallButton(
                hotkeyCaptureTarget == target ? $"...##{target}Set" : $"Set##{target}",
                $"Capture a new hotkey for {label}."))
        {
            hotkeyCaptureTarget = target;
        }

        ImGui.TableSetColumnIndex(4);
        if (UiHelpers.SmallButton($"Clear##{target}", $"Clear the {label} hotkey."))
        {
            binding.Clear();
            if (hotkeyCaptureTarget == target)
                hotkeyCaptureTarget = null;
            SaveAndApply();
        }
    }

    private void ProcessHotkeyCapture()
    {
        if (hotkeyCaptureTarget == null)
            return;

        if (!TryReadCapturedHotkey(out var binding, out var clear, out var cancel))
            return;

        var target = hotkeyCaptureTarget.Value;
        hotkeyCaptureTarget = null;

        if (cancel)
            return;

        var targetBinding = GetHotkeyBinding(target);
        if (clear)
            targetBinding.Clear();
        else
            AssignHotkey(target, binding);

        SaveAndApply();
    }

    private static bool TryReadCapturedHotkey(out HotkeyBinding binding, out bool clear, out bool cancel)
    {
        binding = new HotkeyBinding();
        clear = false;
        cancel = false;

        foreach (var key in Plugin.KeyState.GetValidVirtualKeys())
        {
            var keyCode = (int)key;
            if (!Plugin.IsVirtualKeyPressed(keyCode))
                continue;

            if (keyCode == (int)VirtualKey.ESCAPE)
            {
                cancel = true;
                return true;
            }

            if (keyCode == (int)VirtualKey.BACK || keyCode == (int)VirtualKey.DELETE)
            {
                clear = true;
                return true;
            }

            if (!IsCaptureKey(keyCode))
                continue;

            binding = new HotkeyBinding
            {
                Enabled = true,
                KeyCode = keyCode,
                Ctrl = Plugin.IsCtrlDown(),
                Alt = Plugin.IsAltDown(),
                Shift = Plugin.IsShiftDown(),
            };
            return true;
        }

        return false;
    }

    private static bool IsCaptureKey(int keyCode)
        => keyCode != (int)VirtualKey.NO_KEY
        && keyCode != (int)VirtualKey.LBUTTON
        && keyCode != (int)VirtualKey.RBUTTON
        && keyCode != (int)VirtualKey.MBUTTON
        && keyCode != (int)VirtualKey.XBUTTON1
        && keyCode != (int)VirtualKey.XBUTTON2
        && keyCode != (int)VirtualKey.ESCAPE
        && keyCode != (int)VirtualKey.BACK
        && keyCode != (int)VirtualKey.DELETE
        && !Plugin.IsModifierKey(keyCode)
        && Plugin.KeyState.IsVirtualKeyValid(keyCode);

    private void AssignHotkey(HotkeyTarget target, HotkeyBinding binding)
    {
        ClearDuplicateHotkeys(target, binding);
        GetHotkeyBinding(target).SetFrom(binding);
    }

    private void ClearDuplicateHotkeys(HotkeyTarget owner, HotkeyBinding binding)
    {
        if (!binding.HasChord)
            return;

        foreach (var target in Enum.GetValues<HotkeyTarget>())
        {
            if (target == owner)
                continue;

            var other = GetHotkeyBinding(target);
            if (other.SameChord(binding))
                other.Clear();
        }
    }

    private HotkeyBinding GetHotkeyBinding(HotkeyTarget target)
        => target switch
        {
            HotkeyTarget.Foreground => plugin.Configuration.ForegroundToggleHotkey,
            HotkeyTarget.Background => plugin.Configuration.BackgroundToggleHotkey,
            HotkeyTarget.Crowd => plugin.Configuration.CrowdToggleHotkey,
            HotkeyTarget.AllOff => plugin.Configuration.AllOffHotkey,
            HotkeyTarget.WindowPlacementAndSizeLoad => plugin.Configuration.WindowPlacementAndSizeLoadHotkey,
            _ => plugin.Configuration.AllOffHotkey,
        };

    private static string HotkeyTargetLabel(HotkeyTarget target)
        => target switch
        {
            HotkeyTarget.Foreground => "Foreground",
            HotkeyTarget.Background => "Background",
            HotkeyTarget.Crowd => "Crowd",
            HotkeyTarget.AllOff => "All Off",
            HotkeyTarget.WindowPlacementAndSizeLoad => "Window + Size Load",
            _ => "Hotkey",
        };

    private void DrawWindowPlacementTab()
    {
        var cfg = plugin.Configuration;
        var hasCurrent = plugin.WindowPlacementService.TryReadCurrentPlacement(out var current, out var currentStatus);

        UiHelpers.SectionHeader("Current Game Window");
        if (hasCurrent)
        {
            if (ImGui.BeginTable("##DpsCurrentGameWindowPlacement", 2, ImGuiTableFlags.SizingStretchProp))
            {
                DrawInfoRow("X", current.X.ToString());
                DrawInfoRow("Y", current.Y.ToString());
                DrawInfoRow("Width", current.Width.ToString());
                DrawInfoRow("Height", current.Height.ToString());
                DrawInfoRow("Monitor", WindowPlacementService.FormatMonitor(current.MonitorDeviceName));
                DrawInfoRow("Monitor bounds", WindowPlacementService.FormatBounds(current.MonitorLeft, current.MonitorTop, current.MonitorRight, current.MonitorBottom));
                ImGui.EndTable();
            }
        }
        else
        {
            UiHelpers.WarningStrip(currentStatus);
        }

        if (hasCurrent)
        {
            var liveX = current.X;
            var liveY = current.Y;
            var liveWidth = Math.Max(1, current.Width);
            var liveHeight = Math.Max(1, current.Height);

            UiHelpers.SectionHeader("Position Editor");
            DrawWindowScalarEditor("X", ref liveX, target => plugin.MoveGameWindow(target, liveY, "main window xy position editor"), positiveOnly: false);
            DrawWindowScalarEditor("Y", ref liveY, target => plugin.MoveGameWindow(liveX, target, "main window xy position editor"), positiveOnly: false);

            UiHelpers.SectionHeader("Size Editor");
            DrawWindowScalarEditor("Width", ref liveWidth, target => plugin.ResizeGameWindow(target, liveHeight, "main window xy size editor"), positiveOnly: true);
            DrawWindowScalarEditor("Height", ref liveHeight, target => plugin.ResizeGameWindow(liveWidth, target, "main window xy size editor"), positiveOnly: true);
        }

        UiHelpers.SectionHeader("Saved Window");
        var saved = cfg.WindowPlacement;
        if (saved == null)
        {
            ImGui.TextDisabled("No saved game window placement/size.");
        }
        else if (ImGui.BeginTable("##DpsSavedGameWindowPlacement", 2, ImGuiTableFlags.SizingStretchProp))
        {
            DrawInfoRow("X", saved.X.ToString());
            DrawInfoRow("Y", saved.Y.ToString());
            DrawInfoRow("Width", FormatSavedWindowDimension(saved.Width));
            DrawInfoRow("Height", FormatSavedWindowDimension(saved.Height));
            DrawInfoRow("Monitor", WindowPlacementService.FormatMonitor(saved.MonitorDeviceName));
            DrawInfoRow("Monitor bounds", WindowPlacementService.FormatBounds(saved.MonitorLeft, saved.MonitorTop, saved.MonitorRight, saved.MonitorBottom));
            DrawInfoRow("Saved UTC", saved.SavedUtc == default ? "unknown" : saved.SavedUtc.ToString("u"));
            ImGui.EndTable();
        }

        UiHelpers.SectionHeader("Load");
        var autoLoad = cfg.WindowPlacementAutoLoadEnabled;
        if (ImGui.Checkbox("Load saved window position + display on client load", ref autoLoad))
            plugin.SetWindowPlacementAutoLoadEnabled(autoLoad, "main window xy tab");

        var sizeAutoLoad = cfg.WindowSizeAutoLoadEnabled;
        if (ImGui.Checkbox("Load saved window size on client load", ref sizeAutoLoad))
            plugin.SetWindowSizeAutoLoadEnabled(sizeAutoLoad, "main window xy tab");

        if (UiHelpers.CompactButton("Save Current Window", 164f, "Save the current game client position, size, and monitor."))
            plugin.SaveCurrentWindowPlacement("main window xy tab");
        UiHelpers.SameLineIfFits(164f);
        if (UiHelpers.CompactButton("Load Saved Window", 156f, "Move the game client to the saved position and monitor."))
            plugin.LoadSavedWindowPlacement("main window xy tab");
        UiHelpers.SameLineIfFits(142f);
        if (UiHelpers.CompactButton("Load Saved Size", 134f, "Resize the game client to the saved size."))
            plugin.LoadSavedWindowSize("main window xy tab");
        UiHelpers.SameLineIfFits(220f);
        if (UiHelpers.CompactButton("Load Saved Window + Size", 210f, "Move the game client to the saved position/display, then apply the saved size."))
            plugin.LoadSavedWindowPlacementAndSize("main window xy tab");
        UiHelpers.SameLineIfFits(132f);
        if (UiHelpers.CompactButton("Reset This Tab", 122f, "Clear saved game window placement/size and disable auto-load."))
            plugin.ResetWindowPlacementTab("main window xy tab");

        UiHelpers.SectionHeader("Last Action");
        UiHelpers.Wrapped(plugin.WindowPlacementService.Status);
    }

    private void DrawWindowScalarEditor(string label, ref int value, Func<int, bool> applyValue, bool positiveOnly)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(78f);

        var inputValue = value;
        ImGui.SetNextItemWidth(110f);
        if (ImGui.InputInt($"##DpsWindow{label}", ref inputValue, 0, 0))
            ApplyWindowScalarValue(ref value, inputValue, applyValue, positiveOnly);

        ImGui.SameLine();
        if (UiHelpers.SmallButton($"[-]##DpsWindow{label}Minus", $"Decrease {label} by 1."))
            ApplyWindowScalarValue(ref value, value - 1, applyValue, positiveOnly);

        ImGui.SameLine();
        if (UiHelpers.SmallButton($"[+]##DpsWindow{label}Plus", $"Increase {label} by 1."))
            ApplyWindowScalarValue(ref value, value + 1, applyValue, positiveOnly);
    }

    private static bool ApplyWindowScalarValue(ref int value, int target, Func<int, bool> applyValue, bool positiveOnly)
    {
        if (positiveOnly)
            target = Math.Max(1, target);

        if (target == value)
            return false;

        if (!applyValue(target))
            return false;

        value = target;
        return true;
    }

    private static string FormatSavedWindowDimension(int value)
        => value > 0 ? value.ToString() : "unknown";

    private void DrawDiagnosticsTab()
    {
        UiHelpers.SectionHeader("Startup");
        UiHelpers.Wrapped(plugin.StartupAutoApplyStatus);

        UiHelpers.SectionHeader("Foreground Render");
        UiHelpers.Wrapped(plugin.ForegroundRenderControlService.GetDiagnosticsLine());

        UiHelpers.SectionHeader("Display Recovery");
        UiHelpers.StatusPill("Guard", plugin.Configuration.ForegroundDisplayRecoveryGuardEnabled, "ON", "OFF");
        ImGui.SameLine();
        UiHelpers.StatusPill("Recovery", plugin.DisplayRecoveryService.RecoveryActive, "ACTIVE", "IDLE");
        ImGui.SameLine();
        UiHelpers.StatusPill("Bypass", plugin.ForegroundRenderControlService.DisplayRecoveryBypassActive, "ON", "OFF");
        if (ImGui.BeginTable("##DpsDisplayRecoveryDiagnostics", 2, ImGuiTableFlags.SizingStretchProp))
        {
            DrawInfoRow("Trigger", plugin.DisplayRecoveryService.TriggerReason);
            DrawInfoRow("Last change UTC", plugin.DisplayRecoveryService.LastChangeText);
            DrawInfoRow("Rearm ETA", plugin.DisplayRecoveryService.RearmEtaText);
            DrawInfoRow("Poll seconds", plugin.DisplayRecoveryService.PollInterval.ToString());
            ImGui.EndTable();
        }
        UiHelpers.Wrapped(plugin.DisplayRecoveryService.Status);
        UiHelpers.Wrapped(plugin.DisplayRecoveryService.CurrentSnapshotText);

        UiHelpers.SectionHeader("Background Hook");
        UiHelpers.StatusPill("Hook active", plugin.BackgroundRenderGateService.HooksActive, "YES", "NO");
        ImGui.SameLine();
        UiHelpers.StatusPill("No-render", plugin.BackgroundRenderGateService.IsBackgroundNoRenderActive, "YES", "NO");
        ImGui.SameLine();
        UiHelpers.StatusPill("Transition bypass", plugin.BackgroundRenderGateService.TransitionBypassActive, "YES", "NO");
        UiHelpers.Wrapped(plugin.BackgroundRenderGateService.Status);
        UiHelpers.Wrapped($"Max render-hook delay: {plugin.BackgroundRenderGateService.MaxRenderHookDelayMs:0.0} ms");

        UiHelpers.SectionHeader("Recovery");
        UiHelpers.Wrapped(plugin.BackgroundRecoveryStatus);

        UiHelpers.SectionHeader("Helper Plugins");
        DrawHelperStatus("Custom Resolution", plugin.IsCustomResolutionInstalled());
        DrawHelperStatus("TTSL", IsPluginLoaded("TTSL"));

        UiHelpers.SectionHeader("Texture Lab");
        if (plugin.DebugModeEnabled)
            DrawTextureLab("Main");
        else
        {
            ImGui.TextDisabled("Debug texture lab hidden for this session.");
            if (UiHelpers.CompactButton("Enable Debug", 112f, "Show texture diagnostics for this session."))
                plugin.SetDebugMode(true);
        }
    }

    private void DrawAboutTab()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

        UiHelpers.SectionHeader("Links");
        UiHelpers.LinkButton("Ko-fi", PluginInfo.SupportUrl, "Open Ko-fi support page.");
        ImGui.SameLine();
        UiHelpers.LinkButton("Discord", PluginInfo.DiscordUrl, "Open Discord community link.");
        UiHelpers.Wrapped(PluginInfo.DiscordFeedbackNote);

        UiHelpers.SectionHeader("Version");
        ImGui.TextUnformatted($"{PluginInfo.DisplayName} {version}");
        ImGui.TextUnformatted(PluginInfo.Summary);

        UiHelpers.SectionHeader("Commands");
        ImGui.TextUnformatted("/dps");
        ImGui.TextUnformatted("/dps roff    arm background no-render");
        ImGui.TextUnformatted("/dps ron     restore background no-render");
        ImGui.TextUnformatted("/dps foff    foreground render OFF");
        ImGui.TextUnformatted("/dps fon     foreground render ON");
        ImGui.TextUnformatted("/dps ws      move plugin UI window to 1,1");
        ImGui.TextUnformatted("/dps j       randomize plugin UI window in viewport");
        ImGui.TextUnformatted("/dps wsave   save game window X/Y + size + monitor");
        ImGui.TextUnformatted("/dps wload   load saved game window X/Y + monitor");
        ImGui.TextUnformatted("/dps wloadall load saved game window X/Y + monitor + size");
        ImGui.TextUnformatted("/dps wreset  reset saved game window position/size");
        ImGui.TextUnformatted("/dps debug   show texture lab");
        ImGui.TextUnformatted("/dps debug off");
    }

    private void DrawDtrTab()
    {
        DrawDtrDisplaySection();

        ImGui.Spacing();
        ImGui.Separator();

        DrawDtrClickActionsSection();
    }

    private void DrawDtrDisplaySection()
    {
        var cfg = plugin.Configuration;

        UiHelpers.SectionHeader("Display");
        var dtrEnabled = cfg.DtrBarEnabled;
        if (ImGui.Checkbox("Show DTR bar entry", ref dtrEnabled))
        {
            cfg.DtrBarEnabled = dtrEnabled;
            SaveAndApply(updateDtr: true);
        }

        var mode = cfg.DtrBarMode;
        if (ImGui.BeginCombo("DTR mode", DtrModeLabel(mode)))
        {
            for (var value = 0; value <= 2; value++)
            {
                var selected = value == mode;
                if (ImGui.Selectable(DtrModeLabel(value), selected))
                {
                    cfg.DtrBarMode = value;
                    SaveAndApply(updateDtr: true);
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        var enabledIcon = cfg.DtrIconEnabled;
        if (ImGui.InputText("Enabled icon", ref enabledIcon, 16))
        {
            cfg.DtrIconEnabled = enabledIcon;
            SaveAndApply(updateDtr: true);
        }

        var disabledIcon = cfg.DtrIconDisabled;
        if (ImGui.InputText("Disabled icon", ref disabledIcon, 16))
        {
            cfg.DtrIconDisabled = disabledIcon;
            SaveAndApply(updateDtr: true);
        }

        UiHelpers.StatusPill("Plugin", cfg.PluginEnabled);
        ImGui.SameLine();
        UiHelpers.StatusPill("DTR", cfg.DtrBarEnabled);
        ImGui.SameLine();
        UiHelpers.StatusPill("Click", HasAnyDtrClickAction(cfg), "CONFIGURED", "NOOP");
        UiHelpers.Wrapped($"Preview: {DtrPreviewText(cfg)}");
        UiHelpers.Wrapped($"Tooltip: {PluginInfo.DisplayName} {(cfg.PluginEnabled ? "On" : "Off")}. {plugin.GetDtrClickActionSummary()}");
    }

    private void DrawDtrClickActionsSection()
    {
        var cfg = plugin.Configuration;

        UiHelpers.SectionHeader("Click Actions");
        DrawDtrClickActionToggle("Turn everything off", cfg.DtrClickTurnEverythingOff, value => cfg.DtrClickTurnEverythingOff = value);
        DrawDtrClickActionToggle("Toggle plugin enabled", cfg.DtrClickTogglePluginEnabled, value => cfg.DtrClickTogglePluginEnabled = value);
        DrawDtrClickActionToggle("Toggle background no-render", cfg.DtrClickToggleBackgroundNoRender, value => cfg.DtrClickToggleBackgroundNoRender = value);
        DrawDtrClickActionToggle("Toggle foreground no-render", cfg.DtrClickToggleForegroundNoRender, value => cfg.DtrClickToggleForegroundNoRender = value);
        DrawDtrClickActionToggle("Toggle crowd suppression", cfg.DtrClickToggleCrowdSuppression, value => cfg.DtrClickToggleCrowdSuppression = value);
        DrawDtrClickActionToggle("Open main window", cfg.DtrClickOpenMainWindow, value => cfg.DtrClickOpenMainWindow = value);

        if (UiHelpers.CompactButton("Reset DTR Defaults", 154f, "Restore default DTR click actions."))
        {
            cfg.DtrClickTurnEverythingOff = true;
            cfg.DtrClickOpenMainWindow = true;
            cfg.DtrClickTogglePluginEnabled = false;
            cfg.DtrClickToggleBackgroundNoRender = false;
            cfg.DtrClickToggleForegroundNoRender = false;
            cfg.DtrClickToggleCrowdSuppression = false;
            SaveAndApply(updateDtr: true);
        }

        UiHelpers.Wrapped(plugin.GetDtrClickActionSummary());
    }

    private void DrawDtrClickActionToggle(string label, bool value, Action<bool> setValue)
    {
        var current = value;
        if (ImGui.Checkbox(label, ref current))
        {
            setValue(current);
            SaveAndApply(updateDtr: true);
        }
    }

    private static bool HasAnyDtrClickAction(Configuration cfg)
        => cfg.DtrClickTurnEverythingOff
        || cfg.DtrClickTogglePluginEnabled
        || cfg.DtrClickToggleBackgroundNoRender
        || cfg.DtrClickToggleForegroundNoRender
        || cfg.DtrClickToggleCrowdSuppression
        || cfg.DtrClickOpenMainWindow;

    private static string DtrPreviewText(Configuration cfg)
    {
        var icon = cfg.PluginEnabled ? cfg.DtrIconEnabled : cfg.DtrIconDisabled;
        var status = cfg.PluginEnabled ? "On" : "Off";
        return cfg.DtrBarMode switch
        {
            1 => $"{icon} DPS",
            2 => icon,
            _ => $"DPS: {status}",
        };
    }

    private void DrawTextureLab(string id)
    {
        var cfg = plugin.Configuration;

        var textureEnabled = cfg.TextureRedirectEnabled;
        if (ImGui.Checkbox($"Enable texture redirect##{id}", ref textureEnabled))
        {
            cfg.TextureRedirectEnabled = textureEnabled;
            SaveAndApply();
        }

        var scope = cfg.TextureRedirectScope;
        if (ImGui.BeginCombo($"Texture scope##{id}", TextureRedirectService.ScopeLabel(scope)))
        {
            foreach (var value in Enum.GetValues<TextureRedirectScope>())
            {
                var selected = value == scope;
                if (ImGui.Selectable(TextureRedirectService.ScopeLabel(value), selected))
                {
                    cfg.TextureRedirectScope = value;
                    SaveAndApply();
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        var asset = cfg.TextureReplacementAsset;
        if (ImGui.BeginCombo($"Replacement asset##{id}", TextureRedirectService.AssetLabel(asset)))
        {
            foreach (var value in Enum.GetValues<TextureReplacementAsset>())
            {
                var selected = value == asset;
                if (ImGui.Selectable(TextureRedirectService.AssetLabel(value), selected))
                {
                    cfg.TextureReplacementAsset = value;
                    SaveAndApply();
                }

                if (selected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        var logRedirects = cfg.LogTextureRedirects;
        if (ImGui.Checkbox($"Log redirected textures##{id}", ref logRedirects))
        {
            cfg.LogTextureRedirects = logRedirects;
            SaveAndApply();
        }

        if (UiHelpers.CompactButton($"Chara##{id}", 74f, "Redirect character textures to the black 16x16 asset."))
            ApplyTexturePreset(TextureRedirectScope.CharaOnly, TextureReplacementAsset.Black16x16);
        ImGui.SameLine();
        if (UiHelpers.CompactButton($"World##{id}", 74f, "Redirect world textures to the black 16x16 asset."))
            ApplyTexturePreset(TextureRedirectScope.WorldOnly, TextureReplacementAsset.Black16x16);
        ImGui.SameLine();
        if (UiHelpers.CompactButton($"VFX##{id}", 64f, "Redirect effect textures to the black 16x16 asset."))
            ApplyTexturePreset(TextureRedirectScope.EffectsOnly, TextureReplacementAsset.Black16x16);
        ImGui.SameLine();
        if (UiHelpers.CompactButton($"Global##{id}", 76f, "Redirect all tracked textures to the black 1x1 asset."))
            ApplyTexturePreset(TextureRedirectScope.EverythingTex, TextureReplacementAsset.Black1x1);

        ImGui.TextUnformatted($"Status: {plugin.TextureRedirectService.Status}");
        ImGui.TextUnformatted($"Redirected textures: {plugin.TextureRedirectService.RedirectedTextures}");
        ImGui.TextUnformatted($"Successful loads: {plugin.TextureRedirectService.SuccessfulRedirectLoads}");
        ImGui.TextUnformatted($"Failed loads: {plugin.TextureRedirectService.FailedRedirectLoads}");
        UiHelpers.Wrapped($"Last game path: {plugin.TextureRedirectService.LastGameTexturePath}");
        UiHelpers.Wrapped($"Replacement path: {plugin.TextureRedirectService.LastReplacementPath}");
        if (UiHelpers.SmallButton($"Clear Texture Stats##{id}", "Reset texture redirect counters."))
            plugin.TextureRedirectService.ResetStats();
    }

    private void DrawToggle(string label, bool value, Action<bool> setValue)
    {
        var current = value;
        if (ImGui.Checkbox(label, ref current))
        {
            setValue(current);
            SaveAndApply();
        }
    }

    private static void DrawCountRow(string label, int value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(value.ToString());
    }

    private static void DrawInfoRow(string label, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0);
        ImGui.TextUnformatted(label);
        ImGui.TableSetColumnIndex(1);
        ImGui.TextUnformatted(value);
    }

    private static void DrawHelperStatus(string label, bool loaded)
    {
        UiHelpers.StatusPill(label, loaded, "LOADED", "MISSING");
    }

    private void ApplyTexturePreset(TextureRedirectScope scope, TextureReplacementAsset asset)
    {
        var cfg = plugin.Configuration;
        cfg.PluginEnabled = true;
        cfg.TextureRedirectEnabled = true;
        cfg.TextureRedirectScope = scope;
        cfg.TextureReplacementAsset = asset;
        cfg.HideNonPartyPlayers = false;
        cfg.HideNonPartyPets = false;
        cfg.HideNonPartyChocobos = false;
        cfg.HideNonPartyMinions = false;
        SaveAndApply();
        plugin.UpdateDtrBar();
    }

    private void SaveAndApply(bool updateDtr = false)
    {
        plugin.Configuration.Save();
        plugin.ApplyConfiguration();
        if (updateDtr)
            plugin.UpdateDtrBar();
    }

    private static string DtrModeLabel(int mode)
        => mode switch
        {
            1 => "Icon + DPS",
            2 => "Icon only",
            _ => "Text status",
        };

    private static bool IsPluginLoaded(string internalName)
    {
        try
        {
            return Plugin.PluginInterface.InstalledPlugins.Any(plugin =>
                string.Equals(plugin.InternalName, internalName, StringComparison.OrdinalIgnoreCase) &&
                plugin.IsLoaded);
        }
        catch
        {
            return false;
        }
    }
}
