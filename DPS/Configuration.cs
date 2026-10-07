using Dalamud.Configuration;
using System;
using DPS.Services;

namespace DPS;

public enum ForegroundNoRenderMode
{
    SafeFrozenFrame = 0,
    LegacyBlackScreen = 1,
}

public enum ResolutionProvider
{
    None,
    XASlave,
    CustomResolution,
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 13;
    public bool UiCompact { get; set; }
    public string UiLanguage { get; set; } = "en";
    public uint UiAccentRgb { get; set; } = 0x00BCD4;
    public bool PluginEnabled { get; set; }
    public bool DtrBarEnabled { get; set; } = true;
    public int DtrBarMode { get; set; } = 1;
    public string DtrIconEnabled { get; set; } = "\uE0BB";
    public string DtrIconDisabled { get; set; } = "\uE0BC";
    public bool DtrClickTurnEverythingOff { get; set; } = true;
    public bool DtrClickOpenMainWindow { get; set; } = true;
    public bool DtrClickTogglePluginEnabled { get; set; }
    public bool DtrClickToggleBackgroundNoRender { get; set; }
    public bool DtrClickToggleForegroundNoRender { get; set; }
    public bool DtrClickToggleCrowdSuppression { get; set; }
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
    public bool ContinuousBlackScreenEnforcementEnabled { get; set; } = true;
    public bool BackgroundNoRenderOnlyWhenMinimized { get; set; }
    public bool CleanDisableExperimentalRenderHack { get; set; } = true;
    public bool RenderDuringAreaTransitions { get; set; }
    public bool RenderWhileLoggedOut { get; set; }
    public bool PeriodicRenderFramesEnabled { get; set; }
    public bool AutoRetainerRenderConflictResolutionEnabled { get; set; }
    public int BackgroundSafetyFrameIntervalSeconds { get; set; } = 5;
    public int BackgroundThrottleSleepMs { get; set; }
    public bool BackgroundRecoveryLoopEnabled { get; set; }
    public int BackgroundRecoveryMinMinutes { get; set; } = 15;
    public int BackgroundRecoveryMaxMinutes { get; set; } = 20;
    public int BackgroundRecoveryPulseSeconds { get; set; } = 5;
    public bool ForegroundDisplayRecoveryGuardEnabled { get; set; }
    public bool DisplayRecoveryNoMonitors { get; set; }
    public bool DisplayRecoveryNoCurrentMonitor { get; set; }
    public bool DisplayRecoveryMissingWindow { get; set; }
    public bool DisplayRecoveryHiddenWindow { get; set; }
    public bool DisplayRecoveryMinimizedWindow { get; set; }
    public bool DisplayRecoveryInvalidWindowSize { get; set; }
    public bool DisplayRecoveryMonitorTopologyChanges { get; set; }
    public bool DisplayRecoveryCurrentMonitorChanges { get; set; }
    public bool DisplayRecoveryWindowMovement { get; set; }
    public bool DisplayRecoveryWindowResizing { get; set; }
    public int ForegroundDisplayRecoveryPauseSeconds { get; set; } = 180;
    public int ForegroundDisplayRecoveryStableSeconds { get; set; } = 30;
    public HotkeyBinding ForegroundToggleHotkey { get; set; } = new();
    public HotkeyBinding BackgroundToggleHotkey { get; set; } = new();
    public HotkeyBinding CrowdToggleHotkey { get; set; } = new();
    public HotkeyBinding AllOffHotkey { get; set; } = new();
    public HotkeyBinding WindowPlacementAndSizeLoadHotkey { get; set; } = new();
    public ResolutionProvider ResolutionProvider { get; set; }
    public float ResolutionScale { get; set; } = 0.25f;
    public HotkeyBinding ResolutionToggleHotkey { get; set; } = new();
    public RenderTrimConfiguration RenderTrim { get; set; } = new();
    public bool CustomResolutionDefaultsApplied { get; set; }
    public bool WindowPlacementAutoLoadEnabled { get; set; }
    public bool WindowSizeAutoLoadEnabled { get; set; }
    public SavedWindowPlacement? WindowPlacement { get; set; }

