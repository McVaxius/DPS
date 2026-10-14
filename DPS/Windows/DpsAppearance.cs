using System.Numerics;
using AethertekUI;
using AethertekUI.Dalamud;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace DPS.Windows;

internal sealed class DpsAppearance : IDisposable
{
    private readonly Plugin plugin;
    private UiText text = null!;
    private DpsFonts fonts = null!;
    private MaterialTheme theme = null!;
    private MaterialOptions<string> languages = null!;
    private string appliedLanguage = string.Empty;
    private uint appliedAccent;
    private Vector3 accentDraft;
    private int checkedFontGeneration = -1;
    private bool fontIssueLogged;
    private readonly ISharedImmediateTexture icon;
    private readonly Dictionary<Window, (MaterialAppearanceTitleBar Actions, MaterialWindowOpacity Opacity, bool Advanced)> titlebars = new();

    internal DpsAppearance(Plugin plugin)
    {
        this.plugin = plugin;
        icon = Plugin.TextureProvider.GetFromManifestResource(typeof(Plugin).Assembly, "DPS.images.icon.png");
        Apply();
    }

    internal void Register(Window window, bool advanced = false)
    {
        var actions = MaterialAppearanceTitleBar.Register(window,
            () => new(UiText.T("Language"), UiText.T("Compact mode"), UiText.T("Transparency"), UiText.T("Color")),
            DrawLanguage, ToggleCompact, DrawTransparency, DrawColour);
        titlebars.Add(window, (actions, new MaterialWindowOpacity(), advanced));
    }

    private void Apply()
    {
        var config = plugin.Configuration;
        var language = UiText.Languages.Any(l => l.Code == config.UiLanguage) ? config.UiLanguage : "en";
        if (language != appliedLanguage)
        {
            fonts?.Dispose(); text?.Dispose();
            text = new(language, role => fonts!.Push(role));
            fonts = new(Plugin.PluginInterface.UiBuilder.FontAtlas, text.GlyphRanges(), language);
            languages = new(UiText.Languages.Select(l => new MaterialOption<string>(l.Code, l.Code, l.Name)).ToArray());
            appliedLanguage = language;
            checkedFontGeneration = -1;
            fontIssueLogged = false;
        }
        var accent = config.UiAccentRgb & 0xFFFFFF;
        if (theme is null || accent != appliedAccent)
        {
            appliedAccent = accent;
            theme = DpsPresentation.Theme(accent);
            var rgb = DpsPresentation.Rgb(accent);
            accentDraft = new(rgb.X, rgb.Y, rgb.Z);
        }
        theme.Density = config.UiCompact ? MaterialDensity.Compact : MaterialDensity.Standard;
    }

