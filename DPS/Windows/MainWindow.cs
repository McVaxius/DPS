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

        var enabled = cfg.PluginEnabled;
        if (ImGui.Checkbox("Enabled", ref enabled))
        {
            cfg.PluginEnabled = enabled;
            cfg.Save();
            plugin.ApplyConfiguration();
            plugin.UpdateDtrBar();
        }

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

        ImGui.TextWrapped("Potato experiments: optional actor suppression plus direct in-place texture blacking for character, world, and VFX loads.");
        ImGui.TextDisabled("Texture redirect now rewrites loaded material textures in place with black contents. Actor suppression remains separate if you still want hard hide behavior.");

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

        ImGui.Separator();
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
        {
            cfg.PluginEnabled = false;
            cfg.Save();
            plugin.ApplyConfiguration();
            plugin.ActorSuppressionService.ShowAll();
            plugin.UpdateDtrBar();
        }
    }
}
