using System.Runtime.InteropServices;
using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Hooking;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using NativeGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace DPS.Services;

public sealed record RenderTrimControl(RenderTrimOption Option, string Id, string Name, string Description, string Risk);
public sealed record RenderTrimFailure(string Reason, string? Detail = null);

public sealed unsafe class RenderTrimService : IDisposable
{
    private delegate void SceneUpdateDelegate(nint instance);
    private const string MainViewSignature = "41 83 BD 4C 83 03 00 FF 0F 85 ?? ?? ?? ?? 48 89 AC 24";
    private const string PostEffectsSignature = "41 83 BD 4C 83 03 00 FF 75 ?? 48 8B 0D";
    private const RenderTrimOption SuppressiveOptions = (RenderTrimOption)((1 << 13) - 1);
    private readonly Configuration configuration;
    private readonly BackgroundRenderGateService renderGate;
    private readonly ForegroundRenderControlService foreground;
    private readonly Dictionary<RenderTrimOption, Hook<SceneUpdateDelegate>> hooks = new();
    private readonly Dictionary<RenderTrimOption, nint> patchSites = new();
    private readonly Dictionary<RenderTrimOption, RenderTrimFailure> failures = new();
    private RenderTrimOption requested;
    private RenderTrimOption owned;
    private RenderTrimMainViewMode ownedMainViewMode;
    private Manager* ownedManager;
    private uint savedInitializationFlags;
    private long nextWorkingSetTick;
    private bool initialized;
    private volatile bool disposed;
    private volatile bool normalFramePause;
    private bool startupDiagnosticsPending;

    public static IReadOnlyList<RenderTrimControl> Controls { get; } = new RenderTrimControl[]
    {
        new(RenderTrimOption.MainView, "render-skip", "Main-view render skip", "Skips main-view dispatch. BytePatch changes an instruction; DirectFieldWrite clears initialization flags each frame and may race with game writes.", "Safe"),
        new(RenderTrimOption.PostEffects, "render-skip-post", "Post-effect render skip", "Skips the post-effect dispatch check. DirectFieldWrite already covers this check, so this separate patch is redundant in that mode.", "Safe"),
        new(RenderTrimOption.CharacterAnimations, "chara-animations", "Character animation updates", "Skips skeleton animation work. Characters freeze visually; skipped state may need a transition to refresh.", "Safe"),
        new(RenderTrimOption.ModelRenderer, "model-renderer", "Model renderer", "Skips model render submission. Models may disappear or retain stale visual state.", "Safe"),
        new(RenderTrimOption.HumanRenderer, "render-human", "Human renderer", "Skips humanoid render setup. Upstream reports a CPU/GPU tradeoff; GPU-bound clients may become slower.", "Tradeoff"),
        new(RenderTrimOption.CharacterBase, "render-chara-base", "Character base", "Skips base character rendering. Character visuals may remain stale until refreshed.", "Safe"),
        new(RenderTrimOption.CharacterMaterials, "render-chara-base-mat", "Character materials", "Skips character material updates. Materials and visual effects may remain stale until refreshed.", "Safe"),
        new(RenderTrimOption.VfxObjects, "render-vfx-object", "VFX objects", "Skips VFX transform and bounds updates. Effects may remain stale or require a transition to refresh.", "Safe"),
        new(RenderTrimOption.Terrain, "render-terrain", "Terrain renderer", "Skips terrain render dispatch. Terrain can disappear while suppression is active.", "Safe"),
        new(RenderTrimOption.Water, "render-water", "Water renderer", "Skips water render passes. Water visuals can disappear while suppression is active.", "Safe"),
        new(RenderTrimOption.Lights, "render-lights", "Dynamic lights", "Skips dynamic lighting. Lighting and scene appearance can change.", "Safe"),
        new(RenderTrimOption.Geometry, "geometry-renderer", "Geometry renderer", "Skips geometry render dispatch. Scene geometry can disappear while suppression is active.", "Safe"),
        new(RenderTrimOption.CameraMatrices, "camera-matrices", "Camera matrices", "Skips camera matrices. Visibility, distance and cone calculations can become incorrect.", "Risky"),
        new(RenderTrimOption.WorkingSet, "working-set-trim", "Working-set eviction", "Evicts pages immediately and every sixty seconds. Allocations remain; returning pages can hitch. Turning this off cannot undo an eviction.", "Tradeoff"),
    };

