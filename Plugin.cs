using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;

namespace VieriAutoMarket;

public sealed class Plugin : IDalamudPlugin
{
    internal const string Tag = "VieriAutoMarket";

    [PluginService] internal static IDalamudPluginInterface Pi { get; private set; } = null!;
    [PluginService] internal static ICommandManager Commands { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui Chat { get; private set; } = null!;
    [PluginService] internal static IToastGui Toasts { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windows = new(Tag);
    private readonly Configuration config;
    private readonly MarketAutomationController automation;
    private readonly SettingsWindow settings;

    public Plugin()
    {
        ECommonsMain.Init(Pi, this, ECommons.Module.DalamudReflector);
        config = Pi.GetPluginConfig() as Configuration ?? new Configuration();
        config.Initialize(Pi);
        config.ApplyMigrations();

        var dependencies = new DependencyService(Pi, Log);
        var marketUi = new RetainerMarketUi(GameGui);
        automation = new MarketAutomationController(Framework, Chat, Toasts, Log, Pi, config, dependencies, marketUi);
        settings = new SettingsWindow(config, dependencies, automation);
        windows.AddWindow(settings);
        windows.AddWindow(new MarketToolbarWindow(config, marketUi, automation));

        Commands.AddHandler("/vamarket", new CommandInfo(OnCommand)
        {
            HelpMessage = "Open VieriAutoMarket settings. Use /vamarket check, adjust, auto, or stop for direct control.",
        });
        Pi.UiBuilder.Draw += Draw;
        Pi.UiBuilder.OpenConfigUi += OpenSettings;
        Pi.UiBuilder.OpenMainUi += OpenSettings;
    }

    private void OnCommand(string _, string arguments)
    {
        switch (arguments.Trim().ToLowerInvariant())
        {
            case "check": automation.Start(AutomationMode.Check); break;
            case "adjust": automation.Start(AutomationMode.Adjust); break;
            case "auto": automation.Start(AutomationMode.CheckAndAdjust); break;
            case "stop": automation.Stop(); break;
            default: settings.Toggle(); break;
        }
    }

    private void OpenSettings() => settings.IsOpen = true;
    private void Draw() => windows.Draw();

    public void Dispose()
    {
        Pi.UiBuilder.Draw -= Draw;
        Pi.UiBuilder.OpenConfigUi -= OpenSettings;
        Pi.UiBuilder.OpenMainUi -= OpenSettings;
        Commands.RemoveHandler("/vamarket");
        automation.Dispose();
        windows.RemoveAllWindows();
        config.Save();
        ECommonsMain.Dispose();
    }
}
