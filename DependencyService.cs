using Dalamud.Plugin;
using Dalamud.Plugin.Internal.Types;
using Dalamud.Interface;
using Dalamud.Utility;
using ECommons.Reflection;

namespace VieriAutoMarket;

internal sealed class DependencyService
{
    internal const string MarketbuddyName = "Marketbuddy";
    internal const string AllaganMarketName = "AllaganMarket";
    internal const string PunishRepository = "https://love.puni.sh/ment.json";
    internal const string OfficialRepository = "https://kamori.goats.dev/Plugin/PluginMaster";

    private readonly IDalamudPluginInterface pi;
    private readonly Dalamud.Plugin.Services.IPluginLog log;
    private readonly HashSet<string> installing = new(StringComparer.OrdinalIgnoreCase);

    internal DependencyService(IDalamudPluginInterface pi, Dalamud.Plugin.Services.IPluginLog log)
    {
        this.pi = pi;
        this.log = log;
    }

    internal bool IsLoaded(string internalName) => pi.InstalledPlugins.Any(plugin =>
        plugin.IsLoaded && string.Equals(plugin.InternalName, internalName, StringComparison.OrdinalIgnoreCase));

    internal bool IsInstalled(string internalName) => pi.InstalledPlugins.Any(plugin =>
        string.Equals(plugin.InternalName, internalName, StringComparison.OrdinalIgnoreCase));

    internal bool IsInstalling(string internalName) => installing.Contains(internalName);

    internal void OpenInstaller(string displayName, bool installed) =>
        pi.OpenPluginInstallerTo(installed ? PluginInstallerOpenKind.InstalledPlugins : PluginInstallerOpenKind.AllPlugins,
            displayName);

    internal Task InstallMarketbuddyAsync() => InstallAsync(MarketbuddyName, "Marketbuddy", PunishRepository);
    internal Task InstallAllaganMarketAsync() => InstallAsync(AllaganMarketName, "Allagan Market", OfficialRepository);

    private async Task InstallAsync(string internalName, string displayName, string repositoryUrl)
    {
        if (IsInstalled(internalName))
        {
            OpenInstaller(displayName, true);
            return;
        }

        if (!installing.Add(internalName))
            return;

        try
        {
            var manifests = await DalamudReflector.GetPluginMaster(repositoryUrl).ConfigureAwait(false);
            object? manifest = manifests?.FirstOrDefault(candidate =>
                string.Equals((string?)candidate.GetFoP("InternalName"), internalName, StringComparison.Ordinal));
            if (manifest == null)
                throw new InvalidOperationException($"{displayName} was not found in its Dalamud repository.");

            if (!repositoryUrl.Equals(OfficialRepository, StringComparison.Ordinal) &&
                !DalamudReflector.HasRepo(repositoryUrl))
            {
                DalamudReflector.AddRepo(repositoryUrl, enabled: true);
                DalamudReflector.SaveDalamudConfig();
                DalamudReflector.ReloadPluginMasters();
            }

            object pluginManager = DalamudReflector.GetPluginManager();
            MethodInfo? method = pluginManager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(x => x.Name == "InstallPluginAsync")
                .OrderBy(x => x.GetParameters().Length)
                .FirstOrDefault();
            if (method == null)
                throw new MissingMethodException("Dalamud's plugin installer is unavailable.");

            object?[] arguments = BindInstallerArguments(method, manifest);
            if (method.Invoke(pluginManager, arguments) is not Task task)
                throw new InvalidOperationException("Dalamud did not start the plugin installation.");

            await task.ConfigureAwait(false);
            object? installed = task.GetFoP("Result");
            if (installed == null || installed.GetFoP("IsLoaded") is not true)
                throw new InvalidOperationException($"{displayName} installed but did not load.");

            Plugin.Chat.Print($"Installed {displayName}.", Plugin.Tag);
        }
        catch (Exception ex)
        {
            log.Error(ex, "Failed to install {Dependency}", displayName);
            Plugin.Chat.PrintError($"Could not install {displayName}. The Dalamud installer has been opened.", Plugin.Tag);
            OpenInstaller(displayName, false);
        }
        finally
        {
            installing.Remove(internalName);
        }
    }

    private static object?[] BindInstallerArguments(MethodInfo method, object manifest)
    {
        ParameterInfo[] parameters = method.GetParameters();
        var arguments = new object?[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            arguments[i] = parameters[i].Name switch
            {
                "repoManifest" => manifest,
                "useTesting" => false,
                "reason" => PluginLoadReason.Installer,
                _ => parameters[i].HasDefaultValue
                    ? parameters[i].DefaultValue
                    : parameters[i].ParameterType.IsValueType
                        ? Activator.CreateInstance(parameters[i].ParameterType)
                        : null,
            };
        }

        return arguments;
    }
}
