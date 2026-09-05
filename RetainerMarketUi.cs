using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Colors;
using Dalamud.Plugin.Services;
using ECommons.Automation;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace VieriAutoMarket;

internal sealed unsafe class RetainerMarketUi
{
    private readonly IGameGui gameGui;

    internal RetainerMarketUi(IGameGui gameGui) => this.gameGui = gameGui;

    internal AtkUnitBase* GetAddon(string name)
    {
        nint pointer = gameGui.GetAddonByName(name, 1);
        return pointer == nint.Zero ? null : (AtkUnitBase*)pointer;
    }

    internal bool IsReady(string name)
    {
        AtkUnitBase* addon = GetAddon(name);
        return addon != null && addon->IsVisible && addon->UldManager.LoadedState == AtkLoadState.Loaded;
    }

    internal int GetListingCount()
    {
        InventoryManager* inventory = InventoryManager.Instance();
        InventoryContainer* container = inventory == null ? null : inventory->GetInventoryContainer(InventoryType.RetainerMarket);
        if (container == null || !container->IsLoaded)
            return 0;

        int count = 0;
        for (int i = 0; i < Math.Min(container->Size, 20); i++)
        {
            InventoryItem* item = container->GetInventorySlot(i);
            if (item != null && item->ItemId != 0)
                count++;
        }

        return count;
    }

    internal bool SelectListing(int visualIndex)
    {
        AtkUnitBase* addon = GetAddon("RetainerSellList");
        if (addon == null || !addon->IsVisible)
            return false;

        Callback.Fire(addon, true, 0, visualIndex, 1);
        return true;
    }

    internal bool ShowListingRow(int visualIndex)
    {
        AtkUnitBase* addon = GetAddon("RetainerSellList");
        AtkComponentList* list = addon == null ? null : addon->GetComponentListById(11);
        if (list == null || visualIndex < 0 || visualIndex >= list->GetItemCount())
            return false;

        list->ScrollToItem((short)visualIndex);
        list->UpdateListItems();
        return true;
    }

    internal bool SelectAdjustPrice()
    {
        AtkUnitBase* addon = GetAddon("ContextMenu");
        if (addon == null || !addon->IsVisible)
            return false;

        var menu = new AddonMaster.ContextMenu(addon);
        AddonMaster.ContextMenu.Entry[] matches = menu.Entries
            .Where(x => x.Enabled && IsAdjustPriceEntry(x.Text))
            .Take(1)
            .ToArray();
        if (matches.Length == 0)
            return false;
        return matches[0].Select();
    }

    internal bool HasAdjustPriceEntry()
    {
        AtkUnitBase* addon = GetAddon("ContextMenu");
        if (addon == null || !addon->IsVisible)
            return false;

        var menu = new AddonMaster.ContextMenu(addon);
        return menu.Entries.Any(x => x.Enabled && IsAdjustPriceEntry(x.Text));
    }

