using System.Diagnostics;
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
        ImGui.Text("Experimental Background No-Render");
        ImGui.TextDisabled("Main release candidate feature. Use the big red button in the main window for the fastest live toggle.");

        var backgroundNoRenderEnabled = cfg.BackgroundNoRenderEnabled;
        if (ImGui.Checkbox("Enable background no-render", ref backgroundNoRenderEnabled))
        {
            cfg.BackgroundNoRenderEnabled = backgroundNoRenderEnabled;
            cfg.Save();
            plugin.ApplyConfiguration();
        }

        var onlyWhenMinimized = cfg.BackgroundNoRenderOnlyWhenMinimized;
        if (ImGui.Checkbox("Only trigger while minimized/iconic", ref onlyWhenMinimized))
        {
            cfg.BackgroundNoRenderOnlyWhenMinimized = onlyWhenMinimized;
            cfg.Save();
            plugin.ApplyConfiguration();
        }

        ImGui.TextWrapped($"Render gate status: {plugin.BackgroundRenderGateService.Status}");

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
}
