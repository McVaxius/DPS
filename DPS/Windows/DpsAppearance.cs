using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

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

    internal DpsAppearance(Plugin plugin) { this.plugin = plugin; Apply(); }

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
        using var colours = MaterialTheme.Push(theme, ImGuiHelpers.GlobalScale, MaterialStyleMode.ColorsOnly);
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
        plugin.WindowSystem.Draw();
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
