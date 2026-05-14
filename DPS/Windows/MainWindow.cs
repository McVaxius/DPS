using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
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

    private readonly Plugin plugin;
    private PendingPlacement pendingPlacement;

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
        UiHelpers.StatusPill("BG", cfg.BackgroundNoRenderEnabled, plugin.BackgroundRenderGateService.IsNoRenderActive ? "ACTIVE" : "ARMED");
        UiHelpers.SameLineIfFits(132f);
        UiHelpers.ForegroundRenderStatus(plugin, includeIntent: false);

        ImGui.Spacing();
        if (UiHelpers.CompactButton(cfg.PluginEnabled ? "Stop" : "Run", 58f))
            plugin.SetPluginEnabled(!cfg.PluginEnabled, "main window top bar", showAllOnDisable: cfg.PluginEnabled);
        ImGui.SameLine();
        if (UiHelpers.CompactButton(cfg.DtrBarEnabled ? "DTR On" : "DTR Off", 72f))
        {
            cfg.DtrBarEnabled = !cfg.DtrBarEnabled;
            cfg.Save();
            plugin.UpdateDtrBar();
        }
        ImGui.SameLine();
        if (UiHelpers.CompactButton("Settings", 76f))
            plugin.ToggleConfigUi();
        ImGui.SameLine();
        UiHelpers.LinkButton("Ko-fi", PluginInfo.SupportUrl);
        ImGui.SameLine();
        UiHelpers.LinkButton("Discord", PluginInfo.DiscordUrl);
        ImGui.SameLine();
        if (ImGui.SmallButton("Close"))
            IsOpen = false;

        ImGui.Separator();
    }

    private void DrawRenderTab()
    {
        var cfg = plugin.Configuration;

        UiHelpers.SectionHeader("Background");
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

        UiHelpers.StatusPill("Hook", plugin.BackgroundRenderGateService.HooksActive, "READY", "IDLE");
        ImGui.SameLine();
        UiHelpers.StatusPill("Gate", plugin.BackgroundRenderGateService.IsNoRenderActive, "ACTIVE", "WAIT");
        UiHelpers.Wrapped(plugin.BackgroundRenderGateService.Status);

        UiHelpers.SectionHeader("Foreground");
        UiHelpers.ForegroundRenderOffCheckbox(plugin, "main render tab");
        UiHelpers.ForegroundRenderStatus(plugin);
        UiHelpers.Wrapped(plugin.ForegroundRenderControlService.Status);

        if (cfg.BackgroundNoRenderEnabled && cfg.ForegroundNoRenderEnabled)
            UiHelpers.WarningStrip("Mutual exclusion failed: both render modes are enabled.", danger: true);
        else
            UiHelpers.StatusPill("Mutual exclusion", "OK", UiHelpers.Good);

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

        UiHelpers.SectionHeader("Restore");
        if (UiHelpers.CompactButton("Restore FG", 92f))
            plugin.DisableForegroundNoRender("main restore button");
        ImGui.SameLine();
        if (UiHelpers.CompactButton("Restore BG", 92f))
            plugin.DisableBackgroundNoRender("main restore button");
        ImGui.SameLine();
        if (UiHelpers.CompactButton("Show All", 86f))
            plugin.SetPluginEnabled(false, "main show all", showAllOnDisable: true);
    }

    private void DrawCrowdTab()
    {
        var cfg = plugin.Configuration;

        UiHelpers.SectionHeader("Visibility");
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
        if (UiHelpers.CompactButton("Show Everything Again", 170f))
            plugin.SetPluginEnabled(false, "crowd tab", showAllOnDisable: true);
    }

    private void DrawDiagnosticsTab()
    {
        UiHelpers.SectionHeader("Foreground Byte");
        UiHelpers.Wrapped(plugin.ForegroundRenderControlService.GetDiagnosticsLine());

        UiHelpers.SectionHeader("Background Hook");
        UiHelpers.StatusPill("Hook active", plugin.BackgroundRenderGateService.HooksActive, "YES", "NO");
        ImGui.SameLine();
        UiHelpers.StatusPill("No-render", plugin.BackgroundRenderGateService.IsNoRenderActive, "YES", "NO");
        UiHelpers.Wrapped(plugin.BackgroundRenderGateService.Status);

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
            if (UiHelpers.CompactButton("Enable Debug", 112f))
                plugin.SetDebugMode(true);
        }
    }

    private void DrawAboutTab()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

        UiHelpers.SectionHeader("Links");
        UiHelpers.LinkButton("Ko-fi", PluginInfo.SupportUrl);
        ImGui.SameLine();
        UiHelpers.LinkButton("Discord", PluginInfo.DiscordUrl);
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
        ImGui.TextUnformatted("/dps ws      move main/settings to 1,1");
        ImGui.TextUnformatted("/dps j       randomize main/settings in viewport");
        ImGui.TextUnformatted("/dps debug   show texture lab");
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

        if (UiHelpers.CompactButton($"Chara##{id}", 74f))
            ApplyTexturePreset(TextureRedirectScope.CharaOnly, TextureReplacementAsset.Black16x16);
        ImGui.SameLine();
        if (UiHelpers.CompactButton($"World##{id}", 74f))
            ApplyTexturePreset(TextureRedirectScope.WorldOnly, TextureReplacementAsset.Black16x16);
        ImGui.SameLine();
        if (UiHelpers.CompactButton($"VFX##{id}", 64f))
            ApplyTexturePreset(TextureRedirectScope.EffectsOnly, TextureReplacementAsset.Black16x16);
        ImGui.SameLine();
        if (UiHelpers.CompactButton($"Global##{id}", 76f))
            ApplyTexturePreset(TextureRedirectScope.EverythingTex, TextureReplacementAsset.Black1x1);

        ImGui.TextUnformatted($"Status: {plugin.TextureRedirectService.Status}");
        ImGui.TextUnformatted($"Redirected textures: {plugin.TextureRedirectService.RedirectedTextures}");
        ImGui.TextUnformatted($"Successful loads: {plugin.TextureRedirectService.SuccessfulRedirectLoads}");
        ImGui.TextUnformatted($"Failed loads: {plugin.TextureRedirectService.FailedRedirectLoads}");
        UiHelpers.Wrapped($"Last game path: {plugin.TextureRedirectService.LastGameTexturePath}");
        UiHelpers.Wrapped($"Replacement path: {plugin.TextureRedirectService.LastReplacementPath}");
        if (ImGui.SmallButton($"Clear Texture Stats##{id}"))
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

    private void SaveAndApply()
    {
        plugin.Configuration.Save();
        plugin.ApplyConfiguration();
    }

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
