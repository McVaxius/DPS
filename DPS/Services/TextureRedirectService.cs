using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Graphics.Kernel;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;

namespace DPS.Services;

public sealed unsafe class TextureRedirectService : IDisposable
{
    private readonly record struct BlackTextureContentsKey(
        TextureFormat Format,
        uint Width,
        uint Height,
        uint Depth,
        byte MipLevels,
        byte ArraySize);

    private delegate byte LoadTexFilesDelegate(MaterialResourceHandle* material);

    private readonly Dictionary<BlackTextureContentsKey, byte[]> blackTextureContents = new();
    private Hook<LoadTexFilesDelegate>? loadTexFilesHook;
    private bool initialized;
    private bool initializationAttempted;

    public bool HooksActive
        => initialized && loadTexFilesHook?.IsEnabled == true;

    public string Status { get; private set; } = "Texture hooks idle.";
    public int RedirectedTextures { get; private set; }
    public int SuccessfulRedirectLoads { get; private set; }
    public int FailedRedirectLoads { get; private set; }
    public string LastGameTexturePath { get; private set; } = "None";
    public string LastReplacementPath { get; private set; } = "None";

    public string GetConfiguredReplacementPath(Configuration configuration)
        => "black://in-place/source-size";

    public void RefreshState(Configuration configuration, bool debugModeEnabled)
    {
        if (!debugModeEnabled)
        {
            DisableHooks();
            Status = "Experimental texture redirect hidden. Use /dps debug.";
            return;
        }

        if (configuration.PluginEnabled && configuration.TextureRedirectEnabled && !initializationAttempted)
            InitializeHooks();

        var shouldEnable = configuration.PluginEnabled && configuration.TextureRedirectEnabled && initialized;
        if (shouldEnable)
        {
            EnableHooks();
            return;
        }

        DisableHooks();
        if (!configuration.TextureRedirectEnabled)
            Status = "Texture redirect disabled.";
        else if (!configuration.PluginEnabled)
            Status = "Texture redirect waiting for plugin enable.";
        else if (!initializationAttempted)
            Status = "Texture hooks idle.";
    }

    public void ResetStats()
    {
        RedirectedTextures = 0;
        SuccessfulRedirectLoads = 0;
        FailedRedirectLoads = 0;
        LastGameTexturePath = "None";
        LastReplacementPath = "None";
    }

    public void Dispose()
    {
        DisableHooks();
        loadTexFilesHook?.Dispose();
        blackTextureContents.Clear();
    }

    private void InitializeHooks()
    {
        initializationAttempted = true;
        try
        {
            loadTexFilesHook = Plugin.GameInteropProvider.HookFromAddress<LoadTexFilesDelegate>(
                (nint)MaterialResourceHandle.MemberFunctionPointers.LoadTexFiles,
                LoadTexFilesDetour);
            initialized = true;
            Status = "Texture hooks ready.";
            Plugin.Log.Information("[DPS] Texture redirect hooks initialized from MaterialResourceHandle.LoadTexFiles in-place texture hack.");
        }
        catch (Exception ex)
        {
            initialized = false;
            Status = $"Texture hook init failed: {ex.Message}";
            Plugin.Log.Warning(ex, "[DPS] Texture redirect hook initialization failed.");
        }
    }

    private void EnableHooks()
    {
        if (!initialized)
            return;

        loadTexFilesHook?.Enable();
        Status = $"Texture redirect live: {ScopeLabel(Plugin.PluginInstance.Configuration.TextureRedirectScope)} via in-place Texture.InitializeContents";
    }

    private void DisableHooks()
    {
        loadTexFilesHook?.Disable();
    }

