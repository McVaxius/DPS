using Dalamud.Configuration;
using System;
using DPS.Services;

namespace DPS;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;
    public bool PluginEnabled { get; set; }
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 1;
    public string DtrIconEnabled { get; set; } = "\uE0BB";
    public string DtrIconDisabled { get; set; } = "\uE0BC";
    public bool HideNonPartyPlayers { get; set; }
    public bool HideNonPartyPets { get; set; }
    public bool HideNonPartyChocobos { get; set; }
    public bool HideNonPartyMinions { get; set; }
    public bool KeepCurrentTargetVisible { get; set; } = true;
    public bool TextureRedirectEnabled { get; set; }
    public TextureRedirectScope TextureRedirectScope { get; set; } = TextureRedirectScope.CharaOnly;
    public TextureReplacementAsset TextureReplacementAsset { get; set; } = TextureReplacementAsset.Black16x16;
    public bool LogTextureRedirects { get; set; }
    public bool BackgroundNoRenderEnabled { get; set; }
    public bool BackgroundNoRenderOnlyWhenMinimized { get; set; }

    public void Save()
        => Plugin.PluginInterface.SavePluginConfig(this);
}
