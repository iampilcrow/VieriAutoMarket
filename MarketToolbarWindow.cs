using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace VieriAutoMarket;

internal sealed unsafe class MarketToolbarWindow : Window
{
    private readonly Configuration config;
    private readonly RetainerMarketUi ui;
    private readonly MarketAutomationController automation;

    internal MarketToolbarWindow(Configuration config, RetainerMarketUi ui, MarketAutomationController automation)
        : base("VieriAutoMarket Toolbar###VieriAutoMarketToolbar",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoSavedSettings)
    {
        this.config = config;
        this.ui = ui;
        this.automation = automation;
        RespectCloseHotkey = false;
        IsOpen = true;
    }

    public override bool DrawConditions() => ui.IsReady("RetainerSellList");

    public override void PreDraw()
    {
        AtkUnitBase* addon = ui.GetAddon("RetainerSellList");
        if (addon != null)
        {
            float scale = addon->Scale <= 0 ? 1f : addon->Scale;
            Position = new Vector2(addon->X + config.ToolbarOffsetX * scale, addon->Y + config.ToolbarOffsetY * scale);
            PositionCondition = ImGuiCond.Always;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 1));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(3, 0));
    }

    public override void PostDraw() => ImGui.PopStyleVar(3);

    public override void Draw()
    {
        if (automation.IsRunning)
        {
            if (ImGui.Button("Stop"))
                automation.Stop();
            ImGui.SameLine();
            ImGui.TextUnformatted(automation.Total > 0
                ? $"{automation.Status} ({automation.CurrentNumber}/{automation.Total})"
                : automation.Status);
            return;
        }

        if (ImGui.Button("Check For Undercuts"))
            automation.Start(AutomationMode.Check);
        ImGui.SameLine();
        if (ImGui.Button("Adjust Undercut Pricing"))
            automation.Start(AutomationMode.Adjust);
        ImGui.SameLine();
        if (ImGui.Button("Auto Check/Adjust Undercuts"))
            automation.Start(AutomationMode.CheckAndAdjust);
    }
}