    public RenderTrimService(Configuration configuration, BackgroundRenderGateService renderGate, ForegroundRenderControlService foreground)
    {
        this.configuration = configuration;
        this.renderGate = renderGate;
        this.foreground = foreground;
        startupDiagnosticsPending = configuration.RenderTrim.StartupDiagnostics;
        if (configuration.RenderTrim.RestoreOnStartup && configuration.RenderTrim.Enabled)
        {
            MasterRequested = true;
            requested = configuration.RenderTrim.SelectedOptions;
        }
    }

    public bool MasterRequested { get; private set; }
    public bool ExternalRenderTrimLoaded { get; private set; }
    public DateTimeOffset? LastWorkingSetEviction { get; private set; }
    public bool IsRequested(RenderTrimOption option) => MasterRequested && requested.HasFlag(option);
    public bool IsActive(RenderTrimOption option)
    {
        if (!owned.HasFlag(option)) return false;
        try
        {
            if (option == RenderTrimOption.MainView && ownedMainViewMode == RenderTrimMainViewMode.DirectFieldWrite)
            {
                var manager = Manager.Instance();
                return manager != null && manager == ownedManager && manager->InitializationFlags == 0;
            }
            if (patchSites.TryGetValue(option, out var site))
            {
                RequireCurrentInstruction(site, option == RenderTrimOption.MainView ? MainViewSignature : PostEffectsSignature, 1);
                return true;
            }
            if (hooks.TryGetValue(option, out var hook))
                return hook.IsEnabled && ShouldSuppress(option);
            return option == RenderTrimOption.WorkingSet;
        }
        catch { return false; }
    }

    public RenderTrimFailure? Failure(RenderTrimOption option) => failures.GetValueOrDefault(option);

    public string Availability(RenderTrimOption option)
    {
        if (Failure(option) != null)
            return "Native operation failed.";
        if (option is not (RenderTrimOption.MainView or RenderTrimOption.PostEffects or RenderTrimOption.HumanRenderer or
            RenderTrimOption.CharacterBase or RenderTrimOption.CharacterMaterials or RenderTrimOption.WorkingSet))
            return "The exact native function contract is unverified for this client.";
        if (ExternalRenderTrimLoaded)
            return "External RenderTrim is loaded. Disable it before using DPS trims.";
        if (option != RenderTrimOption.WorkingSet && configuration.ForegroundNoRenderEnabled &&
            configuration.ForegroundNoRenderMode == ForegroundNoRenderMode.LegacyBlackScreen)
            return "Unavailable while legacy black-screen mode owns rendering.";
        return "Checked when activated.";
    }

    public bool CanRequest(RenderTrimOption option) => Availability(option) == "Checked when activated.";

    public string State(RenderTrimOption option)
    {
        if (owned.HasFlag(option) && Failure(option) != null) return "Owned cleanup is pending.";
        if (Failure(option) != null) return "Native operation failed.";
        if (IsActive(option)) return "ACTIVE";
        if (!IsRequested(option)) return configuration.RenderTrim.SelectedOptions.HasFlag(option) ? "Selected, inactive" : "OFF";
        if (!configuration.PluginEnabled) return "Waiting for the DPS master.";
        if (normalFramePause && option != RenderTrimOption.WorkingSet) return "Paused for normal rendering.";
        return Availability(option);
    }

