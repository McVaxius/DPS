using System.Diagnostics;
using System.Numerics;
using AethertekUI;
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

    public static void StatusPill(string label, bool active, string? activeText = null, string? inactiveText = null, bool filled = true)
    {
        var value = active ? activeText ?? "ON" : inactiveText ?? "OFF";
        StatusPill(label,value,active ? Good : Muted,filled);
    }

    public static void StatusPill(string label, string value, Vector4 color, bool filled = true)
        => StatusCell(UiText.F("{0}: {1}",UiText.T(label),UiText.T(value)),color,filled);

    public static void StatusCell(string text, Vector4 color, bool filled = true)
    {
        var s=MaterialTheme.Metrics.Scale;var compact=DpsPresentation.Compact;
        var translated=UiText.T(text);var textSize=ImGui.CalcTextSize(translated);
        var dot=(compact?7:8)*s;var gap=8*s;var padding=(filled?12:0)*s;
        var natural=textSize.X+2*padding+2*dot+gap;
        var width=MaterialLayout.FitNextItemWidth(filled?-1:0,filled?2*padding+2*dot+gap+ImGui.GetFontSize():natural);
        var wrap=filled?Math.Max(ImGui.GetFontSize(),width-2*padding-2*dot-gap):0;
        if(filled)textSize=ImGui.CalcTextSize(translated,false,wrap);
        var height=Math.Max((filled?(compact?34:44):(compact?22:24))*s,textSize.Y+(filled?12*s:0));
        var min=ImGui.GetCursorScreenPos();var max=min+new Vector2(width,height);
        var dl=ImGui.GetWindowDrawList();var c=MaterialTheme.Current.Colors;
        if(filled)
        {
            dl.AddRectFilled(min,max,MaterialCanvas.Color(c.SurfaceContainer),4*s);
            dl.AddRect(min,max,MaterialCanvas.Color(c.OutlineVariant),4*s);
        }
        dl.PushClipRect(min,max,true);
        dl.AddCircleFilled(min+new Vector2(padding+dot,height*.5f),dot,MaterialCanvas.Color(color),20);
        dl.AddText(ImGui.GetFont(),ImGui.GetFontSize(),min+new Vector2(padding+2*dot+gap,(height-textSize.Y)*.5f),
            MaterialCanvas.Color(c.OnSurface),translated,wrap);
        dl.PopClipRect();ImGui.Dummy(new Vector2(width,height));
    }

    public static void StatusHeading()
    {
        ImGui.Spacing();ImGui.Separator();ImGui.Spacing();
        using var font=UiText.Font(UiFontRole.BodyStrong);
        UiGui.TextUnformatted("Status");
    }

    public static void SectionHeader(string text)
    {
        ImGui.Spacing();
        using var font = UiText.Font(UiFontRole.PluginName);
        UiGui.TextColored(MaterialTheme.Current.Colors.OnSurface, text);
        ImGui.Separator();
    }

    public static void WarningStrip(string text, bool danger = false)
        => UiGui.TextColored(danger ? Bad : Warn, text);

    public static void Wrapped(string text)
    {
        var right = ImGui.GetCursorPosX() + Math.Max(1f, ImGui.GetContentRegionAvail().X);
        var inheritedRight = ImGuiP.GetCurrentWindow().DC.TextWrapPos;
        if (inheritedRight > 0) right = Math.Min(right, inheritedRight);
        ImGui.PushTextWrapPos(right);
        UiGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }


    public static void AlignedRow(string label, Action drawControl, float labelWidth = 210f)
    {
        ImGui.AlignTextToFramePadding();
        UiGui.TextUnformatted(label);
        ImGui.SameLine(Math.Max(labelWidth * MaterialTheme.Metrics.Scale, ImGui.CalcTextSize(UiText.T(label)).X + ImGui.GetStyle().ItemInnerSpacing.X));
        drawControl();
    }

    public static void SameLineIfFits(float neededWidth = 90f)
    {
        var right = ImGui.GetCursorScreenPos().X + ImGui.GetContentRegionAvail().X;
        if (ImGui.GetItemRectMax().X + ImGui.GetStyle().ItemSpacing.X + neededWidth * MaterialTheme.Metrics.Scale <= right)
            ImGui.SameLine();
    }

    public static bool CompactButton(string label, float width = 84f, string? tooltip = null)
    {
        using var font = UiText.Font(UiFontRole.Action);
        var scale = MaterialTheme.Metrics.Scale;
        var translated = UiText.T(label.Split("##", 2)[0]);
        var textSize = ImGui.CalcTextSize(translated);
        width = Math.Max(width * scale, textSize.X + 44 * scale);
        if (ImGui.GetContentRegionAvail().X < width && ImGui.GetCursorScreenPos().Y < ImGui.GetItemRectMax().Y)
            ImGui.NewLine();
        var clicked = UiGui.Button(label, new Vector2(width, DpsPresentation.ControlHeight * scale), "");
        var icon = label.Split("##", 2)[0] switch
        {
            "Stop" => MaterialIcon.Stop,
            "Start" or "DTR On" or "DTR Off" => MaterialIcon.Play,
            "All Off" => MaterialIcon.Close,
            "Restore FG" or "Restore BG" => MaterialIcon.Return,
            "Show All" or "Show Everything Again" => MaterialIcon.Person,
            "Hotkeys" => MaterialIcon.Table,
            "Advanced" => MaterialIcon.Settings,
            _ => MaterialIcon.None,
        };
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var iconWidth = icon == MaterialIcon.None ? 0 : 28 * scale;
        var position = min + new Vector2((width - textSize.X - iconWidth) * .5f, (max.Y - min.Y - textSize.Y) * .5f);
        var drawing = ImGui.GetWindowDrawList();
        drawing.PushClipRect(min, max, true);
        if (icon != MaterialIcon.None)
            MaterialIcons.Draw(icon, position + new Vector2(0, (textSize.Y - 20 * scale) * .5f), 20 * scale,
                MaterialTheme.Current.Colors.OnSurface, ImGui.GetStyle().Alpha);
        drawing.AddText(position + new Vector2(iconWidth, 0), ImGui.GetColorU32(ImGuiCol.Text), translated);
        drawing.PopClipRect();
        Tooltip(tooltip);
        return clicked;
    }

    public static bool SmallButton(string label, string? tooltip = null)
    {
        var clicked = UiGui.SmallButton(label);
        Tooltip(tooltip);
        return clicked;
    }

    public static void Tooltip(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || !ImGui.IsItemHovered())
            return;

        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
        UiGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    public static void HotkeyStatus(string label, HotkeyBinding binding)
        => StatusPill(label, HotkeyStatusText(binding), binding.Enabled && binding.HasChord ? Good : Muted,filled:false);

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
        if (!UiGui.Checkbox("Foreground render OFF", ref foregroundRenderOff, "Enable foreground no-render"))
            return false;

        if (foregroundRenderOff)
            plugin.ArmForegroundNoRender(source);
        else
            plugin.DisableForegroundNoRender(source);

        return true;
    }

    public static void ForegroundRenderStatus(Plugin plugin, bool includeIntent = true, bool filled = true)
    {
        var renderDisabled = plugin.ForegroundRenderControlService.RenderDisabledByDps;
        StatusPill("FG render", !renderDisabled, "ON", "OFF",filled);

        if (!includeIntent)
            return;

        ImGui.SameLine();
        var intentOff = plugin.Configuration.ForegroundNoRenderEnabled;
        StatusPill("FG intent", intentOff ? "OFF" : "ON", intentOff ? Warn : Muted,filled);
    }

    public static void LinkButton(string label, string url, string? tooltip = null)
    {
        if (SmallButton(label, tooltip))
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    public static Vector4 BoolColor(bool value)
        => value ? Good : Muted;
}
