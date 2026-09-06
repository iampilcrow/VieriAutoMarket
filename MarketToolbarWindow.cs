using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
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
        ui.ApplyOwnershipAwareHighlighting(config.MarketAssessments);

        if (automation.IsRunning)
        {
            if (IconButton(FontAwesomeIcon.Times, "Stop", "Stop the current VieriAutoMarket operation"))
                automation.Stop();
            ImGui.SameLine();
            ImGui.TextUnformatted(automation.Total > 0 ? $"{automation.CurrentNumber}/{automation.Total}" : "Working");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(automation.Status);
            return;
        }

        if (IconButton(FontAwesomeIcon.Search, "Check", "Check For Undercuts\nRefresh listings and distinguish external sellers from your own retainers"))
            automation.Start(AutomationMode.Check);
        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Edit, "Adjust", "Adjust Undercut Pricing\nVerify every listing, match a market-lowest owned retainer exactly, or undercut the cheapest external seller"))
            automation.Start(AutomationMode.Adjust);
        ImGui.SameLine();
        if (IconButton(FontAwesomeIcon.Play, "Auto", "Auto Check/Adjust Undercuts\nRefresh all listings, then reprice external undercuts and owned-retainer price mismatches"))
            automation.Start(AutomationMode.CheckAndAdjust);
    }

    private static bool IconButton(FontAwesomeIcon icon, string id, string tooltip)
    {
        bool clicked;
        using (Plugin.Pi.UiBuilder.IconFontFixedWidthHandle.Push())
            clicked = ImGui.Button($"{icon.ToIconString()}##VieriAutoMarket{id}", new Vector2(26, 0));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
        return clicked;
    }
}