    private byte LoadTexFilesDetour(MaterialResourceHandle* material)
    {
        var result = loadTexFilesHook!.Original(material);
        if (result == 0 || material == null)
            return result;

        var configuration = Plugin.PluginInstance.Configuration;
        if (!configuration.PluginEnabled || !configuration.TextureRedirectEnabled)
            return result;

        try
        {
            ApplyInPlaceTextureHack(material, configuration);
        }
        catch (Exception ex)
        {
            FailedRedirectLoads++;
            Status = $"In-place texture blacking failed after {RedirectedTextures} attempts.";
            Plugin.Log.Warning(ex, "[DPS] In-place texture blacking failed.");
        }

        return result;
    }

    private void ApplyInPlaceTextureHack(MaterialResourceHandle* material, Configuration configuration)
    {
        if (material->Textures == null)
            return;

        for (var index = 0; index < material->TextureCount; ++index)
        {
            var textureHandle = material->Textures[index].TextureResourceHandle;
            if (textureHandle == null || textureHandle->Texture == null)
                continue;

            var texturePathPtr = material->Strings + material->Textures[index].PathOffset;
            var texturePath = (Marshal.PtrToStringUTF8((nint)texturePathPtr) ?? string.Empty).Replace('\\', '/').ToLowerInvariant();
            if (!texturePath.EndsWith(".tex", StringComparison.Ordinal))
                continue;

            if (!MatchesScope(configuration.TextureRedirectScope, texturePath))
                continue;

            var texture = textureHandle->Texture;
            if (!TryGetBlackTextureContents(texture, out var buffer))
            {
                FailedRedirectLoads++;
                continue;
            }

            var replacementPath = GetConfiguredReplacementPath(configuration);
            fixed (byte* ptr = buffer)
            {
                if (!texture->InitializeContents(ptr))
                {
                    FailedRedirectLoads++;
                    continue;
                }
            }

            RedirectedTextures++;
            SuccessfulRedirectLoads++;
            LastGameTexturePath = texturePath;
            LastReplacementPath = replacementPath;
            Status = $"Blacked {RedirectedTextures} textures via {ScopeLabel(configuration.TextureRedirectScope)}";

            if (configuration.LogTextureRedirects)
                Plugin.Log.Information("[DPS] Blanked material texture '{GamePath}' via in-place InitializeContents -> '{ReplacementPath}'", texturePath, replacementPath);
        }
    }

    private bool TryGetBlackTextureContents(Texture* texture, out byte[] buffer)
    {
        var width = texture->AllocatedWidth > 0 ? texture->AllocatedWidth : texture->ActualWidth;
        var height = texture->AllocatedHeight > 0 ? texture->AllocatedHeight : texture->ActualHeight;
        var depth = texture->Depth > 0 ? texture->Depth : 1u;
        var mipLevels = texture->MipLevel > 0 ? texture->MipLevel : (byte)1;
        var arraySize = texture->ArraySize > 0 ? texture->ArraySize : (byte)1;
        var key = new BlackTextureContentsKey(texture->TextureFormat, width, height, depth, mipLevels, arraySize);
        if (blackTextureContents.TryGetValue(key, out buffer!))
            return true;

        var totalByteLength = ComputeTextureByteLength(texture->TextureFormat, width, height, depth, mipLevels, arraySize);
        if (totalByteLength <= 0)
        {
            buffer = Array.Empty<byte>();
            return false;
        }

        buffer = new byte[totalByteLength];
        blackTextureContents[key] = buffer;
        return true;
    }

    private static int ComputeTextureByteLength(TextureFormat format, uint width, uint height, uint depth, byte mipLevels, byte arraySize)
    {
        var total = 0L;
        for (var mip = 0; mip < Math.Max((int)mipLevels, 1); ++mip)
        {
            var mipWidth = Math.Max(1u, width >> mip);
            var mipHeight = Math.Max(1u, height >> mip);
            var mipDepth = Math.Max(1u, depth >> mip);
            var mipByteLength = ComputeSingleMipByteLength(format, (int)mipWidth, (int)mipHeight);
            if (mipByteLength <= 0)
                return 0;

            total += (long)mipByteLength * mipDepth * Math.Max((int)arraySize, 1);
            if (total > int.MaxValue)
                return 0;
        }

        return (int)total;
    }

