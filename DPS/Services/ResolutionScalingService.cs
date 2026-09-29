using System.Collections;
using System.Globalization;
using System.Reflection;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;

namespace DPS.Services;

public sealed class ResolutionScalingService(Configuration configuration)
{
    private const string SlaveRepository = "https://aethertek.io/x.json";
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private bool slaveRequestEnabled;
    private float customGameplayScale = 0.05f;

    public string Status { get; private set; } = string.Empty;
    public bool Installing { get; private set; }
    public bool SlaveRequestEnabled => slaveRequestEnabled;
    public bool HasProvider => configuration.ResolutionProvider is ResolutionProvider.XASlave or ResolutionProvider.CustomResolution;
    public string ProviderName => configuration.ResolutionProvider == ResolutionProvider.XASlave ? "XA Slave" : "Custom Resolution";
    private string InternalName => configuration.ResolutionProvider == ResolutionProvider.XASlave ? "XASlave" : "CustomResolution2782";

    public IExposedPlugin? FindInstallation()
        => HasProvider ? Plugin.PluginInterface.InstalledPlugins
            .Where(plugin => plugin.InternalName.Equals(InternalName, StringComparison.Ordinal))
            .OrderByDescending(plugin => plugin.IsLoaded).FirstOrDefault() : null;

    public bool Ready => FindInstallation() is { IsLoaded: true, IsOutdated: false, IsBanned: false, IsDecommissioned: false };

