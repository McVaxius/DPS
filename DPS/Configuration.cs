using Dalamud.Configuration;
using System;
using DPS.Services;

namespace DPS;

public enum ForegroundNoRenderMode
{
    SafeFrozenFrame = 0,
    LegacyBlackScreen = 1,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 6;
    public bool PluginEnabled { get; set; }
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 1;
    public string DtrIconEnabled { get; set; } = "\uE0BB";
    public string DtrIconDisabled { get; set; } = "\uE0BC";
    public bool CrowdSuppressionEnabled { get; set; }
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
    public bool ForegroundNoRenderEnabled { get; set; }
    public ForegroundNoRenderMode ForegroundNoRenderMode { get; set; } = ForegroundNoRenderMode.LegacyBlackScreen;
    public bool BackgroundNoRenderOnlyWhenMinimized { get; set; }
    public bool CleanDisableExperimentalRenderHack { get; set; } = true;
    public int BackgroundSafetyFrameIntervalSeconds { get; set; } = 5;
    public int BackgroundThrottleSleepMs { get; set; }
    public bool BackgroundRecoveryLoopEnabled { get; set; }
    public int BackgroundRecoveryMinMinutes { get; set; } = 15;
    public int BackgroundRecoveryMaxMinutes { get; set; } = 20;
    public int BackgroundRecoveryPulseSeconds { get; set; } = 5;
    public HotkeyBinding ForegroundToggleHotkey { get; set; } = new();
    public HotkeyBinding BackgroundToggleHotkey { get; set; } = new();
    public HotkeyBinding CrowdToggleHotkey { get; set; } = new();
    public HotkeyBinding AllOffHotkey { get; set; } = new();

    public void Save()
        => Plugin.PluginInterface.SavePluginConfig(this);
}

[Serializable]
public sealed class HotkeyBinding
{
    public bool Enabled { get; set; }
    public int KeyCode { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }

    public bool HasChord => KeyCode != 0;

    public void SetFrom(HotkeyBinding binding)
    {
        Enabled = binding.Enabled;
        KeyCode = binding.KeyCode;
        Ctrl = binding.Ctrl;
        Alt = binding.Alt;
        Shift = binding.Shift;
    }

    public void Clear()
    {
        Enabled = false;
        KeyCode = 0;
        Ctrl = false;
        Alt = false;
        Shift = false;
    }

    public bool SameChord(HotkeyBinding binding)
        => KeyCode != 0
        && KeyCode == binding.KeyCode
        && Ctrl == binding.Ctrl
        && Alt == binding.Alt
        && Shift == binding.Shift;
}