    internal void Draw()
    {
        Apply();
        if (!plugin.WindowSystem.Windows.Any(window => window.IsOpen)) return;
        using var language = text.Enter();
        using var colours = MaterialTheme.Push(theme, ImGuiHelpers.GlobalScale, MaterialStyleMode.ColorsOnly);
        if (!fonts.Ready)
        {
            if (!fontIssueLogged && fonts.LoadException is { } error)
            { Plugin.Log.Error(error, "[DPS] Required UI fonts failed to load."); fontIssueLogged = true; }
            DrawFontStatus(fonts.LoadException is null);
            return;
        }
        if (checkedFontGeneration != fonts.Generation)
        {
            try
            {
                var generation = fonts.Generation;
                fonts.CheckGlyphs(text.RequiredText);
                checkedFontGeneration = generation;
            }
            catch (Exception error)
            {
                if (!fontIssueLogged) { Plugin.Log.Error(error, "[DPS] Required UI glyph coverage failed."); fontIssueLogged = true; }
                DrawFontStatus(false); return;
            }
        }
        if (!fonts.Ready || checkedFontGeneration != fonts.Generation)
        {
            DrawFontStatus(fonts.LoadException is null);
            return;
        }
        using var geometry = new MaterialStyleScope();
        var compact = plugin.Configuration.UiCompact;
        var scale = ImGuiHelpers.GlobalScale;
        geometry.Style(ImGuiStyleVar.WindowPadding, new Vector2(compact ? 10 : 14) * scale);
        geometry.Style(ImGuiStyleVar.FramePadding, new Vector2(compact ? 6 : 8, compact ? 3 : 5) * scale);
        geometry.Style(ImGuiStyleVar.ItemSpacing, new Vector2(compact ? 6 : 10, compact ? 4 : 6) * scale);
        geometry.Style(ImGuiStyleVar.CellPadding, new Vector2(compact ? 6 : 10, compact ? 4 : 6) * scale);
        geometry.Style(ImGuiStyleVar.FrameRounding, 4 * scale);
        geometry.Style(ImGuiStyleVar.ChildRounding, 4 * scale);
        geometry.Style(ImGuiStyleVar.FrameBorderSize, scale);
        using var body = fonts.Push(UiFontRole.Body);
        foreach (var chrome in titlebars.Values) chrome.Actions.RefreshVisibility();
        plugin.WindowSystem.Draw();
        var status = plugin.Configuration.UiShowTitlebarStatus ? TitlebarStatus() : (MaterialTitlebarStatus?)null;
        var texture = icon.GetWrapOrDefault();
        foreach (var (window, chrome) in titlebars)
        {
            if (!window.IsOpen) continue;
            chrome.Actions.DrawPopups();
            var native = ImGuiP.FindWindowByName(window.WindowName);
            if (native.IsNull || native.LastFrameActive != ImGui.GetFrameCount()) continue;
            var count = window.TitleBarButtons.Count(button => !window.IsClickthrough || button.AvailableClickthrough);
            if (window.AllowPinning || window.AllowClickthrough || window.AllowBackgroundBlur) count++;
            var reserved = count * (ImGuiP.CalcFontSize(native) + ImGui.GetStyle().ItemInnerSpacing.X);
            var title = UiText.T(chrome.Advanced ? $"{PluginInfo.DisplayName} Advanced" : PluginInfo.DisplayName)
                + " v" + typeof(Plugin).Assembly.GetName().Version;
            var size = texture is null ? Vector2.Zero : new Vector2(texture.Width, texture.Height);
            if (status is { } summary)
                MaterialWindowHeader.PaintTitleWithStatus(native, title, texture?.Handle ?? default, size, summary, reserved, window.ShowCloseButton);
            else
                MaterialWindowHeader.PaintTitle(native, title, texture?.Handle ?? default, size, reserved, window.ShowCloseButton);
            chrome.Opacity.Apply(window.WindowName, Opacity, autoFade: false);
        }
    }

    private float Opacity => float.IsFinite(plugin.Configuration.UiOpacity)
        ? Math.Clamp(plugin.Configuration.UiOpacity, .1f, 1f) : 1f;

    private MaterialTitlebarStatus TitlebarStatus()
    {
        var config = plugin.Configuration;
        var enabled = UiText.T(config.PluginEnabled ? "Enabled" : "Disabled");
        var hidden = plugin.ActorSuppressionService.HiddenPlayers + plugin.ActorSuppressionService.HiddenPets
            + plugin.ActorSuppressionService.HiddenChocobos + plugin.ActorSuppressionService.HiddenMinions;
        var detail = UiText.F("FG: {0} | BG: {1} | Crowd: {2}",
            plugin.ForegroundRenderControlService.RenderDisabledByDps ? "Rendering off" : "Rendering on",
            plugin.BackgroundRenderGateService.IsBackgroundNoRenderActive ? "Rendering off" : "Rendering on",
            UiText.F("{0} hidden", hidden));
        var tooltip = string.Join("\n", enabled, detail,
            UiText.T(plugin.ForegroundRenderControlService.Status),
            UiText.T(plugin.BackgroundRenderGateService.Status),
            UiText.T(plugin.ActorSuppressionService.Status),
            UiText.F("RenderTrim requested: {0}", plugin.RenderTrimService.MasterRequested ? "Enabled" : "Disabled"));
        return new(config.UiDetailedTitlebarStatus ? enabled + " | " + detail : enabled,
            enabled, tooltip, config.PluginEnabled ? theme.Colors.Primary : theme.Colors.OnSurfaceVariant);
    }

    private void ToggleCompact()
    {
        plugin.Configuration.UiCompact = !plugin.Configuration.UiCompact;
        plugin.Configuration.Save();
    }

    private MaterialAppearanceLabels SelectorLabels => new(UiText.T("Color"), UiText.T("Language"),
        UiText.T("Teal"), UiText.T("Blue"), UiText.T("Pink"), UiText.T("Custom RGB"));

    private void DrawLanguage()
    {
        var selected = appliedLanguage;
        using var font = UiText.Font(UiFontRole.Action);
        if (MaterialAppearanceSelector.DrawLanguage("appearance", ref selected, languages, 160))
        {
            plugin.Configuration.UiLanguage = selected;
            plugin.Configuration.Save();
        }
    }