    public void Toggle()
    {
        try
        {
            RequireReady();
            var enabled = slaveRequestEnabled;
            if (configuration.ResolutionProvider == ResolutionProvider.CustomResolution)
            {
                var game = Read(Read(ReadCustomConfiguration(), "V1"), "Game");
                enabled = (bool)Read(game, "IsEnabled") && (float)Read(game, "Scale") != 1f;
            }
            Apply(!enabled);
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
    }

    public void Apply(bool enabled)
    {
        try
        {
            RequireReady();
            if (configuration.ResolutionProvider == ResolutionProvider.XASlave)
            {
                if (enabled && (!float.IsFinite(configuration.ResolutionScale) || configuration.ResolutionScale is < 0.01f or > 1f))
                    throw new InvalidOperationException("Choose a resolution scale between 0.01 and 1.00.");
                var command = enabled ? $"lowres {configuration.ResolutionScale.ToString("0.00", CultureInfo.InvariantCulture)}" : "lowres off";
                var response = Plugin.PluginInterface.GetIpcSubscriber<string, string>("XASlave.ExecuteCommand").InvokeFunc(command);
                if (response == null || !(response.Equals("OK", StringComparison.Ordinal) || response.StartsWith("OK:", StringComparison.Ordinal)))
                    throw new InvalidOperationException(response ?? "XA Slave returned no response.");
                slaveRequestEnabled = enabled;
                Status = response;
                return;
            }

            var config = ReadCustomConfiguration();
            var version = Read(config, "V1");
            var applyDefaults = enabled && !configuration.CustomResolutionDefaultsApplied;
            if (applyDefaults)
                ApplyCustomDefaults(version);

            var game = Read(version, "Game");
            RememberCustomGameplayScale(game);
            // Keep the native override enabled so Off forces 1.0 instead of restoring the game's own scale.
            Write(game, "IsEnabled", true);
            Write(game, "IsScale", true);
            Write(game, "Scale", enabled ? customGameplayScale : 1f);
            Write(version, "Game", game);
            Write(config, "V1", version);
            config.GetType().GetMethod("Save", Instance)!.Invoke(config, null);
            if (applyDefaults)
            {
                configuration.CustomResolutionDefaultsApplied = true;
                configuration.Save();
            }
            Status = enabled ? $"Custom Resolution gameplay scale set to {customGameplayScale:0.###}x." : "Custom Resolution gameplay scale set to 1.0x.";
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
    }

    public void DrawCustomSettings()
    {
        try
        {
            RequireReady();
            var config = ReadCustomConfiguration();
            var ui = config.GetType().Assembly.GetType("CustomResolution.Service", true)!
                .GetProperty("PluginUI", Static)!.GetValue(null)!;
            // Reuse the provider's draft and Save and apply behavior without resetting edits each frame.
            ((Window)Read(ui, "_configWindow")).Draw();
            RememberCustomGameplayScale(Read(Read(config, "V1"), "Game"));
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
    }

    private void RememberCustomGameplayScale(object game)
    {
        var scale = (float)Read(game, "Scale");
        if (float.IsFinite(scale) && scale > 0f && scale != 1f)
            customGameplayScale = scale;
    }

    private static void ApplyCustomDefaults(object version)
    {
        var display = Read(version, "Display");
        SetCustomSizeDefaults(display, false, 1f);
        Write(version, "Display", display);
        var game = Read(version, "Game");
        SetCustomSizeDefaults(game, true, 0.05f);
        Write(version, "Game", game);
        Write(version, "ResolutionScalingMode", Enum.Parse(Read(version, "ResolutionScalingMode").GetType(), "Point"));
        Write(version, "DisplayScalingMode", Enum.Parse(Read(version, "DisplayScalingMode").GetType(), "WindowSize"));
        Write(version, "DXVKDWMHackMode", Enum.Parse(Read(version, "DXVKDWMHackMode").GetType(), "Off"));
        Write(version, "MinSizeMode", Enum.Parse(Read(version, "MinSizeMode").GetType(), "Unchanged"));
        Write(version, "FixFullscreen", true);
        Write(version, "DisableOnHang", true);
    }

    private static void SetCustomSizeDefaults(object size, bool enabled, float scale)
    {
        Write(size, "IsEnabled", enabled);
        Write(size, "IsScale", true);
        Write(size, "Scale", scale);
        Write(size, "Width", 1024u);
        Write(size, "Height", 1024u);
        Write(size, "HotkeyKey", Enum.ToObject(Read(size, "HotkeyKey").GetType(), 0));
        Write(size, "HotkeyModifier", Enum.ToObject(Read(size, "HotkeyModifier").GetType(), 0));
    }

    public bool SlaveRepositoryEnabled()
    {
        try
        {
            return FindSlaveRepository(GetService("Dalamud.Configuration.Internal.DalamudConfiguration")) is { } repo
                && (bool)Read(repo, "IsEnabled");
        }
        catch (Exception ex)
        {
            SetError(ex);
            return false;
        }
    }

    public async Task InstallAsync()
    {
        if (!HasProvider || Installing || FindInstallation() != null)
            return;
        var name = InternalName;
        var provider = configuration.ResolutionProvider;
        Installing = true;
        Status = $"Installing {ProviderName}...";
        try
        {
            var manager = GetService("Dalamud.Plugin.Internal.PluginManager");
            if (provider == ResolutionProvider.XASlave)
            {
                var config = GetService("Dalamud.Configuration.Internal.DalamudConfiguration");
                var repo = FindSlaveRepository(config);
                if (repo == null)
                {
                    repo = Activator.CreateInstance(config.GetType().Assembly.GetType("Dalamud.Configuration.ThirdPartyRepoSettings", true)!)!;
                    Write(repo, "Url", SlaveRepository);
                    ((IList)Read(config, "ThirdRepoList")).Add(repo);
                }
                Write(repo, "IsEnabled", true);
                config.GetType().GetMethod("QueueSave", Instance)!.Invoke(config, null);
            }

            await (Task)manager.GetType().GetMethod("SetPluginReposFromConfigAsync", Instance)!.Invoke(manager, [true])!;
            var manifest = ((IEnumerable)Read(manager, "AvailablePlugins")).Cast<object>().Single(candidate =>
                (string)Read(candidate, "InternalName") == name &&
                (provider == ResolutionProvider.CustomResolution
                    ? !(bool)Read(Read(candidate, "SourceRepo"), "IsThirdParty")
                    : SameUrl((string)Read(Read(candidate, "SourceRepo"), "PluginMasterUrl"), SlaveRepository)));
            var installMethod = manager.GetType().GetMethods(Instance)
                .Single(method => method.Name == "InstallPluginAsync" && method.GetParameters().Length == 3);
            var task = (Task)installMethod.Invoke(manager, [manifest, false, Enum.Parse(installMethod.GetParameters()[2].ParameterType, "Installer")])!;
            await task;
            var installed = Read(task, "Result");
            Status = (bool)Read(installed, "IsLoaded") ? $"Installed and activated {name}." : $"Installed {name}; enable it in the Plugin Installer.";
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            Installing = false;
        }
    }

    private void RequireReady()
    {
        if (!HasProvider || Installing || !Ready)
            throw new InvalidOperationException("Select a loaded, compatible resolution plugin first.");
    }

    private object ReadCustomConfiguration()
    {
        var installation = Plugin.PluginInterface.InstalledPlugins.Single(plugin => plugin.InternalName == "CustomResolution2782" && plugin.IsLoaded);
        foreach (var field in installation.GetType().GetFields(Instance))
        {
            var localPlugin = field.GetValue(installation);
            if (localPlugin == null)
                continue;
            for (var type = localPlugin.GetType(); type != null; type = type.BaseType)
            {
                var instance = type.GetField("instance", Instance)?.GetValue(localPlugin);
                if (instance?.GetType().FullName == "CustomResolution.Plugin")
                    return instance.GetType().Assembly.GetType("CustomResolution.Service", true)!
                        .GetProperty("Config", Static)!.GetValue(null)!;
            }
        }
        throw new InvalidOperationException("Could not access the loaded Custom Resolution configuration.");
    }

    private static object GetService(string name)
    {
        var assembly = typeof(IDalamudPluginInterface).Assembly;
        return assembly.GetType("Dalamud.Service`1", true)!.MakeGenericType(assembly.GetType(name, true)!)
            .GetMethod("Get", Static)!.Invoke(null, null)!;
    }

    private static object? FindSlaveRepository(object config)
        => ((IEnumerable)Read(config, "ThirdRepoList")).Cast<object>()
            .FirstOrDefault(repo => SameUrl((string)Read(repo, "Url"), SlaveRepository));

    private static bool SameUrl(string left, string right)
        => left.Trim().TrimEnd('/').Equals(right.Trim().TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static object Read(object target, string name)
        => target.GetType().GetProperty(name, Instance)?.GetValue(target)
            ?? target.GetType().GetField(name, Instance)?.GetValue(target)
            ?? throw new MissingMemberException(target.GetType().FullName, name);

    private static void Write(object target, string name, object value)
    {
        if (target.GetType().GetProperty(name, Instance) is { } property)
            property.SetValue(target, value);
        else if (target.GetType().GetField(name, Instance) is { } field)
            field.SetValue(target, value);
        else
            throw new MissingMemberException(target.GetType().FullName, name);
    }

    private void SetError(Exception exception)
    {
        var message = $"Resolution scaling: {exception.GetBaseException().Message}";
        if (Status == message)
            return;
        Status = message;
        Plugin.Log.Warning(Status);
    }
}
