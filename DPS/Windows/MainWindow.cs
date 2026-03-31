using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using DPS.Services;

namespace DPS.Windows;

public sealed class MainWindow : Window
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin)
        : base($"{PluginInfo.DisplayName}##DPSMain")
    {
        this.plugin = plugin;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(500f, 340f),
            MaximumSize = new Vector2(1100f, 900f),
        };
    }

    public override void Draw()
    {
        var cfg = plugin.Configuration;
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0.0";

        var discordWidth = ImGui.CalcTextSize("Discord").X + (ImGui.GetStyle().FramePadding.X * 2f);
        ImGui.Text($"{PluginInfo.DisplayName} v{version}");
        ImGui.SameLine(ImGui.GetWindowWidth() - (120f + discordWidth));
        if (ImGui.SmallButton("Ko-fi"))
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.SupportUrl, UseShellExecute = true });
        ImGui.SameLine();
        if (ImGui.SmallButton("Discord"))
            Process.Start(new ProcessStartInfo { FileName = PluginInfo.DiscordUrl, UseShellExecute = true });

        ImGui.Separator();
        ImGui.TextColored(new Vector4(1f, 0.35f, 0.35f, 1f), "Experimental Background No-Render");
        ImGui.TextDisabled("Background-only render gate. It skips the DX11 post-tick while the game is inactive and lets periodic safety frames through on a configurable cadence.");

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
        ImGui.TextDisabled("If enabled, /dps ron and the red-button disable path fully disable DPS to clear the render hack exactly like the plugin off path.");

        var safetyFrameInterval = cfg.BackgroundSafetyFrameIntervalSeconds;
        if (ImGui.SliderInt("Safety frame interval (sec)", ref safetyFrameInterval, 1, 60))
        {
            cfg.BackgroundSafetyFrameIntervalSeconds = safetyFrameInterval;
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

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.72f, 0.11f, 0.11f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.86f, 0.18f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.58f, 0.08f, 0.08f, 1f));
        var backgroundButtonText = cfg.BackgroundNoRenderEnabled
            ? "CLICK AGAIN TO DISABLE EXPERIMENTAL BACKGROUND NO-RENDER"
            : "EXPERIMENTAL BACKGROUND NO-RENDER";
        if (ImGui.Button(backgroundButtonText, new Vector2(-1f, 46f)))
        {
            if (cfg.BackgroundNoRenderEnabled)
                plugin.DisableBackgroundNoRender("main window");
            else
                plugin.ArmBackgroundNoRender("main window");
        }
        ImGui.PopStyleColor(3);

        ImGui.Text(cfg.BackgroundNoRenderEnabled
            ? $"State: armed. Click the red button again to disable{(cfg.CleanDisableExperimentalRenderHack ? " with clean-disable" : string.Empty)}."
            : "State: off. Click the red button to enable.");
        ImGui.TextWrapped($"Render gate status: {plugin.BackgroundRenderGateService.Status}");
        ImGui.TextWrapped($"Recovery status: {plugin.BackgroundRecoveryStatus}");
        ImGui.TextDisabled("Slash commands: /dps roff arms render-off. /dps ron disables it. After arming: alt-tab away, or minimize if you checked the iconic-only option.");

        ImGui.Separator();
        ImGui.Text("Optional Helpers");
        var customResolutionInstalled = plugin.IsCustomResolutionInstalled();
        var helperColor = customResolutionInstalled
            ? new Vector4(0.2f, 0.8f, 0.2f, 1f)
            : new Vector4(0.85f, 0.7f, 0.2f, 1f);
        ImGui.TextColored(helperColor, customResolutionInstalled
            ? "Custom Resolution detected."
            : "Custom Resolution not detected.");
        ImGui.TextWrapped("Custom Resolution by 0x0ade from the main Dalamud repo is a good companion if you want more display-space flexibility around DPS. It does not replace the DPS render gate.");
        ImGui.TextWrapped("TTSL (Thick Thighs Save Lives) is the planned companion HUD for seeing core state while rendering is disabled. It is still experimental and not available yet.");

        ImGui.Separator();
        var enabled = cfg.PluginEnabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
            plugin.SetPluginEnabled(enabled, "main window", showAllOnDisable: !enabled);

        ImGui.SameLine();
        var dtrEnabled = cfg.DtrBarEnabled;
        if (ImGui.Checkbox("DTR Bar", ref dtrEnabled))
        {
            cfg.DtrBarEnabled = dtrEnabled;
            cfg.Save();
            plugin.UpdateDtrBar();
        }

        ImGui.SameLine();
        if (ImGui.SmallButton("Settings"))
            plugin.ToggleConfigUi();

        ImGui.TextWrapped("Potato experiments: background no-render at the top, plus optional hide toggles below for reducing clutter in busy public areas.");

        ImGui.Separator();
        ImGui.Text("Reduce Problems In Public Areas");
        ImGui.TextDisabled("Optional actor hide toggles for crowds, minions, and pets when you still want the game rendering normally.");

        ImGui.Spacing();
        if (ImGui.BeginTable("DpsToggles", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
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

            ImGui.TableSetColumnIndex(1);
            var hideMinions = cfg.HideNonPartyMinions;
            if (ImGui.Checkbox("Hide non-party minions", ref hideMinions))
            {
                cfg.HideNonPartyMinions = hideMinions;
                cfg.Save();
                plugin.ApplyConfiguration();
            }

            var keepCurrentTargetVisible = cfg.KeepCurrentTargetVisible;
            if (ImGui.Checkbox("Keep current target visible", ref keepCurrentTargetVisible))
            {
                cfg.KeepCurrentTargetVisible = keepCurrentTargetVisible;
                cfg.Save();
                plugin.ApplyConfiguration();
            }
            ImGui.EndTable();
        }

        ImGui.Text($"Players hidden: {plugin.ActorSuppressionService.HiddenPlayers}");
        ImGui.Text($"Pets hidden: {plugin.ActorSuppressionService.HiddenPets}");
        ImGui.Text($"Chocobos hidden: {plugin.ActorSuppressionService.HiddenChocobos}");
        ImGui.Text($"Minions hidden: {plugin.ActorSuppressionService.HiddenMinions}");
        ImGui.TextWrapped($"Suppression status: {plugin.ActorSuppressionService.Status}");

        ImGui.Separator();
        if (plugin.DebugModeEnabled)
        {
            ImGui.Text("Experimental Texture Redirect");
            ImGui.TextDisabled("Direct in-place texture hack. Matching textures keep their handles and get black contents written into the loaded kernel texture.");

            var textureEnabled = cfg.TextureRedirectEnabled;
            if (ImGui.Checkbox("Enable texture redirect", ref textureEnabled))
            {
                cfg.TextureRedirectEnabled = textureEnabled;
                cfg.Save();
                plugin.ApplyConfiguration();
            }

            var scope = cfg.TextureRedirectScope;
            if (ImGui.BeginCombo("Texture scope", TextureRedirectService.ScopeLabel(scope)))
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
            if (ImGui.BeginCombo("Replacement asset", TextureRedirectService.AssetLabel(asset)))
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
            ImGui.TextDisabled("Current in-place hack ignores the size choice and blacks the loaded source texture directly.");

            var logRedirects = cfg.LogTextureRedirects;
            if (ImGui.Checkbox("Log redirected textures", ref logRedirects))
            {
                cfg.LogTextureRedirects = logRedirects;
                cfg.Save();
                plugin.ApplyConfiguration();
            }

            if (ImGui.Button("Chara Preset", new Vector2(130f, 28f)))
            {
                cfg.PluginEnabled = true;
                cfg.TextureRedirectEnabled = true;
                cfg.TextureRedirectScope = TextureRedirectScope.CharaOnly;
                cfg.TextureReplacementAsset = TextureReplacementAsset.Black16x16;
                cfg.HideNonPartyPlayers = false;
                cfg.HideNonPartyPets = false;
                cfg.HideNonPartyChocobos = false;
                cfg.HideNonPartyMinions = false;
                cfg.Save();
                plugin.ApplyConfiguration();
                plugin.UpdateDtrBar();
                Plugin.Log.Information("[DPS] Applied Chara Preset.");
            }

            ImGui.SameLine();
            if (ImGui.Button("World Preset", new Vector2(110f, 28f)))
            {
                cfg.PluginEnabled = true;
                cfg.TextureRedirectEnabled = true;
                cfg.TextureRedirectScope = TextureRedirectScope.WorldOnly;
                cfg.TextureReplacementAsset = TextureReplacementAsset.Black16x16;
                cfg.HideNonPartyPlayers = false;
                cfg.HideNonPartyPets = false;
                cfg.HideNonPartyChocobos = false;
                cfg.HideNonPartyMinions = false;
                cfg.Save();
                plugin.ApplyConfiguration();
                plugin.UpdateDtrBar();
                Plugin.Log.Information("[DPS] Applied World Preset.");
            }

            ImGui.SameLine();
            if (ImGui.Button("VFX Preset", new Vector2(100f, 28f)))
            {
                cfg.PluginEnabled = true;
                cfg.TextureRedirectEnabled = true;
                cfg.TextureRedirectScope = TextureRedirectScope.EffectsOnly;
                cfg.TextureReplacementAsset = TextureReplacementAsset.Black16x16;
                cfg.HideNonPartyPlayers = false;
                cfg.HideNonPartyPets = false;
                cfg.HideNonPartyChocobos = false;
                cfg.HideNonPartyMinions = false;
                cfg.Save();
                plugin.ApplyConfiguration();
                plugin.UpdateDtrBar();
                Plugin.Log.Information("[DPS] Applied VFX Preset.");
            }

            ImGui.SameLine();
            if (ImGui.Button("Global Preset", new Vector2(110f, 28f)))
            {
                cfg.PluginEnabled = true;
                cfg.TextureRedirectEnabled = true;
                cfg.TextureRedirectScope = TextureRedirectScope.EverythingTex;
                cfg.TextureReplacementAsset = TextureReplacementAsset.Black1x1;
                cfg.HideNonPartyPlayers = false;
                cfg.HideNonPartyPets = false;
                cfg.HideNonPartyChocobos = false;
                cfg.HideNonPartyMinions = false;
                cfg.Save();
                plugin.ApplyConfiguration();
                plugin.UpdateDtrBar();
                Plugin.Log.Information("[DPS] Applied Global Preset.");
            }

            ImGui.Text($"Status: {plugin.TextureRedirectService.Status}");
            ImGui.Text($"Redirected textures: {plugin.TextureRedirectService.RedirectedTextures}");
            ImGui.Text($"Successful redirected loads: {plugin.TextureRedirectService.SuccessfulRedirectLoads}");
            ImGui.Text($"Failed redirected loads: {plugin.TextureRedirectService.FailedRedirectLoads}");
            ImGui.TextWrapped($"Last game path: {plugin.TextureRedirectService.LastGameTexturePath}");
            ImGui.TextWrapped($"Replacement path: {plugin.TextureRedirectService.LastReplacementPath}");
            if (ImGui.SmallButton("Clear Texture Stats"))
                plugin.TextureRedirectService.ResetStats();
        }
        else
        {
            ImGui.TextDisabled("Experimental texture controls are hidden. Use /dps debug to expose them for this session.");
        }

        if (ImGui.Button("Show Everything Again", new Vector2(170f, 28f)))
            plugin.SetPluginEnabled(false, "Show Everything Again", showAllOnDisable: true);
    }
}
