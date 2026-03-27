using Dalamud.Configuration;
using System;

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
    public bool HideNonPartyPlayers { get; set; } = true;
    public bool HideNonPartyPets { get; set; } = true;
    public bool HideNonPartyChocobos { get; set; } = true;
    public bool HideNonPartyMinions { get; set; } = true;
    public bool KeepCurrentTargetVisible { get; set; } = true;

    public void Save()
        => Plugin.PluginInterface.SavePluginConfig(this);
}
