using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Numerics;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace DPS.Windows;

// Keep the ORIGINAL label passed to native ImGui. Translated ink is painted into the native item's measured bounds.
// This retains English-derived IDs, popup identity, selection, editing, focus and navigation behavior.
internal static class UiGui
{
    internal static void Text(string text) => ImGui.TextUnformatted(UiText.T(text));
    internal static void Text(FormattableString text) => ImGui.TextUnformatted(UiText.F(text));
    internal static void TextUnformatted(string text) => ImGui.TextUnformatted(UiText.T(text));
    internal static void TextWrapped(string text) => ImGui.TextWrapped(UiText.T(text));
    internal static void TextDisabled(string text) { ImGui.PushTextWrapPos(0);ImGui.TextDisabled(UiText.T(text));ImGui.PopTextWrapPos(); }
    internal static void TextColored(Vector4 color,string text) { ImGui.PushTextWrapPos(0);ImGui.TextColored(color,UiText.T(text));ImGui.PopTextWrapPos(); }
    internal static void BulletText(string text) => ImGui.BulletText(UiText.T(text));
    internal static void SetTooltip(string text) { ImGui.BeginTooltip();ImGui.PushTextWrapPos(Math.Min(560*MaterialTheme.Metrics.Scale,ImGui.GetMainViewport().WorkSize.X*.8f));ImGui.TextUnformatted(UiText.T(text));ImGui.PopTextWrapPos();ImGui.EndTooltip(); }
    private static string Visible(string label) => UiText.T(label.Split("##",2)[0]);
    private static void Ink(string label,Vector2 position,Vector2 min,Vector2 max)
    {
        var dl=ImGui.GetWindowDrawList();dl.PushClipRect(min,max,true);
        dl.AddText(position,ImGui.GetColorU32(ImGuiCol.Text),label);dl.PopClipRect();
    }
    internal static bool Button(string original,Vector2 size=default,string? display=null)
    {
        var translated=display ?? Visible(original);
        var natural=ImGui.CalcTextSize(translated).X+ImGui.GetStyle().FramePadding.X*2;
        var minimum=size.X>0?Math.Max(size.X,natural):natural;
        size.X=MaterialLayout.FitNextItemWidth(size.X,minimum);
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var clicked=ImGui.Button(original,size);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
        Ink(translated,min+(max-min-ImGui.CalcTextSize(translated))*.5f,min,max);
        if (ImGui.CalcTextSize(translated).X>max.X-min.X-ImGui.GetStyle().FramePadding.X*2 && ImGui.IsItemHovered()) ImGui.SetTooltip(translated);
        return clicked;
    }
    internal static bool SmallButton(string label)
    { ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(ImGui.GetStyle().FramePadding.X,0));var click=Button(label);ImGui.PopStyleVar();return click; }
    internal static bool Checkbox(string label,ref bool value,string? display=null)
    {
        var translated=UiText.T(display ?? label.Split("##",2)[0]);var padding=ImGui.GetStyle().FramePadding;var gap=ImGui.GetStyle().ItemInnerSpacing;
        var original=label.Split("##",2)[0];
        var textSize=ImGui.CalcTextSize(translated);
        MaterialLayout.FitNextItemWidth(0,ImGui.GetFrameHeight()+(textSize.X>0?gap.X+textSize.X:0));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(gap.X+textSize.X-ImGui.CalcTextSize(original).X,gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var changed=ImGui.Checkbox(label,ref value);ImGui.PopStyleColor();ImGui.PopStyleVar();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
        var p=min+new Vector2(ImGui.GetFrameHeight()+gap.X,padding.Y);
        Ink(translated,p,min,max);
        return changed;
    }
    internal static bool RadioButton(string label,bool selected,string? display=null)
    {
        var translated=UiText.T(display ?? label.Split("##",2)[0]);var original=label.Split("##",2)[0];var gap=ImGui.GetStyle().ItemInnerSpacing;
        var textSize=ImGui.CalcTextSize(translated);
        MaterialLayout.FitNextItemWidth(0,ImGui.GetFrameHeight()+(textSize.X>0?gap.X+textSize.X:0));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(gap.X+textSize.X-ImGui.CalcTextSize(original).X,gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var changed=ImGui.RadioButton(label,selected);ImGui.PopStyleColor();ImGui.PopStyleVar();
        var min=ImGui.GetItemRectMin();var p=min+new Vector2(ImGui.GetFrameHeight()+gap.X,ImGui.GetStyle().FramePadding.Y);
        Ink(translated,p,min,new Vector2(p.X+ImGui.CalcTextSize(translated).X,ImGui.GetItemRectMax().Y));return changed;
    }
    internal static bool RadioTile(string label,bool selected,float width=0,string? display=null)
    {
        var scale=MaterialTheme.Metrics.Scale;
        var text=UiText.T(display ?? label.Split("##",2)[0]);
        var height=DpsPresentation.ControlHeight*scale;
        width=Math.Max(width,ImGui.CalcTextSize(text).X+height+18*scale);
        MaterialLayout.FitNextItemWidth(0,width);
        var min=ImGui.GetCursorScreenPos();
        var colors=MaterialTheme.Current.Colors;
        var dl=ImGui.GetWindowDrawList();
        dl.AddRectFilled(min,min+new Vector2(width,height),MaterialCanvas.Color(selected?colors.PrimaryContainer:colors.SurfaceContainer),4*scale);
        dl.AddRect(min,min+new Vector2(width,height),MaterialCanvas.Color(selected?colors.Primary:colors.OutlineVariant),4*scale);
        var raw=label.Split("##",2)[0];
        var gap=ImGui.GetStyle().ItemInnerSpacing;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(6*scale,Math.Max(0,(height-ImGui.GetTextLineHeight())*.5f)));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing,new Vector2(width-height-ImGui.CalcTextSize(raw).X,gap.Y));
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);
        var changed=ImGui.RadioButton(label,selected);
        ImGui.PopStyleColor();ImGui.PopStyleVar(2);
        Ink(text,min+new Vector2(height,(height-ImGui.GetTextLineHeight())*.5f),min,min+new Vector2(width,height));
        return changed;
    }
    internal static bool CollapsingHeader(string label,ImGuiTreeNodeFlags flags=ImGuiTreeNodeFlags.None)
    {
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var open=ImGui.CollapsingHeader(label,flags);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();var padding=ImGui.GetStyle().FramePadding;
        MaterialIcons.Draw(open?MaterialIcon.ChevronDown:MaterialIcon.ArrowRight,min+padding,ImGui.GetFontSize(),MaterialTheme.Current.Colors.OnSurface,ImGui.GetStyle().Alpha);
        Ink(Visible(label),min+new Vector2(ImGui.GetFontSize()+padding.X*2,padding.Y),min,max);return open;
    }
    internal static bool TreeNode(string label)
    {
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var open=ImGui.TreeNode(label);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();
        MaterialIcons.Draw(open?MaterialIcon.ChevronDown:MaterialIcon.ArrowRight,min,ImGui.GetFontSize(),MaterialTheme.Current.Colors.OnSurface,ImGui.GetStyle().Alpha);
        var p=min+new Vector2(ImGui.GetFontSize()+ImGui.GetStyle().FramePadding.X*2,0);
        Ink(Visible(label),p,min,new Vector2(p.X+ImGui.CalcTextSize(Visible(label)).X,max.Y));return open;
    }
    internal static bool BeginTabItem(string label,ImGuiTabItemFlags flags=ImGuiTabItemFlags.None)
    {
        var translated=Visible(label);
        ImGui.SetNextItemWidth(ImGui.CalcTextSize(translated).X+ImGui.GetStyle().FramePadding.X*2+12*MaterialTheme.Metrics.Scale);
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var open=ImGui.BeginTabItem(label,flags);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();Ink(translated,min+(max-min-ImGui.CalcTextSize(translated))*.5f,min,max);return open;
    }
    internal static bool BeginPrimaryTabItem(string label,ImGuiTabItemFlags flags,MaterialIcon icon,float width)
    {
        var scale=MaterialTheme.Metrics.Scale;using var font=UiText.Font(UiFontRole.BodyStrong);
        var height=(DpsPresentation.Compact?40:44)*scale;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,new Vector2(12*scale,Math.Max(0,(height-ImGui.GetTextLineHeight())*.5f)));
        ImGui.SetNextItemWidth(width);
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var open=ImGui.BeginTabItem(label,flags);ImGui.PopStyleColor();ImGui.PopStyleVar();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();var text=Visible(label);var textSize=ImGui.CalcTextSize(text);
        var p=min+new Vector2(Math.Max(4*scale,(max.X-min.X-textSize.X-30*scale)*.5f),(max.Y-min.Y-24*scale)*.5f);
        var color=MaterialTheme.Current.Colors.OnSurface;
        var dl=ImGui.GetWindowDrawList();dl.PushClipRect(min,max,true);MaterialIcons.Draw(icon,p,24*scale,color);
        dl.AddText(p+new Vector2(32*scale,(24*scale-textSize.Y)*.5f),ImGui.GetColorU32(ImGuiCol.Text),text);dl.PopClipRect();
        if (textSize.X+30*scale>max.X-min.X && ImGui.IsItemHovered()) ImGui.SetTooltip(text);
        return open;
    }

    internal static bool BeginCombo(string label,string preview,ImGuiComboFlags flags=ImGuiComboFlags.None,bool translatePreview=true)
    {
        var width=FitField(label);var min=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();var shown=translatePreview?UiText.T(preview):preview;
        var clipped=ClipFieldLabel(label,min,width,dl);
        var open=ImGui.BeginCombo(label,shown,flags);if(clipped)dl.PopClipRect();FieldLabel(label,min,width,dl,parent,previousMax);
        if(!open && ImGui.CalcTextSize(shown).X>width-ImGui.GetFrameHeight() && ImGui.IsItemHovered()) ImGui.SetTooltip(shown);
        return open;
    }
    internal static bool Selectable(string label,bool selected=false,ImGuiSelectableFlags flags=ImGuiSelectableFlags.None,Vector2 size=default)
    {
        var origin=ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);var changed=ImGui.Selectable(label,selected,flags,size);ImGui.PopStyleColor();
        var min=ImGui.GetItemRectMin();var max=ImGui.GetItemRectMax();Ink(Visible(label),origin,min,max);
        if (ImGui.CalcTextSize(Visible(label)).X>max.X-min.X && ImGui.IsItemHovered()) ImGui.SetTooltip(Visible(label));return changed;
    }
    private static float FitField(string label,float? requestedPixels=null,float? minimumPixels=null,bool showLabel=true)
    {
        // Capture pending native width before any separate label consumes it.
        var requested=requestedPixels ?? ImGui.CalcItemWidth();
        var minimum=minimumPixels ?? 80*MaterialTheme.Metrics.Scale;
        var text=showLabel?Visible(label):string.Empty;
        var labelWidth=text.Length==0?0:ImGui.CalcTextSize(text).X+ImGui.GetStyle().ItemInnerSpacing.X;
        var total=MaterialLayout.FitNextItemWidth(requested+labelWidth,minimum+labelWidth);
        var width=MathF.Ceiling(Math.Max(minimum,total-labelWidth));
        ImGui.SetNextItemWidth(width);return width;
    }
    private static bool ClipFieldLabel(string original,Vector2 min,float width,ImDrawListPtr drawing)
    {
        if(Visible(original)==original.Split("##",2)[0])return false;
        var window=ImGui.GetWindowPos();
        drawing.PushClipRect(new Vector2(min.X,window.Y),new Vector2(min.X+width,window.Y+ImGui.GetWindowSize().Y),true);
        return true;
    }
    private static void FieldLabel(string original,Vector2 min,float width,ImDrawListPtr drawing,ImGuiWindowPtr window,Vector2 previousMax)
    {
        var visible=original.Split("##",2)[0];var translated=UiText.T(visible);if (translated==visible) return;
        var p=min+new Vector2(width+ImGui.GetStyle().ItemInnerSpacing.X,ImGui.GetStyle().FramePadding.Y);
        drawing.AddText(p,ImGui.GetColorU32(ImGuiCol.Text),translated);
        // Replace the original label's width as well as its ink, retaining native IDs and editing.
        var right=p.X+ImGui.CalcTextSize(translated).X;
        window.DC.CursorMaxPos=new Vector2(Math.Max(previousMax.X,right),window.DC.CursorMaxPos.Y);
        window.DC.CursorPosPrevLine=new Vector2(right,window.DC.CursorPosPrevLine.Y);
    }
    internal static bool InputText(string label,ref string value,int length,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None,bool showLabel=true)
    {
        var width=FitField(label,showLabel:showLabel);var min=ImGui.GetCursorScreenPos();
        var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();
        var clipped=showLabel?ClipFieldLabel(label,min,width,dl):true;
        if (!showLabel) dl.PushClipRect(min,min+new Vector2(width,ImGui.GetFrameHeight()),true);
        var changed=ImGui.InputText(label,ref value,length,flags);
        if (clipped) dl.PopClipRect();
        if(showLabel)FieldLabel(label,min,width,dl,parent,previousMax);
        else
        {
            parent.DC.CursorMaxPos=new Vector2(Math.Max(previousMax.X,min.X+width),parent.DC.CursorMaxPos.Y);
            parent.DC.CursorPosPrevLine=new Vector2(min.X+width,parent.DC.CursorPosPrevLine.Y);
        }
        return changed;
    }
    internal static bool InputTextWithHint(string label,string hint,ref string value,int length,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None)
    { var width=FitField(label);var min=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();var clipped=ClipFieldLabel(label,min,width,dl);var changed=ImGui.InputTextWithHint(label,UiText.T(hint),ref value,length,flags);if(clipped)dl.PopClipRect();FieldLabel(label,min,width,dl,parent,previousMax);return changed; }
    internal static bool InputTextMultiline(string label,ref string value,int length,Vector2 size,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None)
    { var requested=size.X<0?ImGui.GetContentRegionAvail().X+size.X:size.X>0?size.X:ImGui.CalcItemWidth();var width=FitField(label,requested);size.X=width;var min=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();var clipped=ClipFieldLabel(label,min,width,dl);var changed=ImGui.InputTextMultiline(label,ref value,length,size,flags);if(clipped)dl.PopClipRect();FieldLabel(label,min,width,dl,parent,previousMax);return changed; }
    internal static bool InputInt(string label,ref int value,int step=0,int fastStep=0,ImGuiInputTextFlags flags=ImGuiInputTextFlags.None)
    { var padding=ImGui.GetStyle().FramePadding;var gap=ImGui.GetStyle().ItemInnerSpacing.X;var minimum=Math.Max(80*MaterialTheme.Metrics.Scale,ImGui.CalcTextSize("-00000").X+2*padding.X+(step>0?2*(ImGui.GetFrameHeight()+gap):0));var width=FitField(label,minimumPixels:minimum);var min=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();var clipped=ClipFieldLabel(label,min,width,dl);var changed=ImGui.InputInt(label,ref value,step,fastStep,"%d",flags);if(clipped)dl.PopClipRect();FieldLabel(label,min,width,dl,parent,previousMax);return changed; }
    internal static bool SliderInt(string label,ref int value,int min,int max,string format="%d",ImGuiSliderFlags flags=ImGuiSliderFlags.None)
    { var width=FitField(label);var p=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();var clipped=ClipFieldLabel(label,p,width,dl);var changed=ImGui.SliderInt(label,ref value,min,max,UiText.T(format),flags);if(clipped)dl.PopClipRect();FieldLabel(label,p,width,dl,parent,previousMax);return changed; }
    internal static bool SliderFloat(string label,ref float value,float min,float max,string format="%.3f",ImGuiSliderFlags flags=ImGuiSliderFlags.None)
    { var width=FitField(label);var p=ImGui.GetCursorScreenPos();var parent=ImGuiP.GetCurrentWindow();var previousMax=parent.DC.CursorMaxPos;var dl=ImGui.GetWindowDrawList();var clipped=ClipFieldLabel(label,p,width,dl);var changed=ImGui.SliderFloat(label,ref value,min,max,UiText.T(format),flags);if(clipped)dl.PopClipRect();FieldLabel(label,p,width,dl,parent,previousMax);return changed; }
    internal static bool Combo(string label,ref int value,string[] options,int count)
    {
        var changed=false;
        if (BeginCombo(label,value>=0 && value<count?options[value]:string.Empty))
        {
            for(var index=0;index<count;index++)
            { ImGui.PushID(index);if (Selectable(options[index],value==index)) { changed=value!=index;value=index; }if (value==index) ImGui.SetItemDefaultFocus();ImGui.PopID(); }
            ImGui.EndCombo();
        }
        return changed;
    }
    internal static bool Combo(string label,ref int value,string options)
    { var items=options.Split('\0').Where(v=>v.Length>0).ToArray();return Combo(label,ref value,items,items.Length); }
    internal static bool BeginPopupModal(string original,ImGuiWindowFlags flags)
    {
        ImGui.SetNextWindowSize(new Vector2(520*MaterialTheme.Metrics.Scale,0),ImGuiCond.Always);
        var open=ImGui.BeginPopupModal(original,flags);if(open) Title(original.Split("##",2)[0]);return open;
    }
    internal static void Title(string original,string? display=null)
    {
        var translated=display ?? UiText.T(original);if (translated==original) return;
        var style=ImGui.GetStyle();var fontSize=ImGui.GetFontSize();var height=ImGui.GetFrameHeight();
        var flags=ImGuiP.GetCurrentWindow().Flags;
        var collapseLeft=(flags & (ImGuiWindowFlags.NoCollapse|ImGuiWindowFlags.Modal))==0 && style.WindowMenuButtonPosition==ImGuiDir.Left;
        var p=ImGui.GetWindowPos()+new Vector2(style.FramePadding.X+(collapseLeft?fontSize+style.ItemInnerSpacing.X:0),style.FramePadding.Y);
        var dl=ImGui.GetWindowDrawList();dl.PushClipRect(ImGui.GetWindowPos(),ImGui.GetWindowPos()+new Vector2(ImGui.GetWindowSize().X-2*height,height),false);
        var background=style.Colors[(int)(ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows)?ImGuiCol.TitleBgActive:ImGuiCol.TitleBg)];
        dl.AddRectFilled(p,p+new Vector2(Math.Max(ImGui.CalcTextSize(original).X,ImGui.CalcTextSize(translated).X),height-style.FramePadding.Y),ImGui.ColorConvertFloat4ToU32(background));
        dl.AddText(p,ImGui.GetColorU32(ImGuiCol.Text),translated);dl.PopClipRect();
    }

    internal static void TableHeadersRow()
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        for(var index=0;index<ImGui.TableGetColumnCount();index++)
        {
            if(!ImGui.TableSetColumnIndex(index)) continue;
            TableHeader(ImGui.TableGetColumnName(index));
        }
    }
    internal static void TableHeader(string original)
    {
        var translated=UiText.T(original);
        var p=ImGui.GetCursorScreenPos();var width=ImGui.GetContentRegionAvail().X;
        ImGui.PushStyleColor(ImGuiCol.Text,Vector4.Zero);ImGui.TableHeader(original);ImGui.PopStyleColor();
        var dl=ImGui.GetWindowDrawList();dl.PushClipRect(p,p+new Vector2(Math.Max(1,width),ImGui.GetTextLineHeight()),true);
        dl.AddText(p,ImGui.GetColorU32(ImGuiCol.Text),translated);dl.PopClipRect();
        if(ImGui.CalcTextSize(translated).X>width && ImGui.IsItemHovered()) ImGui.SetTooltip(translated);
    }
}
