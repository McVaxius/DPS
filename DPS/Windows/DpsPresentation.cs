using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace DPS.Windows;

internal enum UiFontRole { Body, BodyStrong, Title, PluginName, Counter, Action, CompactTitle }

internal static class DpsPresentation
{
    // Approved DPS-review-v2 and DPS-compact-review-v1, measured in logical pixels.
    internal const uint ReferenceAccent = 0x00BCD4;
    internal static readonly float[] FontSizes = [16, 16, 32, 22, 22, 18, 28];
    internal static readonly string[] FontFiles = ["segoeui.ttf", "seguisb.ttf", "segoeuib.ttf", "seguisb.ttf", "seguisb.ttf", "seguisb.ttf", "segoeuib.ttf"];
    internal static float AtlasHeight(UiFontRole role) => FontSizes[(int)role] * 4 / 3;
    internal static bool Compact => MaterialTheme.Current.Density == MaterialDensity.Compact;
    internal static float HeaderHeight => Compact ? 84 : 108;
    internal static float Gap => Compact ? 10 : 14;
    internal static float ControlHeight => Compact ? 36 : 44;
    internal static Vector4 Rgb(uint rgb) => new(((rgb >> 16) & 255) / 255f, ((rgb >> 8) & 255) / 255f, (rgb & 255) / 255f, 1);

