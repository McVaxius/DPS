using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Resources;
using System.Text.RegularExpressions;
using AethertekUI;
using Dalamud.Bindings.ImGui;

namespace DPS.Windows;

internal sealed class UiText : IDisposable
{
    [ThreadStatic] private static UiText? current;
    internal static UiText Current => current ?? throw new InvalidOperationException("Enter the DPS UI frame before drawing.");
    internal static readonly (string Code,string Name)[] Languages=[("en","English"),("de","Deutsch"),("fr","Français"),
        ("es","Español"),("it","Italiano"),("ru","Русский"),("ja","日本語"),("ko","한국어"),("zh-Hans","简体中文")];
    internal static IEnumerable<string> CjkLanguages(string selected) => new[]{"ja","ko","zh-Hans"}.OrderBy(code=>code==selected?0:1);
    private readonly ResourceManager manager;
    private readonly ResourceManager englishManager;
    private readonly Dictionary<string,string> labels;
    private readonly string[] concatenatedPrefixes;
    internal ResourceSet Resources { get; }
    internal IReadOnlyList<string> RequiredText { get; }
    internal CultureInfo Culture { get; }
    internal string Language { get; }
    private readonly Func<UiFontRole,IDisposable> pushFont;
    private readonly (Regex Pattern, string Key, string Prefix, int ArgumentCount)[] messageTemplates;
    internal UiText(string language, Func<UiFontRole,IDisposable> pushFont)
    {
        Language=Languages.Any(l=>l.Code==language)?language:"en";
        Culture=CultureInfo.GetCultureInfo(Language);
        manager=new ResourceManager("DPS.Localization.Strings_"+Language.Replace('-','_'),typeof(UiText).Assembly);
        Resources=manager.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException(Language);
        englishManager=new ResourceManager("DPS.Localization.Strings_en",typeof(UiText).Assembly);
        var english=englishManager.GetResourceSet(CultureInfo.InvariantCulture,true,false) ?? throw new MissingManifestResourceException("en");
        RequiredText=Values(Resources).Concat(Values(english)).Concat(Languages.Select(l=>l.Name)).Distinct().ToArray();
        labels=english.Cast<DictionaryEntry>().ToDictionary(entry=>(string)entry.Value!,entry=>(string)entry.Key,StringComparer.OrdinalIgnoreCase);
        if (labels.Count!=Resources.Cast<DictionaryEntry>().Count() || labels.Values.Any(key=>Resources.GetString(key,false) is null))
            throw new MissingManifestResourceException("Incomplete DPS UI translations for "+Language);
        concatenatedPrefixes=labels.Keys.Where(key=>key.EndsWith(' ') || key.EndsWith('.')).OrderByDescending(key=>key.Length).ToArray();
        this.pushFont=pushFont;
        // Service messages remain English in logs; only their UI copies are localized.
        var parameter = new Regex(@"\{(\d+)(?::([^}]+))?\}");
        messageTemplates = labels.Keys
            .Where(key => parameter.IsMatch(key) && parameter.Replace(key, "").Any(char.IsLetter))
            .OrderByDescending(key=>parameter.Replace(key, "").Length).Select(key =>
            {
                var argumentCount = 0;
                var pattern = "^";
                var offset = 0;
                foreach (Match hole in parameter.Matches(key))
                {
                    var index = int.Parse(hole.Groups[1].Value, CultureInfo.InvariantCulture);
                    pattern += Regex.Escape(key[offset..hole.Index]) + $"(?<arg{index}>.*?)";
                    argumentCount = Math.Max(argumentCount, index + 1);
                    offset = hole.Index + hole.Length;
                }
                pattern += Regex.Escape(key[offset..]) + "$";
                return (new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(20)), key, key[..parameter.Match(key).Index], argumentCount);
            }).ToArray();
    }
    internal static string T(string english)
    {

        if (Current.labels.TryGetValue(english,out var resourceKey)) return Current.Resources.GetString(resourceKey,false)!;
        foreach (var template in Current.messageTemplates)
        {
            if (!english.StartsWith(template.Prefix,StringComparison.Ordinal)) continue;
            var match = template.Pattern.Match(english);
            if (!match.Success) continue;
            // Captures are already formatted service values; keep empty arguments and identifiers exact.
            var args = Enumerable.Range(0, template.ArgumentCount).Select(index =>
            {
                var value = match.Groups[$"arg{index}"].Value;
                return TranslateArgument(template.Key,index,value);
            }).ToArray();
            return string.Format(Current.Culture, Current.Resources.GetString(Current.labels[template.Key], false)!, args);
        }
        foreach (var prefix in Current.concatenatedPrefixes)
            if (english.Length>prefix.Length && english.StartsWith(prefix,StringComparison.Ordinal))
                return Current.Resources.GetString(Current.labels[prefix],false)!+" "+T(english[prefix.Length..]);
        if (english.Contains(" | ",StringComparison.Ordinal)) return string.Join(" | ",english.Split(" | ",StringSplitOptions.None).Select(T));
        return english; // External names, command tokens and raw runtime data retain their original values.
    }
    private static string TranslateArgument(string format,int index,string value)
    {
        if (format.StartsWith("DPS DLL={0}; Mode={1}; Intent={2};", StringComparison.Ordinal))
        {
            if (index is 1 or 2 or 3 or 4 or 10 or 11) return Status(value);
            if ((index is 7 or 8 or 9) && (value is "(not used)" or "(unavailable)")) return T(value);
            return value;
        }
        if (index == 0 && (format is "Display recovery watching enabled foreground exceptions: {0}." or
            "Display recovery active; waiting for enabled conditions to clear: {0}." or
            "waiting for enabled conditions to clear: {0}" or
            "This chord is currently used by {0}. Confirming will clear those conflicting bindings."))
            return Causes(value);
        if (format == "Click: {0}.")
            return string.Join(T("; then "), value.Split("; then ", StringSplitOptions.None).Select(T));
        if (format == "monitors={0} [{1}]; window={2} ({3}); currentMonitor={4} ({5}); valid={6}")
            return index is 1 or 2 or 3 or 5 or 6 ? Status(value) : value;
        if (format == "{0},{1} {2}x{3}; visible={4}; minimized={5}")
            return index is 4 or 5 ? T(value) : value;
        if (format == "{0} — {1} — {2} — {3}x{4} at {5},{6} [{7}]")
            return index is 1 or 2 ? T(value) : value;
        return LocalizeArgument(format,index) ? T(value) : value;
    }
    internal static string Causes(string value)
        => string.Join(", ", value.Split(", ", StringSplitOptions.None).Select(T));

    private static string Status(string value)
    {
        var translated = T(value);
        if (translated != value || !value.Contains("; ", StringComparison.Ordinal)) return translated;
        return string.Join("; ", value.Split("; ", StringSplitOptions.None).Select(T));
    }

    // Only source-authored status/label positions are translated; paths, addresses and external errors stay raw.
    private static bool LocalizeArgument(string format,int index)
    {
        if (format is "{0}: {1}" or "Transition exception: {0}; logged-out exception: {1}; periodic frames: {2}." or
            "Loaded saved window position and size. {0} {1}" or "{0} No further size retry is scheduled; retry cap will be reached in {1}." or
            "{0} Retrying saved size in {1}." or
            "Saved game window size restore failed after {0}; retry cap reached. Last result: {1}") return true;
        if (format is "Tooltip: {0} {1}. {2}" or "{0} {1}. {2}") return index is 1 or 2;
        if (format == "Loaded game window to X/Y {0}, {1} on {2} via {3}; size preserved ({4}).") return index == 3;
        if (format == "Blacked {0} textures via {1}") return index == 1;
        if (format is "Could not disable foreground render via {0}: {1}" or "Could not restore foreground render via {0}: {1}") return true;
        return index == 0 && (format is "{0} listening" or "Capture a new hotkey for {0}." or "Clear the {0} hotkey." or
            "Status: {0}" or "Preview: {0}" or "DPS: {0}" or "Window size auto-load remains {0}." or
            "Automatic AutoRetainer conflict resolution: {0}." or "Texture redirect live: {0} via in-place Texture.InitializeContents" or
            "OFF requested ({0})" or "Client load restore: {0}" or "Resolution scaling: {0}" or
            "Loaded saved window position, but size restore failed: {0}" or "Window + size load failed before size restore: {0}" or
            "XA Slave toggle follows the last successful DPS request this session ({0}). After changing scaling in XA Slave, use these enable/disable buttons to set the toggle state." or
            "Automatic recovery pulse active for another {0}." or "Automatic recovery pulse queued in {0}." or
            "Display recovery active; foreground no-render re-arms in {0}." or "Decrease {0} by 1." or "Increase {0} by 1." or
            "Foreground no-render unavailable: {0}" or "Foreground no-render restore pending: {0}" or
            "Foreground no-render unavailable: unsupported mode {0}." or "Could not observe foreground render: {0}");
    }
    internal static string F(string english,params object?[] args) => string.Format(Current.Culture,T(english),
        args.Select((value,index)=>value is Enum e?T(e.ToString()):value is bool b?T(b.ToString()):value is string s?TranslateArgument(english,index,s):value).ToArray());
    internal static string F(FormattableString text) => F(text.Format,text.GetArguments());
    internal static IDisposable Font(UiFontRole role) => Current.pushFont(role);
    internal Scope Enter() => new(this);
    internal readonly struct Scope : IDisposable
    {
        private readonly UiText? previous;
        internal Scope(UiText value) { previous=current; current=value; }
        public void Dispose() => current=previous;
    }
    internal static string Date(DateTimeOffset? date) => date?.ToLocalTime().ToString("g",Current.Culture) ?? T("Never");
    internal ushort[] GlyphRanges()
    {
        var chars=RequiredText.SelectMany(t=>t).Where(c=>!char.IsControl(c))
            .Concat(Enumerable.Range(0x20,0x024F-0x20+1).Select(i=>(char)i))
            .Concat(Enumerable.Range(0x0400,0x052F-0x0400+1).Select(i=>(char)i)).Concat("—").Distinct().Order().ToArray();
        var result=new List<ushort>();
        for(var index=0;index<chars.Length;index++)
        {
            var first=chars[index]; var last=first;
            while(index+1<chars.Length && chars[index+1]==last+1) last=chars[++index];
            result.Add(first); result.Add(last);
        }
        result.Add(0); return result.ToArray();
    }
    internal static IEnumerable<string> Values(ResourceSet set) => set.Cast<DictionaryEntry>().Select(e=>(string)e.Value!);
    public void Dispose() { manager.ReleaseAllResources();englishManager.ReleaseAllResources(); }
}
