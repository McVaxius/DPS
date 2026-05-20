using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Windowing;
using DPS.Services;

namespace DPS.Windows;

public sealed class ConfigWindow : Window
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
    }

    private readonly Plugin plugin;
    private PendingPlacement pendingPlacement;
    private HotkeyTarget? hotkeyCaptureTarget;

    public bool IsCapturingHotkey => hotkeyCaptureTarget != null;

    public ConfigWindow(Plugin plugin)
        : base($"{PluginInfo.DisplayName} Settings##DPSConfig")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(470f, 320f),
            MaximumSize = new Vector2(920f, 820f),
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

    public override void Draw()
    {
        ApplyPendingPlacement();

        ImGui.TextUnformatted($"{PluginInfo.DisplayName} Advanced Settings");
        ImGui.SameLine();
        UiHelpers.LinkButton("Ko-fi##DpsConfig", PluginInfo.SupportUrl);
        ImGui.SameLine();
        UiHelpers.LinkButton("Discord##DpsConfig", PluginInfo.DiscordUrl);
        ImGui.Separator();

        if (ImGui.BeginTabBar("##DpsConfigTabs"))
        {
            if (ImGui.BeginTabItem("DTR"))
            {
                DrawDtrTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Render"))
            {
                DrawRenderDefaultsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Crowd"))
            {
                DrawCrowdDefaultsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Texture Lab"))
            {
                DrawTextureLabTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Hotkeys"))
            {
                DrawHotkeysTab();
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

    private void DrawDtrTab()
    {
        var cfg = plugin.Configuration;

        UiHelpers.SectionHeader("DTR");
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
    }

    private void DrawRenderDefaultsTab()
    {
        var cfg = plugin.Configuration;

        UiHelpers.SectionHeader("Render Defaults");
        var pluginEnabled = cfg.PluginEnabled;
        if (ImGui.Checkbox("Plugin enabled", ref pluginEnabled))
            plugin.SetPluginEnabled(pluginEnabled, "settings render tab", showAllOnDisable: !pluginEnabled);

        var cleanDisable = cfg.CleanDisableExperimentalRenderHack;
        if (ImGui.Checkbox("Clean disable restores plugin state", ref cleanDisable))
        {
            cfg.CleanDisableExperimentalRenderHack = cleanDisable;
            SaveAndApply();
        }

        var onlyWhenMinimized = cfg.BackgroundNoRenderOnlyWhenMinimized;
        if (ImGui.Checkbox("Background only while minimized/iconic", ref onlyWhenMinimized))
        {
            cfg.BackgroundNoRenderOnlyWhenMinimized = onlyWhenMinimized;
            SaveAndApply();
        }

        var safetyFrameInterval = cfg.BackgroundSafetyFrameIntervalSeconds;
        if (ImGui.SliderInt("Safety frame interval (sec)", ref safetyFrameInterval, 1, 60))
        {
            cfg.BackgroundSafetyFrameIntervalSeconds = safetyFrameInterval;
            SaveAndApply();
        }

        var throttleSleepMs = cfg.BackgroundThrottleSleepMs;
        if (ImGui.SliderInt("Throttle sleep while gated (ms)", ref throttleSleepMs, 0, 200))
        {
            cfg.BackgroundThrottleSleepMs = throttleSleepMs;
            SaveAndApply();
        }

        UiHelpers.SectionHeader("Mode Toggles");
        UiHelpers.HotkeyStatus("Background hotkey", cfg.BackgroundToggleHotkey);
        var backgroundNoRenderEnabled = cfg.BackgroundNoRenderEnabled;
        if (ImGui.Checkbox("Background no-render", ref backgroundNoRenderEnabled))
        {
            if (backgroundNoRenderEnabled)
                plugin.ArmBackgroundNoRender("settings render tab");
            else
                plugin.DisableBackgroundNoRender("settings render tab");
        }

        UiHelpers.HotkeyStatus("Foreground hotkey", cfg.ForegroundToggleHotkey);
        UiHelpers.ForegroundRenderOffCheckbox(plugin, "settings render tab");
        UiHelpers.ForegroundRenderStatus(plugin);
        UiHelpers.Wrapped(plugin.ForegroundRenderControlService.Status);

        UiHelpers.SectionHeader("Recovery Pulse");
        var recoveryLoopEnabled = cfg.BackgroundRecoveryLoopEnabled;
        if (ImGui.Checkbox("Automatic recovery pulse", ref recoveryLoopEnabled))
        {
            cfg.BackgroundRecoveryLoopEnabled = recoveryLoopEnabled;
            SaveAndApply();
        }

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

        UiHelpers.Wrapped(plugin.BackgroundRecoveryStatus);
        UiHelpers.HotkeyStatus("All Off hotkey", cfg.AllOffHotkey);
        if (UiHelpers.CompactButton("All Off", 86f))
            plugin.AllOff("settings render tab");
    }

    private void DrawCrowdDefaultsTab()
    {
        var cfg = plugin.Configuration;

        UiHelpers.SectionHeader("Crowd Defaults");
        var crowdEnabled = cfg.CrowdSuppressionEnabled;
        if (ImGui.Checkbox("Crowd suppression", ref crowdEnabled))
            plugin.SetCrowdSuppressionEnabled(crowdEnabled, "settings crowd tab", enablePluginOnEnable: true);
        UiHelpers.HotkeyStatus("Crowd hotkey", cfg.CrowdToggleHotkey);
        DrawToggle("Hide players", cfg.HideNonPartyPlayers, value => cfg.HideNonPartyPlayers = value);
        DrawToggle("Hide pets", cfg.HideNonPartyPets, value => cfg.HideNonPartyPets = value);
        DrawToggle("Hide chocobos", cfg.HideNonPartyChocobos, value => cfg.HideNonPartyChocobos = value);
        DrawToggle("Hide minions", cfg.HideNonPartyMinions, value => cfg.HideNonPartyMinions = value);
        DrawToggle("Keep target visible", cfg.KeepCurrentTargetVisible, value => cfg.KeepCurrentTargetVisible = value);

        UiHelpers.SectionHeader("Current Counts");
        ImGui.TextUnformatted($"Players hidden: {plugin.ActorSuppressionService.HiddenPlayers}");
        ImGui.TextUnformatted($"Pets hidden: {plugin.ActorSuppressionService.HiddenPets}");
        ImGui.TextUnformatted($"Chocobos hidden: {plugin.ActorSuppressionService.HiddenChocobos}");
        ImGui.TextUnformatted($"Minions hidden: {plugin.ActorSuppressionService.HiddenMinions}");
        UiHelpers.Wrapped(plugin.ActorSuppressionService.Status);

        if (UiHelpers.CompactButton("Show Everything Again", 170f))
            plugin.SetPluginEnabled(false, "settings crowd tab", showAllOnDisable: true);
    }

    private void DrawTextureLabTab()
    {
        if (!plugin.DebugModeEnabled)
        {
            ImGui.TextDisabled("Debug texture lab hidden for this session.");
            if (UiHelpers.CompactButton("Enable Debug", 112f))
                plugin.SetDebugMode(true);
            return;
        }

        var cfg = plugin.Configuration;
        var textureEnabled = cfg.TextureRedirectEnabled;
        if (ImGui.Checkbox("Enable texture redirect##Config", ref textureEnabled))
        {
            cfg.TextureRedirectEnabled = textureEnabled;
            SaveAndApply();
        }

        var scope = cfg.TextureRedirectScope;
        if (ImGui.BeginCombo("Texture scope##Config", TextureRedirectService.ScopeLabel(scope)))
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
        if (ImGui.BeginCombo("Replacement asset##Config", TextureRedirectService.AssetLabel(asset)))
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
        if (ImGui.Checkbox("Log redirected textures##Config", ref logRedirects))
        {
            cfg.LogTextureRedirects = logRedirects;
            SaveAndApply();
        }

        UiHelpers.Wrapped(plugin.TextureRedirectService.Status);
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
        if (ImGui.SmallButton(hotkeyCaptureTarget == target ? $"...##{target}Set" : $"Set##{target}"))
            hotkeyCaptureTarget = target;

        ImGui.TableSetColumnIndex(4);
        if (ImGui.SmallButton($"Clear##{target}"))
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
            _ => plugin.Configuration.AllOffHotkey,
        };

    private static string HotkeyTargetLabel(HotkeyTarget target)
        => target switch
        {
            HotkeyTarget.Foreground => "Foreground",
            HotkeyTarget.Background => "Background",
            HotkeyTarget.Crowd => "Crowd",
            HotkeyTarget.AllOff => "All Off",
            _ => "Hotkey",
        };

    private void DrawAboutTab()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

        UiHelpers.SectionHeader("Support");
        UiHelpers.LinkButton("Ko-fi##ConfigAbout", PluginInfo.SupportUrl);
        ImGui.SameLine();
        UiHelpers.LinkButton("Discord##ConfigAbout", PluginInfo.DiscordUrl);
        UiHelpers.Wrapped(PluginInfo.DiscordFeedbackNote);

        UiHelpers.SectionHeader("Version");
        ImGui.TextUnformatted($"{PluginInfo.DisplayName} {version}");

        UiHelpers.SectionHeader("Commands");
        ImGui.TextUnformatted("/dps");
        ImGui.TextUnformatted("/dps roff    arm background no-render");
        ImGui.TextUnformatted("/dps ron     restore background no-render");
        ImGui.TextUnformatted("/dps foff    foreground render OFF");
        ImGui.TextUnformatted("/dps fon     foreground render ON");
        ImGui.TextUnformatted("/dps ws      move main/settings to 1,1");
        ImGui.TextUnformatted("/dps j       randomize main/settings in viewport");
        ImGui.TextUnformatted("/dps debug   show texture lab");
        ImGui.TextUnformatted("/dps debug off");
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
}