    public void SetMaster(bool enabled)
    {
        if (disposed) return;
        MasterRequested = enabled;
        configuration.RenderTrim.Enabled = enabled;
        requested = enabled ? configuration.RenderTrim.SelectedOptions : RenderTrimOption.None;
        if (!enabled) ReleaseOwned(owned, force: true);
        configuration.Save();
        RefreshState();
    }

    public void Select(RenderTrimOption option, bool selected)
    {
        if (disposed) return;
        if (selected && !CanRequest(option)) return;
        configuration.RenderTrim.SelectedOptions = selected
            ? configuration.RenderTrim.SelectedOptions | option
            : configuration.RenderTrim.SelectedOptions & ~option;
        if (!selected)
        {
            requested &= ~option;
            ReleaseOwned(option, force: true);
            if (!owned.HasFlag(option)) failures.Remove(option);
        }
        else if (MasterRequested)
            requested |= option;
        configuration.Save();
        RefreshState();
    }

    public void Toggle(RenderTrimOption option)
        => Select(option, !configuration.RenderTrim.SelectedOptions.HasFlag(option));

    public void ActivateSelected() => SetMaster(true);

    public void RevertAll(bool save = true)
    {
        MasterRequested = false;
        requested = RenderTrimOption.None;
        configuration.RenderTrim.Enabled = false;
        ReleaseOwned(owned, force: true);
        if (save) configuration.Save();
    }

    public void RefreshState()
    {
        if (disposed) return;
        initialized = true;
        Tick();
        if (startupDiagnosticsPending)
        {
            startupDiagnosticsPending = false;
            foreach (var control in Controls)
                Plugin.Log.Information("[DPS][RenderTrim] {Trim}: {Availability}", control.Id, Availability(control.Option));
        }
    }

    public void Tick()
    {
        if (disposed || !initialized) return;
        ExternalRenderTrimLoaded = Plugin.PluginInterface.InstalledPlugins.Any(plugin =>
            plugin.IsLoaded && plugin.InternalName.Equals("RenderTrim", StringComparison.OrdinalIgnoreCase));
        if (!MasterRequested || !configuration.PluginEnabled || ExternalRenderTrimLoaded)
        {
            ReleaseOwned(owned);
            return;
        }
        normalFramePause = (requested & SuppressiveOptions) != RenderTrimOption.None && ShouldAdmitNormalRendering();
        var wanted = normalFramePause ? requested & ~SuppressiveOptions : requested;
        if (configuration.ForegroundNoRenderEnabled && configuration.ForegroundNoRenderMode == ForegroundNoRenderMode.LegacyBlackScreen)
            wanted &= ~SuppressiveOptions;
        if (owned.HasFlag(RenderTrimOption.MainView) && ownedMainViewMode != configuration.RenderTrim.MainViewMode)
            ReleaseOwned(RenderTrimOption.MainView);
        ReleaseOwned(owned & ~wanted);
        foreach (var control in Controls)
        {
            var option = control.Option;
            if (!wanted.HasFlag(option) || !CanRequest(option)) continue;
            if (owned.HasFlag(option))
            {
                if (option != RenderTrimOption.WorkingSet &&
                    !(option == RenderTrimOption.MainView && ownedMainViewMode == RenderTrimMainViewMode.DirectFieldWrite) && !IsActive(option))
                {
                    failures[option] = new("The owned native operation changed externally.");
                    ReleaseOwned(option, force: true);
                }
                continue;
            }
            try { Apply(option); }
            catch (Exception ex)
            {
                RecordFailure(option, ex);
                ReleaseOwned(option, force: true);
                Plugin.Log.Warning(ex, "[DPS][RenderTrim] Could not apply {Trim}.", control.Id);
            }
        }
        if (owned.HasFlag(RenderTrimOption.MainView) && wanted.HasFlag(RenderTrimOption.MainView) &&
            ownedMainViewMode == RenderTrimMainViewMode.DirectFieldWrite && ownedMainViewMode == configuration.RenderTrim.MainViewMode &&
            !failures.ContainsKey(RenderTrimOption.MainView))
        {
            try { WriteInitializationFlags(); }
            catch (Exception ex)
            {
                RecordFailure(RenderTrimOption.MainView, ex);
                ReleaseOwned(RenderTrimOption.MainView, force: true);
                Plugin.Log.Warning(ex, "[DPS][RenderTrim] Could not update initialization flags.");
            }
        }
        if (owned.HasFlag(RenderTrimOption.WorkingSet) && Environment.TickCount64 >= nextWorkingSetTick)
        {
            try { EvictWorkingSet(); }
            catch (Exception ex)
            {
                RecordFailure(RenderTrimOption.WorkingSet, ex);
                ReleaseOwned(RenderTrimOption.WorkingSet, force: true);
                Plugin.Log.Warning(ex, "[DPS][RenderTrim] Working-set eviction stopped after a failure.");
            }
        }
    }

