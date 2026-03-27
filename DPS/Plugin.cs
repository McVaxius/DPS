using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using DPS.Services;
using DPS.Windows;

namespace DPS;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    public static Plugin PluginInstance { get; private set; } = null!;
    public Configuration Configuration { get; }
    public ActorSuppressionService ActorSuppressionService { get; }

    public WindowSystem WindowSystem { get; } = new(PluginInfo.InternalName);
    private readonly MainWindow mainWindow;
    private readonly ConfigWindow configWindow;
    private IDtrBarEntry? dtrEntry;

    public Plugin()
    {
        PluginInstance = this;
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ActorSuppressionService = new ActorSuppressionService();

        mainWindow = new MainWindow(this);
        configWindow = new ConfigWindow(this);
        WindowSystem.AddWindow(mainWindow);
        WindowSystem.AddWindow(configWindow);

        CommandManager.AddHandler(PluginInfo.Command, new CommandInfo(OnCommand)
        {
            HelpMessage = $"Open {PluginInfo.DisplayName}.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
        Framework.Update += OnFrameworkUpdate;

        SetupDtrBar();
        UpdateDtrBar();
        Log.Information("[DPS] Plugin loaded.");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        ActorSuppressionService.ShowAll();
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        CommandManager.RemoveHandler(PluginInfo.Command);
        WindowSystem.RemoveAllWindows();
        dtrEntry?.Remove();
    }

    public void ToggleMainUi() => mainWindow.Toggle();
    public void ToggleConfigUi() => configWindow.Toggle();

    public void UpdateDtrBar()
    {
        if (dtrEntry == null)
            return;

        dtrEntry.Shown = Configuration.DtrBarEnabled;
        if (!Configuration.DtrBarEnabled)
            return;

        var icon = Configuration.PluginEnabled ? Configuration.DtrIconEnabled : Configuration.DtrIconDisabled;
        var status = Configuration.PluginEnabled ? "On" : "Off";
        dtrEntry.Text = Configuration.DtrBarMode switch
        {
            1 => new SeString(new TextPayload($"{icon} DPS")),
            2 => new SeString(new TextPayload(icon)),
            _ => new SeString(new TextPayload($"DPS: {status}")),
        };
        dtrEntry.Tooltip = new SeString(new TextPayload($"{PluginInfo.DisplayName} {status}. Click to toggle."));
    }

    private void SetupDtrBar()
    {
        dtrEntry = DtrBar.Get(PluginInfo.DisplayName);
        dtrEntry.OnClick = _ =>
        {
            Configuration.PluginEnabled = !Configuration.PluginEnabled;
            Configuration.Save();
            UpdateDtrBar();
            if (!Configuration.PluginEnabled)
                ActorSuppressionService.ShowAll();
        };
    }

    private void OnCommand(string command, string arguments)
    {
        ToggleMainUi();
    }

    private void OnFrameworkUpdate(IFramework framework)
    {
        ActorSuppressionService.Update(Configuration);
        UpdateDtrBar();
    }
}