    private void DrawColour()
    {
        using var font = UiText.Font(UiFontRole.Action);
        if (!MaterialAppearanceSelector.DrawPalette("appearance", ref accentDraft, SelectorLabels)) return;
        SaveAccent();
    }

    private void SaveAccent()
    {
        plugin.Configuration.UiAccentRgb = ((uint)Math.Clamp((int)MathF.Round(accentDraft.X * 255), 0, 255) << 16)
            | ((uint)Math.Clamp((int)MathF.Round(accentDraft.Y * 255), 0, 255) << 8)
            | (uint)Math.Clamp((int)MathF.Round(accentDraft.Z * 255), 0, 255);
        plugin.Configuration.Save();
    }

    private void DrawTransparency()
    {
        var value = Opacity * 100;
        ImGui.SetNextItemWidth(180 * MaterialTheme.Metrics.Scale);
        if (ImGui.SliderFloat(UiText.T("Window opacity") + "##DpsOpacity", ref value, 10, 100, "%.0f%%"))
        {
            plugin.Configuration.UiOpacity = value / 100;
            plugin.Configuration.Save();
        }
    }

    internal void DrawSettings()
    {
        using var font = UiText.Font(UiFontRole.Action);
        using var controls = MaterialControls.Push(MaterialControlContext.Toolbar);
        var width = new[] { "Theme and language", "Titlebar", "Show titlebar status", "Detailed titlebar status", "Window opacity" }
            .Max(label => ImGui.CalcTextSize(UiText.T(label)).X) + ImGui.GetFrameHeight() + 24 * MaterialTheme.Metrics.Scale;
        MaterialSettings.Columns("DpsAppearanceGroups", Math.Max(280 * MaterialTheme.Metrics.Scale, width), () =>
        {
            MaterialSettings.Section(UiText.T("Theme and language"));
            DrawColour();
            DrawLanguage();
            var compact = plugin.Configuration.UiCompact;
            if (UiGui.Checkbox("C##CompactMode", ref compact)) ToggleCompact();
            UiHelpers.Tooltip("Compact mode");
        }, () =>
        {
            MaterialSettings.Section(UiText.T("Titlebar"));
            var show = plugin.Configuration.UiShowTitlebarStatus;
            if (UiGui.Checkbox("Show titlebar status", ref show))
            { plugin.Configuration.UiShowTitlebarStatus = show; plugin.Configuration.Save(); }
            ImGui.BeginDisabled(!show);
            var detailed = plugin.Configuration.UiDetailedTitlebarStatus;
            if (UiGui.Checkbox("Detailed titlebar status", ref detailed))
            { plugin.Configuration.UiDetailedTitlebarStatus = detailed; plugin.Configuration.Save(); }
            ImGui.EndDisabled();
            MaterialSettings.Section(UiText.T("Transparency"));
            DrawTransparency();
        });
    }

    private static void DrawFontStatus(bool loading)
    {
        ImGui.SetNextWindowSize(new Vector2(460 * ImGuiHelpers.GlobalScale, 0));
        if (ImGui.Begin("DPS##FontStatus", ImGuiWindowFlags.AlwaysAutoResize))
            ImGui.TextWrapped(UiText.T(loading ? "Loading UI fonts..." : "UI fonts failed to load. See the plugin log."));
        ImGui.End();
    }

    internal void DrawSelector()
    {
        var selected = appliedLanguage;
        using var font = UiText.Font(UiFontRole.Action);
        using var controls = MaterialControls.Push(DpsPresentation.Controls(DpsPresentation.ControlHeight));
        var changed = MaterialAppearanceSelector.Draw("appearance", ref accentDraft, ref selected, languages,
            new(UiText.T("Color"), UiText.T("Language"), UiText.T("Teal"), UiText.T("Blue"), UiText.T("Pink"), UiText.T("Custom RGB")), languageWidth: 130);
        var config = plugin.Configuration;
        if (changed.AccentChanged)
            config.UiAccentRgb = ((uint)Math.Clamp((int)MathF.Round(accentDraft.X * 255), 0, 255) << 16)
                | ((uint)Math.Clamp((int)MathF.Round(accentDraft.Y * 255), 0, 255) << 8)
                | (uint)Math.Clamp((int)MathF.Round(accentDraft.Z * 255), 0, 255);
        if (changed.LanguageChanged) config.UiLanguage = selected;
        if (changed.AccentChanged || changed.LanguageChanged) config.Save();
    }

    public void Dispose() { fonts?.Dispose(); text?.Dispose(); }
}
