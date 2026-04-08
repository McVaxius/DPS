using System.Diagnostics;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DPS.Services;

namespace DPS.Windows;

public sealed class ConfigWindow : Window
{
    private readonly Plugin plugin;

    public ConfigWindow(Plugin plugin)
        : base($"{PluginInfo.DisplayName} Settings##DPSConfig")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new System.Numerics.Vector2(420f, 260f),
            MaximumSize = new System.Numerics.Vector2(900f, 800f),
        };
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        if (ImGui.SmallButton("Ko-fi##DpsConfig"))
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.SupportUrl, UseShellExecute = true });
        ImGui.SameLine();
        if (ImGui.SmallButton("Discord##DpsConfig"))
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.DiscordUrl, UseShellExecute = true });
        ImGui.TextDisabled(PluginInfo.DiscordFeedbackNote);
        ImGui.Separator();

        var dtrEnabled = cfg.DtrBarEnabled;
        if (ImGui.Checkbox("Show DTR bar entry", ref dtrEnabled))
        {
            cfg.DtrBarEnabled = dtrEnabled;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        ImGui.Separator();
        DrawSuggestedPluginStatus();

        ImGui.Separator();
        ImGui.Text("Experimental Background No-Render");
        ImGui.TextDisabled("Main release candidate feature. Use the big red button in the main window or /dps roff and /dps ron for the fastest live toggle.");

        var backgroundNoRenderEnabled = cfg.BackgroundNoRenderEnabled;
        if (ImGui.Checkbox("Enable background no-render", ref backgroundNoRenderEnabled))
            if (backgroundNoRenderEnabled)
                plugin.ArmBackgroundNoRender("settings");
            else
                plugin.DisableBackgroundNoRender("settings");

        var onlyWhenMinimized = cfg.BackgroundNoRenderOnlyWhenMinimized;
        if (ImGui.Checkbox("Only trigger while minimized/iconic", ref onlyWhenMinimized))
        {
            cfg.BackgroundNoRenderOnlyWhenMinimized = onlyWhenMinimized;
            cfg.Save();
            plugin.ApplyConfiguration();
        }

        var cleanDisable = cfg.CleanDisableExperimentalRenderHack;
        if (ImGui.Checkbox("Clean disable experimental render hack", ref cleanDisable))
        {
            cfg.CleanDisableExperimentalRenderHack = cleanDisable;
            cfg.Save();
            plugin.ApplyConfiguration();
        }
        ImGui.TextDisabled("If enabled, disabling background no-render fully disables DPS first to clear the render hack the same way as the plugin off path.");

        var safetyFrameInterval = cfg.BackgroundSafetyFrameIntervalSeconds;
        if (ImGui.SliderInt("Safety frame interval (sec)", ref safetyFrameInterval, 1, 60))
        {
            cfg.BackgroundSafetyFrameIntervalSeconds = safetyFrameInterval;
            cfg.Save();
            plugin.ApplyConfiguration();
        }

        var throttleSleepMs = cfg.BackgroundThrottleSleepMs;
        if (ImGui.SliderInt("Throttle sleep while gated (ms)", ref throttleSleepMs, 0, 200))
        {
            cfg.BackgroundThrottleSleepMs = throttleSleepMs;
            cfg.Save();
            plugin.ApplyConfiguration();
        }
        ImGui.TextDisabled($"Current cadence: {cfg.BackgroundSafetyFrameIntervalSeconds}s between safety frames (~{plugin.BackgroundRenderGateService.SafetyFramesPerMinute:0.0} frames/min).");

        var recoveryLoopEnabled = cfg.BackgroundRecoveryLoopEnabled;
        if (ImGui.Checkbox("Automatic /ron -> /roff recovery pulse", ref recoveryLoopEnabled))
        {
            cfg.BackgroundRecoveryLoopEnabled = recoveryLoopEnabled;
            cfg.Save();
            plugin.ApplyConfiguration();
        }

        if (cfg.BackgroundRecoveryLoopEnabled)
        {
            var recoveryMinMinutes = cfg.BackgroundRecoveryMinMinutes;
            if (ImGui.InputInt("Recovery minimum (min)", ref recoveryMinMinutes))
            {
                cfg.BackgroundRecoveryMinMinutes = Math.Clamp(recoveryMinMinutes, 1, 120);
                if (cfg.BackgroundRecoveryMaxMinutes < cfg.BackgroundRecoveryMinMinutes)
                    cfg.BackgroundRecoveryMaxMinutes = cfg.BackgroundRecoveryMinMinutes;
                cfg.Save();
                plugin.ApplyConfiguration();
            }

            var recoveryMaxMinutes = cfg.BackgroundRecoveryMaxMinutes;
            if (ImGui.InputInt("Recovery maximum (min)", ref recoveryMaxMinutes))
            {
                cfg.BackgroundRecoveryMaxMinutes = Math.Clamp(recoveryMaxMinutes, cfg.BackgroundRecoveryMinMinutes, 120);
                cfg.Save();
                plugin.ApplyConfiguration();
            }

            var recoveryPulseSeconds = cfg.BackgroundRecoveryPulseSeconds;
            if (ImGui.InputInt("Recovery pulse (sec)", ref recoveryPulseSeconds))
            {
                cfg.BackgroundRecoveryPulseSeconds = Math.Clamp(recoveryPulseSeconds, 1, 30);
                cfg.Save();
                plugin.ApplyConfiguration();
            }
        }

        ImGui.TextWrapped($"Render gate status: {plugin.BackgroundRenderGateService.Status}");
        ImGui.TextWrapped($"Recovery status: {plugin.BackgroundRecoveryStatus}");

        ImGui.Separator();
        ImGui.Text("Reduce Problems In Public Areas");
        ImGui.TextDisabled("Optional crowd reduction toggles when you want the client active but visually quieter.");

        var keepTargetVisible = cfg.KeepCurrentTargetVisible;
        if (ImGui.Checkbox("Keep current target visible", ref keepTargetVisible))
        {
            cfg.KeepCurrentTargetVisible = keepTargetVisible;
            cfg.Save();
            plugin.ApplyConfiguration();
        }

        var hidePlayers = cfg.HideNonPartyPlayers;
        if (ImGui.Checkbox("Hide non-party players", ref hidePlayers))
        {
            cfg.HideNonPartyPlayers = hidePlayers;
            cfg.Save();
            plugin.ApplyConfiguration();
        }

        var hidePets = cfg.HideNonPartyPets;
        if (ImGui.Checkbox("Hide non-party pets", ref hidePets))
        {
            cfg.HideNonPartyPets = hidePets;
            cfg.Save();
            plugin.ApplyConfiguration();
        }

        var hideChocobos = cfg.HideNonPartyChocobos;
        if (ImGui.Checkbox("Hide non-party chocobos", ref hideChocobos))
        {
            cfg.HideNonPartyChocobos = hideChocobos;
            cfg.Save();
            plugin.ApplyConfiguration();
        }

        var hideMinions = cfg.HideNonPartyMinions;
        if (ImGui.Checkbox("Hide non-party minions", ref hideMinions))
        {
            cfg.HideNonPartyMinions = hideMinions;
            cfg.Save();
            plugin.ApplyConfiguration();
        }

        ImGui.Separator();
        if (plugin.DebugModeEnabled)
        {
            ImGui.Text("Experimental Texture Redirect");
            ImGui.TextDisabled("Direct in-place texture blacking experiment. Character/world/VFX/global scopes all stay available.");

            var textureEnabled = cfg.TextureRedirectEnabled;
            if (ImGui.Checkbox("Enable texture redirect##Config", ref textureEnabled))
            {
                cfg.TextureRedirectEnabled = textureEnabled;
                cfg.Save();
                plugin.ApplyConfiguration();
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
                        cfg.Save();
                        plugin.ApplyConfiguration();
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
                        cfg.Save();
                        plugin.ApplyConfiguration();
                    }

                    if (selected)
                        ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }
            ImGui.TextDisabled("Size selection is currently ignored because the loaded source texture is blacked in place.");

            var logRedirects = cfg.LogTextureRedirects;
            if (ImGui.Checkbox("Log redirected textures##Config", ref logRedirects))
            {
                cfg.LogTextureRedirects = logRedirects;
                cfg.Save();
                plugin.ApplyConfiguration();
            }

            ImGui.TextDisabled("Use 16x16 first. Global + 1x1 remain the riskiest combination.");
            ImGui.TextWrapped($"Hook status: {plugin.TextureRedirectService.Status}");
        }
        else
        {
            ImGui.TextDisabled("Experimental texture controls are hidden. Use /dps debug to expose them for this session.");
        }
    }

    private void DrawSuggestedPluginStatus()
    {
        ImGui.Text("Suggested Plugins");
        ImGui.TextDisabled("DPS runs on its own. These are companion helpers only.");

        DrawPluginStatusLine(
            "Custom Resolution",
            plugin.IsCustomResolutionInstalled(),
            "This lets you change internal display res.");

        DrawPluginStatusLine(
            "TTSL (Thick Thighs Save Lives)",
            IsPluginLoaded("TTSL"),
            "This lets you remote view parties. Handy if renderer is off.");
    }

    private static void DrawPluginStatusLine(string name, bool installed, string description)
    {
        var color = installed
            ? new System.Numerics.Vector4(0.2f, 0.8f, 0.2f, 1f)
            : new System.Numerics.Vector4(0.85f, 0.7f, 0.2f, 1f);

        ImGui.TextColored(color, installed ? $"[OK] {name}" : $"[--] {name}");
        ImGui.SameLine();
        ImGui.TextDisabled(installed ? "Installed" : "Not installed");
        ImGui.TextWrapped(description);
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