    private static bool IsAdjustPriceEntry(string text)
    {
        string normalized = text.Trim();
        return normalized.Contains("Adjust Price", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("価格を変更", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("Preis ändern", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("Modifier le prix", StringComparison.OrdinalIgnoreCase);
    }

    internal MarketResultsState GetMarketResultsState()
    {
        AddonItemSearchResult* addon = (AddonItemSearchResult*)GetAddon("ItemSearchResult");
        if (addon == null || !addon->IsVisible || addon->Results == null)
            return MarketResultsState.Waiting;

        AgentModule* agentModule = AgentModule.Instance();
        AgentItemSearch* agent = agentModule == null
            ? null
            : (AgentItemSearch*)agentModule->GetAgentByInternalId(AgentId.ItemSearch);
        if (agent == null || agent->InfoProxyItemSearch == null)
            return MarketResultsState.Waiting;

        var search = agent->InfoProxyItemSearch;
        uint selectedItemId = GetSelectedMarketItemId();
        if (search->WaitingForListings || selectedItemId == 0 || search->SearchItemId != selectedItemId)
            return MarketResultsState.Waiting;

        int resultCount = addon->Results->GetItemCount();
        if (resultCount <= 0)
            return search->ListingCount == 0
                ? MarketResultsState.ReadyWithoutListings
                : MarketResultsState.Waiting;

        AtkComponentListItemRenderer* first = addon->Results->GetItemRenderer(0);
        if (first == null)
            return MarketResultsState.Waiting;

        AtkTextNode* price = first->GetTextNodeById(5);
        return price != null && !string.IsNullOrWhiteSpace(price->NodeText.ToString())
            ? MarketResultsState.ReadyWithListings
            : MarketResultsState.Waiting;
    }

    private static uint GetSelectedMarketItemId()
    {
        InventoryManager* inventory = InventoryManager.Instance();
        InventoryContainer* blocked = inventory == null
            ? null
            : inventory->GetInventoryContainer(InventoryType.BlockedItems);
        InventoryItem* selected = blocked == null || !blocked->IsLoaded ? null : blocked->GetInventorySlot(0);
        return selected == null ? 0 : selected->ItemId;
    }

    internal bool ClickBestMarketListing()
    {
        AddonItemSearchResult* addon = (AddonItemSearchResult*)GetAddon("ItemSearchResult");
        if (addon == null || !addon->IsVisible || addon->Results == null || addon->Results->GetItemCount() <= 0)
            return false;

        addon->Results->DispatchItemEvent(0, AtkEventType.ListItemClick);
        return true;
    }

    internal bool Close(string name)
    {
        AtkUnitBase* addon = GetAddon(name);
        if (addon == null || !addon->IsVisible)
            return false;
        addon->Close(true);
        return true;
    }

    internal bool IsUndercutRow(int visualIndex) =>
        GetListingPriceState(visualIndex) == ListingPriceState.Undercut;

    internal ListingPriceState GetListingPriceState(int visualIndex)
    {
        AtkUnitBase* addon = GetAddon("RetainerSellList");
        if (addon == null || !addon->IsVisible)
            return ListingPriceState.Unknown;

        AtkComponentList* list = addon->GetComponentListById(11);
        if (list == null)
            return ListingPriceState.Unknown;

        for (int i = 0; i < list->ListLength; i++)
        {
            AtkComponentListItemRenderer* renderer = list->ItemRendererList[i].AtkComponentListItemRenderer;
            if (renderer == null || renderer->ListItemIndex != visualIndex)
                continue;

            AtkTextNode* text = renderer->GetTextNodeById(3);
            if (text == null)
                return ListingPriceState.Unknown;
            if (IsAllaganUndercutColor(text->TextColor.R, text->TextColor.G, text->TextColor.B))
                return ListingPriceState.Undercut;
            if (IsAllaganNeedsCheckColor(text->TextColor.R, text->TextColor.G, text->TextColor.B))
                return ListingPriceState.NeedsCheck;
            return ListingPriceState.Current;
        }

        return ListingPriceState.Unknown;
    }

    internal int[] GetVisibleUndercutRows()
    {
        AtkUnitBase* addon = GetAddon("RetainerSellList");
        AtkComponentList* list = addon == null ? null : addon->GetComponentListById(11);
        if (list == null)
            return [];

        var rows = new HashSet<int>();
        for (int i = 0; i < list->ListLength; i++)
        {
            AtkComponentListItemRenderer* renderer = list->ItemRendererList[i].AtkComponentListItemRenderer;
            if (renderer == null || renderer->ListItemIndex < 0)
                continue;
            AtkTextNode* text = renderer->GetTextNodeById(3);
            if (text != null && IsAllaganUndercutColor(text->TextColor.R, text->TextColor.G, text->TextColor.B))
                rows.Add(renderer->ListItemIndex);
        }

        return rows.Order().ToArray();
    }

    internal static bool IsAllaganUndercutColor(byte r, byte g, byte b) =>
        IsColor(r, g, b, ImGuiColors.DalamudRed);

    internal static bool IsAllaganNeedsCheckColor(byte r, byte g, byte b) =>
        IsColor(r, g, b, ImGuiColors.DalamudYellow);

    private static bool IsColor(byte r, byte g, byte b, Vector4 color)
    {
        byte expectedR = (byte)Math.Round(Math.Clamp(color.X, 0f, 1f) * 255f);
        byte expectedG = (byte)Math.Round(Math.Clamp(color.Y, 0f, 1f) * 255f);
        byte expectedB = (byte)Math.Round(Math.Clamp(color.Z, 0f, 1f) * 255f);
        // A small tolerance covers the conversion between ImGui's float color and the game's byte color.
        return Math.Abs(r - expectedR) <= 3 && Math.Abs(g - expectedG) <= 3 && Math.Abs(b - expectedB) <= 3;
    }
}