    private static int ComputeSingleMipByteLength(TextureFormat format, int width, int height)
    {
        var blockWidth = Math.Max(1, (width + 3) / 4);
        var blockHeight = Math.Max(1, (height + 3) / 4);

        return format switch
        {
            TextureFormat.L8_UNORM => width * height,
            TextureFormat.A8_UNORM => width * height,
            TextureFormat.R8_UNORM => width * height,
            TextureFormat.R8_UINT => width * height,
            TextureFormat.R16_UINT => width * height * 2,
            TextureFormat.R16_FLOAT => width * height * 2,
            TextureFormat.R16_UNORM => width * height * 2,
            TextureFormat.R32_UINT => width * height * 4,
            TextureFormat.R32_FLOAT => width * height * 4,
            TextureFormat.R8G8_UNORM => width * height * 2,
            TextureFormat.B4G4R4A4_UNORM => width * height * 2,
            TextureFormat.B5G5R5A1_UNORM => width * height * 2,
            TextureFormat.B8G8R8A8_UNORM => width * height * 4,
            TextureFormat.B8G8R8X8_UNORM => width * height * 4,
            TextureFormat.R16G16_FLOAT => width * height * 4,
            TextureFormat.R16G16_UNORM => width * height * 4,
            TextureFormat.R32G32_FLOAT => width * height * 8,
            TextureFormat.R11G11B10_FLOAT => width * height * 4,
            TextureFormat.R16G16B16A16_FLOAT => width * height * 8,
            TextureFormat.R32G32B32A32_FLOAT => width * height * 16,
            TextureFormat.R10G10B10A2_UNORM => width * height * 4,
            TextureFormat.R10G10B10A2_UNORM_2 => width * height * 4,
            TextureFormat.BC1_UNORM => blockWidth * blockHeight * 8,
            TextureFormat.BC2_UNORM => blockWidth * blockHeight * 16,
            TextureFormat.BC3_UNORM => blockWidth * blockHeight * 16,
            TextureFormat.BC4_UNORM => blockWidth * blockHeight * 8,
            TextureFormat.BC5_UNORM => blockWidth * blockHeight * 16,
            TextureFormat.BC6H_SF16 => blockWidth * blockHeight * 16,
            TextureFormat.BC7_UNORM => blockWidth * blockHeight * 16,
            _ => 0,
        };
    }

    private static bool MatchesScope(TextureRedirectScope scope, string path)
        => scope switch
        {
            TextureRedirectScope.CharaOnly => path.StartsWith("chara/", StringComparison.Ordinal),
            TextureRedirectScope.WorldOnly => path.StartsWith("bg/", StringComparison.Ordinal)
                                              || path.StartsWith("bgcommon/", StringComparison.Ordinal),
            TextureRedirectScope.EffectsOnly => path.StartsWith("vfx/", StringComparison.Ordinal),
            TextureRedirectScope.AggressiveNonUi => !path.StartsWith("ui/", StringComparison.Ordinal)
                                                    && !path.StartsWith("common/font", StringComparison.Ordinal),
            TextureRedirectScope.EverythingTex => true,
            _ => false,
        };

    public static string ScopeLabel(TextureRedirectScope scope)
        => scope switch
        {
            TextureRedirectScope.CharaOnly => "Characters",
            TextureRedirectScope.WorldOnly => "World / terrain",
            TextureRedirectScope.EffectsOnly => "VFX only",
            TextureRedirectScope.AggressiveNonUi => "Aggressive non-UI",
            TextureRedirectScope.EverythingTex => "Everything .tex",
            _ => "Unknown",
        };

    public static string AssetLabel(TextureReplacementAsset asset)
        => asset switch
        {
            TextureReplacementAsset.Black16x16 => "Black 16x16",
            TextureReplacementAsset.Black1x1 => "Black 1x1",
            _ => "Unknown",
        };
}
