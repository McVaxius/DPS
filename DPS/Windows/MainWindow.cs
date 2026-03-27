using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

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

        ImGui.TextWrapped("First pass potato mode: hide non-party players and their clutter so the client renders less garbage while you bot.");
        ImGui.TextDisabled("Render-flag suppression is live. No restart required for this first version.");

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

            ImGui.TableSetColumnIndex(1);
            var hideMinions = cfg.HideNonPartyMinions;
            if (ImGui.Checkbox("Hide non-party minions", ref hideMinions))
            {
                cfg.HideNonPartyMinions = hideMinions;
                cfg.Save();
            }

            var keepCurrentTargetVisible = cfg.KeepCurrentTargetVisible;
            if (ImGui.Checkbox("Keep current target visible", ref keepCurrentTargetVisible))
            {
                cfg.KeepCurrentTargetVisible = keepCurrentTargetVisible;
                cfg.Save();
            }
            ImGui.EndTable();
        }

        ImGui.Separator();
        ImGui.Text($"Players hidden: {plugin.ActorSuppressionService.HiddenPlayers}");
        ImGui.Text($"Pets hidden: {plugin.ActorSuppressionService.HiddenPets}");
        ImGui.Text($"Chocobos hidden: {plugin.ActorSuppressionService.HiddenChocobos}");
        ImGui.Text($"Minions hidden: {plugin.ActorSuppressionService.HiddenMinions}");

        if (ImGui.Button("Show Everything Again", new Vector2(170f, 28f)))
        {
            cfg.PluginEnabled = false;
            cfg.Save();
            plugin.ActorSuppressionService.ShowAll();
            plugin.UpdateDtrBar();
        }
    }
}
