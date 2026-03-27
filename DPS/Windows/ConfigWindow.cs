using System.Diagnostics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

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

        var keepTargetVisible = cfg.KeepCurrentTargetVisible;
        if (ImGui.Checkbox("Keep current target visible", ref keepTargetVisible))
        {
            cfg.KeepCurrentTargetVisible = keepTargetVisible;
            cfg.Save();
        }

        var hidePlayers = cfg.HideNonPartyPlayers;
        if (ImGui.Checkbox("Hide non-party players", ref hidePlayers))
        {
            cfg.HideNonPartyPlayers = hidePlayers;
            cfg.Save();
        }

        var hidePets = cfg.HideNonPartyPets;
        if (ImGui.Checkbox("Hide non-party pets", ref hidePets))
        {
            cfg.HideNonPartyPets = hidePets;
            cfg.Save();
        }

        var hideChocobos = cfg.HideNonPartyChocobos;
        if (ImGui.Checkbox("Hide non-party chocobos", ref hideChocobos))
        {
            cfg.HideNonPartyChocobos = hideChocobos;
            cfg.Save();
        }

        var hideMinions = cfg.HideNonPartyMinions;
        if (ImGui.Checkbox("Hide non-party minions", ref hideMinions))
        {
            cfg.HideNonPartyMinions = hideMinions;
            cfg.Save();
        }
    }
}
