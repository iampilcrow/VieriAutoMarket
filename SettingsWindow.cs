using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace VieriAutoMarket;

internal sealed class SettingsWindow : Window
{
    private readonly Configuration config;
    private readonly DependencyService dependencies;
    private readonly MarketAutomationController automation;

    internal SettingsWindow(Configuration config, DependencyService dependencies, MarketAutomationController automation)
        : base("VieriAutoMarket", ImGuiWindowFlags.AlwaysAutoResize)
    {
        this.config = config;
        this.dependencies = dependencies;
        this.automation = automation;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(620, 320),
            MaximumSize = new Vector2(1200, 900),
        };
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Dependencies");
        ImGui.Separator();
        DrawDependency("Marketbuddy", DependencyService.MarketbuddyName, dependencies.InstallMarketbuddyAsync);
        DrawDependency("Allagan Market", DependencyService.AllaganMarketName, dependencies.InstallAllaganMarketAsync);

        ImGui.Spacing();
        ImGui.TextWrapped("Marketbuddy controls the undercut amount, percentage and rounding. Allagan Market must have retainer-list highlighting enabled so red listings can be identified.");

        ImGui.Spacing();
        ImGui.TextUnformatted("Toolbar placement");
        ImGui.Separator();
        float x = config.ToolbarOffsetX;
        if (ImGui.DragFloat("Horizontal offset", ref x, 1f, 0f, 1200f, "%.0f px"))
        {
            config.ToolbarOffsetX = x;
            config.Save();
        }
        float y = config.ToolbarOffsetY;
        if (ImGui.DragFloat("Vertical offset", ref y, 1f, -100f, 300f, "%.0f px"))
        {
            config.ToolbarOffsetY = y;
            config.Save();
        }

        int delay = config.ActionDelayMilliseconds;
        if (ImGui.SliderInt("Step delay", ref delay, 75, 800, "%d ms"))
        {
            config.ActionDelayMilliseconds = delay;
            config.Save();
        }

        bool chat = config.PrintCompletionToChat;
        if (ImGui.Checkbox("Print completion summaries in chat", ref chat))
        {
            config.PrintCompletionToChat = chat;
            config.Save();
        }

        ImGui.Spacing();
        ImGui.TextWrapped($"Status: {automation.Status}");
        if (automation.IsRunning && ImGui.Button("Stop current operation"))
            automation.Stop();

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Last run report", ImGuiTreeNodeFlags.DefaultOpen))
            DrawLastRunReport();
    }

    private void DrawLastRunReport()
    {
        ImGui.TextWrapped(config.LastRunSummary);
        if (config.LastRunAt != default)
            ImGui.TextDisabled(config.LastRunAt.ToString("g"));
        if (config.LastRunReport.Count == 0)
        {
            ImGui.TextDisabled("No listing details were recorded for the last run.");
            return;
        }

        ImGuiTableFlags flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg |
                                ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollX |
                                ImGuiTableFlags.ScrollY;
        if (!ImGui.BeginTable("VieriAutoMarketLastRun", 8, flags, new Vector2(0, 260)))
            return;

        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthFixed, 190);
        ImGui.TableSetupColumn("Retainer", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Quality", ImGuiTableColumnFlags.WidthFixed, 55);
        ImGui.TableSetupColumn("Old", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Competitor", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("External price", ImGuiTableColumnFlags.WidthFixed, 90);
        ImGui.TableSetupColumn("Final", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Outcome", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();

        foreach (MarketRunReportEntry entry in config.LastRunReport)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn(); ImGui.TextUnformatted(entry.Item);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(entry.Retainer);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(entry.Quality);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(FormatPrice(entry.OldPrice));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(string.IsNullOrWhiteSpace(entry.Competitor) ? "—" : entry.Competitor);
            ImGui.TableNextColumn(); ImGui.TextUnformatted(FormatPrice(entry.CompetitorPrice));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(FormatPrice(entry.FinalPrice));
            ImGui.TableNextColumn(); ImGui.TextUnformatted(entry.Outcome);
        }

        ImGui.EndTable();
    }

    private static string FormatPrice(uint value) => value == 0 ? "—" : $"{value:N0}";

    private void DrawDependency(string displayName, string internalName, Func<Task> install)
    {
        bool loaded = dependencies.IsLoaded(internalName);
        bool installed = dependencies.IsInstalled(internalName);
        Vector4 color = loaded ? new Vector4(0.25f, 0.85f, 0.35f, 1f) :
            installed ? new Vector4(0.95f, 0.72f, 0.2f, 1f) : new Vector4(0.95f, 0.35f, 0.3f, 1f);
        ImGui.TextColored(color, loaded ? "Loaded" : installed ? "Installed, not loaded" : "Not installed");
        ImGui.SameLine(155);
        ImGui.TextUnformatted(displayName);
        ImGui.SameLine(320);

        if (!installed)
        {
            bool busy = dependencies.IsInstalling(internalName);
            if (busy) ImGui.BeginDisabled();
            if (ImGui.Button(busy ? $"Installing##{internalName}" : $"Install##{internalName}"))
                _ = install();
            if (busy) ImGui.EndDisabled();
        }
        else if (ImGui.Button($"Open in installer##{internalName}"))
        {
            dependencies.OpenInstaller(displayName, true);
        }
    }
}