    public void Save()
        => Plugin.PluginInterface.SavePluginConfig(this);
}

[Flags]
public enum RenderTrimOption
{
    None = 0,
    MainView = 1 << 0,
    PostEffects = 1 << 1,
    CharacterAnimations = 1 << 2,
    ModelRenderer = 1 << 3,
    HumanRenderer = 1 << 4,
    CharacterBase = 1 << 5,
    CharacterMaterials = 1 << 6,
    VfxObjects = 1 << 7,
    Terrain = 1 << 8,
    Water = 1 << 9,
    Lights = 1 << 10,
    Geometry = 1 << 11,
    CameraMatrices = 1 << 12,
    WorkingSet = 1 << 13,
}

public enum RenderTrimMainViewMode
{
    BytePatch,
    DirectFieldWrite,
}

[Serializable]
public sealed class RenderTrimConfiguration
{
    public bool Enabled { get; set; }
    public bool RestoreOnStartup { get; set; }
    public bool StartupDiagnostics { get; set; }
    public RenderTrimOption SelectedOptions { get; set; }
    public RenderTrimMainViewMode MainViewMode { get; set; }
    public HotkeyBinding MasterHotkey { get; set; } = new();
    public HotkeyBinding MainViewHotkey { get; set; } = new();
    public HotkeyBinding PostEffectsHotkey { get; set; } = new();
    public HotkeyBinding CharacterAnimationsHotkey { get; set; } = new();
    public HotkeyBinding ModelRendererHotkey { get; set; } = new();
    public HotkeyBinding HumanRendererHotkey { get; set; } = new();
    public HotkeyBinding CharacterBaseHotkey { get; set; } = new();
    public HotkeyBinding CharacterMaterialsHotkey { get; set; } = new();
    public HotkeyBinding VfxObjectsHotkey { get; set; } = new();
    public HotkeyBinding TerrainHotkey { get; set; } = new();
    public HotkeyBinding WaterHotkey { get; set; } = new();
    public HotkeyBinding LightsHotkey { get; set; } = new();
    public HotkeyBinding GeometryHotkey { get; set; } = new();
    public HotkeyBinding CameraMatricesHotkey { get; set; } = new();
    public HotkeyBinding WorkingSetHotkey { get; set; } = new();
    public HotkeyBinding RevertAllHotkey { get; set; } = new();

    public HotkeyBinding GetHotkey(RenderTrimOption option)
        => option switch
        {
            RenderTrimOption.MainView => MainViewHotkey,
            RenderTrimOption.PostEffects => PostEffectsHotkey,
            RenderTrimOption.CharacterAnimations => CharacterAnimationsHotkey,
            RenderTrimOption.ModelRenderer => ModelRendererHotkey,
            RenderTrimOption.HumanRenderer => HumanRendererHotkey,
            RenderTrimOption.CharacterBase => CharacterBaseHotkey,
            RenderTrimOption.CharacterMaterials => CharacterMaterialsHotkey,
            RenderTrimOption.VfxObjects => VfxObjectsHotkey,
            RenderTrimOption.Terrain => TerrainHotkey,
            RenderTrimOption.Water => WaterHotkey,
            RenderTrimOption.Lights => LightsHotkey,
            RenderTrimOption.Geometry => GeometryHotkey,
            RenderTrimOption.CameraMatrices => CameraMatricesHotkey,
            RenderTrimOption.WorkingSet => WorkingSetHotkey,
            _ => throw new ArgumentOutOfRangeException(nameof(option)),
        };
}

[Serializable]
public sealed class SavedWindowPlacement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string? MonitorDeviceName { get; set; }
    public string? MonitorDevicePath { get; set; }
    public int MonitorLeft { get; set; }
    public int MonitorTop { get; set; }
    public int MonitorRight { get; set; }
    public int MonitorBottom { get; set; }
    public DateTime SavedUtc { get; set; }
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