    internal static MaterialTheme Theme(uint accent)
    {
        accent &= 0xFFFFFF;
        var selected = Rgb(accent);
        var reference = Rgb(ReferenceAccent);
        var seed = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(selected.X, selected.Y, selected.Z)));
        var original = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(reference.X, reference.Y, reference.Z)));
        var hue = seed.Y < .001f ? 0 : seed.Z - original.Z;
        var chroma = seed.Y < .001f ? 0 : seed.Y / original.Y;
        Vector4 Relative(uint rgb)
        {
            var color = Rgb(rgb);
            if (accent == ReferenceAccent) return color;
            var lch = MaterialColor.LabToLch(MaterialColor.SrgbToOklab(new(color.X, color.Y, color.Z)));
            return new(MaterialColor.GamutMap(lch.X, lch.Y * chroma, lch.Z + hue), 1);
        }
        var background = Relative(0x091B28);
        var foreground = Relative(0xEEF0FA);
        var primary = Relative(ReferenceAccent);
        var colors = new MaterialColorScheme(new OklchPaletteGenerator().Generate(new(selected.X, selected.Y, selected.Z)))
        {
            Background = background, OnBackground = foreground,
            Surface = Relative(0x0F2536), OnSurface = foreground,
            SurfaceContainerLowest = Relative(0x0A1D2B), SurfaceContainerLow = Relative(0x0C2030),
            SurfaceContainer = Relative(0x112C40), SurfaceContainerHigh = Relative(0x122B3F), SurfaceContainerHighest = Relative(0x14344A),
            SurfaceVariant = Relative(0x18394E), OnSurfaceVariant = Relative(0xB1D0E8),
            Outline = Relative(0x416981), OutlineVariant = Relative(0x2B536C),
            Primary = primary, OnPrimary = MaterialColor.Contrast(primary, background) >= MaterialColor.Contrast(primary, foreground) ? background : foreground,
            PrimaryContainer = Relative(0x005B70), OnPrimaryContainer = foreground,
            Secondary = Relative(0xB1D0E8), OnSecondary = background, SecondaryContainer = Relative(0x163A52), OnSecondaryContainer = foreground,
            Tertiary = Relative(0xBEDCE9), OnTertiary = background, TertiaryContainer = Relative(0x173B4A), OnTertiaryContainer = foreground,
            InverseSurface = foreground, InverseOnSurface = background, InversePrimary = Relative(0x006273),
        };
        return new(colors, MaterialDensity.Standard) { SurfaceOpacity = 1 };
    }

    internal static MaterialControlMetrics Controls(float height = 28)
    {
        var s = MaterialTheme.Metrics.Scale;
        return new() { Height = height * s, Padding = new(10 * s, Math.Max(0, (height * s - ImGui.GetTextLineHeight()) * .5f)),
            Gap = 8 * s, IconSize = 20 * s, Rounding = 4 * s, ItemSpacing = new(8 * s, 6 * s), CellPadding = new(12 * s, 6 * s) };
    }

    internal static void Panel(Action draw, uint originalIdRoot, float minimumHeight = 0, Vector2? inset = null)
    {
        var s = MaterialTheme.Metrics.Scale;
        var start = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var padding = (inset ?? new Vector2(Compact ? 14 : 20, Compact ? 12 : 16)) * s;
        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        try
        {
            dl.ChannelsSetCurrent(1);
            ImGui.SetCursorScreenPos(start + padding);
            ImGui.BeginGroup();
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Math.Max(1, width - padding.X * 2));
            ImGui.PushItemWidth(Math.Max(1, width - padding.X * 2));
            // A presentation table/card must not change IDs of existing controls.
            ImGuiP.PushOverrideID(originalIdRoot);
            try { draw(); }
            finally
            {
                ImGui.PopID();
                ImGui.PopItemWidth();
                ImGui.PopTextWrapPos();
                ImGui.EndGroup();
            }
            var bottom = Math.Max(start.Y + minimumHeight * s, ImGui.GetItemRectMax().Y + padding.Y);
            var measuredWidth = Math.Max(width,ImGui.GetItemRectMax().X-start.X+padding.X);
            dl.ChannelsSetCurrent(0);
            var c = MaterialTheme.Current.Colors;
            MaterialCanvas.Surface(start, new(start.X + measuredWidth, bottom), c.SurfaceContainerHigh, c.Surface, 5 * s);
            dl.AddRect(start, new(start.X + measuredWidth, bottom), MaterialCanvas.Color(c.OutlineVariant), 5 * s);
            ImGui.SetCursorScreenPos(new(start.X, bottom));
            ImGui.Dummy(new(measuredWidth, Gap * s));
        }
        finally
        {
            dl.ChannelsMerge();
        }
    }

    internal static void Brand(Vector2 origin, float size)
    {
        var dl = ImGui.GetWindowDrawList();
        // Consumer-owned potato artwork keeps its natural colours under accent changes.
        dl.PathLineTo(origin + new Vector2(.38f, .12f) * size);
        dl.PathBezierCubicCurveTo(origin + new Vector2(.76f, -.05f) * size, origin + new Vector2(1.12f, .57f) * size, origin + new Vector2(.77f, .90f) * size, 20);
        dl.PathBezierCubicCurveTo(origin + new Vector2(.36f, 1.13f) * size, origin + new Vector2(-.08f, .49f) * size, origin + new Vector2(.38f, .12f) * size, 20);
        dl.PathFillConvex(MaterialCanvas.Color(Rgb(0xA76E35)));
        dl.AddCircleFilled(origin + new Vector2(.47f, .43f) * size, size * .26f, MaterialCanvas.Color(Rgb(0xD69B55)), 32);
        dl.AddCircleFilled(origin + new Vector2(.67f, .76f) * size, size * .06f, MaterialCanvas.Color(Rgb(0x815327)), 12);
        dl.AddCircleFilled(origin + new Vector2(.29f, .60f) * size, size * .04f, MaterialCanvas.Color(Rgb(0x815327)), 12);
        var powerInk = MaterialCanvas.Color(MaterialTheme.Current.Colors.Primary);
        var powerCenter = origin + new Vector2(.48f, .43f) * size;
        dl.PathArcTo(powerCenter, size * .18f, -MathF.PI * .5f + .45f, MathF.PI * 1.5f - .45f, 28);
        dl.PathStroke(powerInk, ImDrawFlags.None, Math.Max(1, size * .04f));
        dl.AddLine(origin + new Vector2(.48f, .18f) * size, origin + new Vector2(.48f, .39f) * size, powerInk, Math.Max(1, size * .04f));
        var rays = new[] { (.04f, .18f, -.03f, .08f), (.16f, .03f, .10f, -.09f), (.65f, .06f, .69f, -.06f), (.79f, .13f, .88f, .04f) };
        foreach (var (x, y, endX, endY) in rays)
            dl.AddLine(origin + new Vector2(x, y) * size, origin + new Vector2(endX, endY) * size, MaterialCanvas.Color(MaterialTheme.Current.Colors.Primary), 3);
    }
}