    // Keep the pause until the next framework update, including the remainder of a safety frame.
    public void AdmitNormalRenderFrame()
    {
        if (disposed || !initialized || !MasterRequested) return;
        normalFramePause = true;
        ReleaseOwned(owned & SuppressiveOptions);
    }

    private bool ShouldAdmitNormalRendering()
        => (configuration.RenderDuringAreaTransitions && (Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51]))
        || (configuration.RenderWhileLoggedOut && !Plugin.ClientState.IsLoggedIn)
        || foreground.DisplayRecoveryBypassActive || renderGate.BackgroundRecoveryBypassActive
        || renderGate.ShouldAdmitNormalRendering();

    private void Apply(RenderTrimOption option)
    {
        if (option == RenderTrimOption.WorkingSet)
        {
            EvictWorkingSet();
            owned |= option;
            return;
        }
        if (option == RenderTrimOption.MainView && configuration.RenderTrim.MainViewMode == RenderTrimMainViewMode.DirectFieldWrite)
        {
            ValidateCompare(MainViewSignature, false);
            var manager = Manager.Instance();
            if (manager == null) throw new NativeOperationException("The current render manager is unavailable.");
            var initializationFlags = manager->InitializationFlags;
            if (initializationFlags == 0)
                throw new NativeOperationException("Initialization flags are already zero; DPS did not claim the field.");
            ownedManager = manager;
            savedInitializationFlags = initializationFlags;
            ownedMainViewMode = configuration.RenderTrim.MainViewMode;
            owned |= option;
            ownedManager->InitializationFlags = 0;
            return;
        }
        if (option is RenderTrimOption.MainView or RenderTrimOption.PostEffects)
        {
            var site = ValidateCompare(option == RenderTrimOption.MainView ? MainViewSignature : PostEffectsSignature, option == RenderTrimOption.PostEffects);
            patchSites[option] = site;
            if (option == RenderTrimOption.MainView)
                ownedMainViewMode = configuration.RenderTrim.MainViewMode;
            owned |= option;
            WriteCodeByte(site + 7, 1);
            return;
        }
        var signature = option switch
        {
            RenderTrimOption.HumanRenderer => "40 53 48 83 EC ?? 48 8B D9 E8 ?? ?? ?? ?? 48 8B CB E8 ?? ?? ?? ?? 83 BB",
            RenderTrimOption.CharacterBase => "48 89 5C 24 ?? 57 48 83 EC ?? 33 FF 48 8B D9 89 B9 ?? ?? ?? ?? 40 88 B9",
            RenderTrimOption.CharacterMaterials => "48 89 5C 24 ?? 48 89 6C 24 ?? 56 57 41 56 48 83 EC ?? 4C 89 7C 24",
            _ => throw new NativeOperationException("The exact native function contract is unverified for this client."),
        };
        if (!hooks.TryGetValue(option, out var hook))
        {
            var address = Plugin.SigScanner.ScanText(signature);
            RequireCurrentInstruction(address, signature);
            var documentedAddress = DocumentedSceneMethod(option);
            if (documentedAddress == 0 || documentedAddress != address)
                throw new NativeOperationException("The signature does not match the documented scene method.");
            Hook<SceneUpdateDelegate>? capturedHook = null;
            capturedHook = Plugin.GameInteropProvider.HookFromAddress<SceneUpdateDelegate>(address, instance =>
            {
                if (!ShouldSuppress(option))
                    capturedHook!.Original(instance);
            });
            hook = capturedHook;
            hooks.Add(option, hook);
        }
        RequireCurrentInstruction(hook.Address, signature);
        owned |= option;
        hook.Enable();
    }

    private bool ShouldSuppress(RenderTrimOption option)
        => !disposed && MasterRequested && configuration.PluginEnabled && IsRequested(option) && !normalFramePause
        && !ExternalRenderTrimLoaded && !(configuration.ForegroundNoRenderEnabled &&
            configuration.ForegroundNoRenderMode == ForegroundNoRenderMode.LegacyBlackScreen);

    private static nint DocumentedSceneMethod(RenderTrimOption option)
    {
        if (option == RenderTrimOption.CharacterBase)
            return CharacterBase.StaticVirtualTablePointer == null ? 0 : (nint)CharacterBase.StaticVirtualTablePointer->UpdateRender;
        if (option == RenderTrimOption.CharacterMaterials)
            return CharacterBase.StaticVirtualTablePointer == null ? 0 : (nint)CharacterBase.StaticVirtualTablePointer->UpdateMaterials;
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null) return 0;
        var drawObject = ((NativeGameObject*)player.Address)->DrawObject;
        if (drawObject == null || drawObject->GetObjectType() != ObjectType.CharacterBase) return 0;
        // The inherited slot's installed contract is void(this*), including Human's override.
        return (nint)((CharacterBase*)drawObject)->VirtualTable->UpdateRender;
    }

    private void WriteInitializationFlags()
    {
        var manager = Manager.Instance();
        if (manager == null || manager != ownedManager)
        {
            failures[RenderTrimOption.MainView] = new("The render manager changed while DPS owned the field.");
            ReleaseOwned(RenderTrimOption.MainView, force: true);
            return;
        }
        if (manager->InitializationFlags != 0)
            savedInitializationFlags = manager->InitializationFlags;
        manager->InitializationFlags = 0;
    }

    private void ReleaseOwned(RenderTrimOption options, bool force = false)
    {
        foreach (var control in Controls)
        {
            var option = control.Option;
            if (!options.HasFlag(option) || !owned.HasFlag(option)) continue;
            if (!force && failures.ContainsKey(option)) continue;
            try
            {
                if (option == RenderTrimOption.MainView && ownedMainViewMode == RenderTrimMainViewMode.DirectFieldWrite)
                {
                    var manager = Manager.Instance();
                    if (manager != null && manager == ownedManager && manager->InitializationFlags == 0)
                        manager->InitializationFlags = savedInitializationFlags;
                    ownedManager = null;
                }
                else if (patchSites.TryGetValue(option, out var site))
                {
                    try { RequireCurrentInstruction(site, option == RenderTrimOption.MainView ? MainViewSignature : PostEffectsSignature, 1); }
                    catch (NativeOperationException ex) when (ex.Message == "The native bytes are already changed or incompatible.")
                    {
                        patchSites.Remove(option);
                        owned &= ~option;
                        failures[option] = new("The owned instruction was changed externally; DPS did not overwrite it.");
                        continue;
                    }
                    WriteCodeByte(site + 7, 0xFF);
                    patchSites.Remove(option);
                }
                else if (hooks.TryGetValue(option, out var hook))
                    hook.Disable();
                owned &= ~option;
            }
            catch (Exception ex)
            {
                RecordFailure(option, ex);
                Plugin.Log.Warning(ex, "[DPS][RenderTrim] Could not release {Trim}.", control.Id);
            }
        }
    }

    private static nint ValidateCompare(string signature, bool postEffects)
    {
        var address = Plugin.SigScanner.ScanText(signature);
        RequireCurrentInstruction(address, signature);
        // Obtain the declared offset without marshaling ClientStructs' nested native layouts.
        var flagsOffset = typeof(Manager).GetField(nameof(Manager.InitializationFlags))?.GetCustomAttribute<FieldOffsetAttribute>()?.Value;
        if (!flagsOffset.HasValue || Marshal.ReadInt32(address + 3) != flagsOffset.Value ||
            Marshal.ReadByte(address + 7) != 0xFF || Marshal.ReadByte(address + 8) != (postEffects ? 0x75 : 0x0F))
            throw new NativeOperationException("The current dispatch comparison is incompatible.");
        return address;
    }

    private static void RequireCurrentInstruction(nint address, string signature, byte? compareByte = null)
    {
        var bytes = signature.Split(' ');
        if (address < Plugin.SigScanner.TextSectionBase || address + bytes.Length > Plugin.SigScanner.TextSectionBase + Plugin.SigScanner.TextSectionSize)
            throw new NativeOperationException("The native instruction is outside the game's code section.");
        for (var i = 0; i < bytes.Length; i++)
            if (bytes[i] != "??" && Marshal.ReadByte(address + i) != (i == 7 && compareByte.HasValue ? compareByte.Value : Convert.ToByte(bytes[i], 16)))
                throw new NativeOperationException("The native bytes are already changed or incompatible.");
    }

    private void EvictWorkingSet()
    {
        if (!EmptyWorkingSet(GetCurrentProcess()))
            throw new NativeOperationException("EmptyWorkingSet failed.", $"Win32 {Marshal.GetLastWin32Error()}");
        LastWorkingSetEviction = DateTimeOffset.UtcNow;
        nextWorkingSetTick = Environment.TickCount64 + 60_000;
    }

    private static void WriteCodeByte(nint address, byte value)
    {
        if (!VirtualProtect(address, 1, 0x40, out var protection))
            throw new NativeOperationException("VirtualProtect failed.", $"Win32 {Marshal.GetLastWin32Error()}");
        try
        {
            Marshal.WriteByte(address, value);
            if (!FlushInstructionCache(GetCurrentProcess(), address, 1))
                throw new NativeOperationException("FlushInstructionCache failed.", $"Win32 {Marshal.GetLastWin32Error()}");
        }
        finally
        {
            if (!VirtualProtect(address, 1, protection, out _))
                throw new NativeOperationException("Restoring instruction protection failed.", $"Win32 {Marshal.GetLastWin32Error()}");
        }
    }

    public void Dispose()
    {
        disposed = true;
        MasterRequested = false;
        requested = RenderTrimOption.None;
        ReleaseOwned(owned, force: true);
        foreach (var (option, hook) in hooks)
        {
            try { hook.Dispose(); }
            catch (Exception ex)
            {
                RecordFailure(option, ex);
                Plugin.Log.Warning(ex, "[DPS][RenderTrim] Could not dispose {Trim}.", option);
            }
        }
        hooks.Clear();
    }

    private void RecordFailure(RenderTrimOption option, Exception exception)
        => failures[option] = exception is NativeOperationException native
            ? new(native.Message, native.Detail)
            : new("The native operation could not be completed.", $"{exception.GetType().Name}: {exception.Message}");

    private sealed class NativeOperationException(string reason, string? detail = null) : Exception(reason)
    {
        public string? Detail { get; } = detail;
    }

    [DllImport("psapi.dll", SetLastError = true)] private static extern bool EmptyWorkingSet(nint process);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool VirtualProtect(nint address, nuint size, uint protection, out uint previous);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool FlushInstructionCache(nint process, nint address, nuint size);
}
