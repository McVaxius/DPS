using System.Diagnostics;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;

namespace DPS.Windows;

internal static class UiHelpers
{
    public static readonly Vector4 Good = new(0.25f, 0.82f, 0.38f, 1f);
    public static readonly Vector4 Warn = new(0.95f, 0.68f, 0.25f, 1f);
    public static readonly Vector4 Bad = new(0.92f, 0.28f, 0.28f, 1f);
    public static readonly Vector4 Info = new(0.35f, 0.72f, 0.95f, 1f);
    public static readonly Vector4 Muted = new(0.62f, 0.62f, 0.68f, 1f);

    public static void StatusPill(string label, bool active, string? activeText = null, string? inactiveText = null)
    {
        var value = active ? activeText ?? "ON" : inactiveText ?? "OFF";
        ImGui.TextColored(active ? Good : Muted, $"{label}: {value}");
    }

    public static void StatusPill(string label, string value, Vector4 color)
        => ImGui.TextColored(color, $"{label}: {value}");

    public static void SectionHeader(string text)
    {
        ImGui.Spacing();
        ImGui.TextColored(Info, text);
        ImGui.Separator();
    }

    public static void WarningStrip(string text, bool danger = false)
        => ImGui.TextColored(danger ? Bad : Warn, text);

    public static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(120f, ImGui.GetContentRegionAvail().X));
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }

    public static void AlignedRow(string label, Action drawControl, float labelWidth = 210f)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(labelWidth);
        drawControl();
    }

    public static void SameLineIfFits(float neededWidth = 90f)
    {
        if (ImGui.GetContentRegionAvail().X > neededWidth)
            ImGui.SameLine();
    }

    public static bool CompactButton(string label, float width = 84f)
        => ImGui.Button(label, new Vector2(width, 0f));

    public static void HotkeyStatus(string label, HotkeyBinding binding)
        => StatusPill(label, HotkeyStatusText(binding), binding.Enabled && binding.HasChord ? Good : Muted);

    public static string HotkeyStatusText(HotkeyBinding binding)
    {
        var chord = HotkeyChordLabel(binding);
        if (!binding.HasChord)
            return binding.Enabled ? "Unbound" : "Disabled (unbound)";

        if (binding.Enabled && binding.HasChord)
            return chord;

        return $"Disabled ({chord})";
    }

    public static string HotkeyChordLabel(HotkeyBinding binding)
    {
        if (!binding.HasChord)
            return "Unbound";

        var parts = new List<string>(4);
        if (binding.Ctrl)
            parts.Add("Ctrl");
        if (binding.Alt)
            parts.Add("Alt");
        if (binding.Shift)
            parts.Add("Shift");

        parts.Add(VirtualKeyLabel(binding.KeyCode));
        return string.Join("+", parts);
    }

    public static string VirtualKeyLabel(int keyCode)
    {
        if (keyCode is < 0 or > ushort.MaxValue)
            return $"VK {keyCode}";

        var key = (VirtualKey)keyCode;
        if (!Enum.IsDefined(typeof(VirtualKey), key))
            return $"VK {keyCode}";

        var fancyName = key.GetFancyName();
        return string.IsNullOrWhiteSpace(fancyName) ? key.ToString() : fancyName;
    }

    public static bool ForegroundRenderOffCheckbox(Plugin plugin, string source)
    {
        var foregroundRenderOff = plugin.Configuration.ForegroundNoRenderEnabled;
        if (!ImGui.Checkbox("Foreground render OFF", ref foregroundRenderOff))
            return false;

        if (foregroundRenderOff)
            plugin.ArmForegroundNoRender(source);
        else
            plugin.DisableForegroundNoRender(source);

        return true;
    }

    public static void ForegroundRenderStatus(Plugin plugin, bool includeIntent = true)
    {
        var renderDisabled = plugin.ForegroundRenderControlService.RenderDisabledByDps;
        StatusPill("FG render", !renderDisabled, "ON", "OFF");

        if (!includeIntent)
            return;

        ImGui.SameLine();
        var intentOff = plugin.Configuration.ForegroundNoRenderEnabled;
        StatusPill("FG intent", intentOff ? "OFF" : "ON", intentOff ? Warn : Muted);
    }

    public static void LinkButton(string label, string url)
    {
        if (ImGui.SmallButton(label))
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    public static Vector4 BoolColor(bool value)
        => value ? Good : Muted;
}
