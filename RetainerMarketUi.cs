using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Colors;
using Dalamud.Plugin.Services;
using ECommons.Automation;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Graphics;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Info;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace VieriAutoMarket;

internal sealed unsafe class RetainerMarketUi
{
    private readonly IGameGui gameGui;
    private readonly Dictionary<uint, ByteColor> normalItemColors = [];
    private readonly HashSet<ulong> ownedRetainerIds = [];

    internal RetainerMarketUi(IGameGui gameGui) => this.gameGui = gameGui;

    internal void BeginAutomationRun() => ownedRetainerIds.Clear();

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

    internal int[] GetUniqueMarketCheckRows(int listingCount)
    {
        AtkUnitBase* addon = GetAddon("RetainerSellList");
        InventoryManager* inventory = InventoryManager.Instance();
        InventoryContainer* container = inventory == null
            ? null
            : inventory->GetInventoryContainer(InventoryType.RetainerMarket);
        if (addon == null || addon->AtkValues == null || container == null || !container->IsLoaded)
            return AutomationPlan.ListingRows(listingCount);

        var mappedRows = new List<MarketListingRow>();
        for (int visualIndex = 0; visualIndex < listingCount; visualIndex++)
        {
            int atkIndex = 15 + visualIndex * 13;
            if (atkIndex >= addon->AtkValuesCount || addon->AtkValues[atkIndex].Type == AtkValueType.Undefined)
                return AutomationPlan.ListingRows(listingCount);

            int inventorySlot = addon->AtkValues[atkIndex].Int;
            if (inventorySlot < 0 || inventorySlot >= container->Size)
                return AutomationPlan.ListingRows(listingCount);
            InventoryItem* item = container->GetInventorySlot(inventorySlot);
            if (item == null || item->ItemId == 0)
                return AutomationPlan.ListingRows(listingCount);

            mappedRows.Add(new MarketListingRow(
                visualIndex,
                new MarketListingIdentity(item->ItemId,
                    item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality))));
        }

        return mappedRows
            .GroupBy(x => x.Identity)
            .Select(group => group.First().VisualIndex)
            .ToArray();
    }

    internal bool TryGetListingSnapshot(int visualIndex, out RetainerListingSnapshot snapshot)
    {
        snapshot = default;
        AtkUnitBase* addon = GetAddon("RetainerSellList");
        InventoryManager* inventory = InventoryManager.Instance();
        InventoryContainer* container = inventory == null
            ? null
            : inventory->GetInventoryContainer(InventoryType.RetainerMarket);
        if (addon == null || addon->AtkValues == null || container == null || !container->IsLoaded)
            return false;

        int atkIndex = 15 + visualIndex * 13;
        if (atkIndex >= addon->AtkValuesCount || addon->AtkValues[atkIndex].Type == AtkValueType.Undefined)
            return false;

        int inventorySlot = addon->AtkValues[atkIndex].Int;
        if (inventorySlot < 0 || inventorySlot >= container->Size)
            return false;

        InventoryItem* item = container->GetInventorySlot(inventorySlot);
        if (item == null || item->ItemId == 0)
            return false;

        RetainerManager* retainers = RetainerManager.Instance();
        ulong retainerId = retainers == null ? 0 : retainers->LastSelectedRetainerId;
        string retainerName = "Current retainer";
        RetainerManager.Retainer* activeRetainer = retainers == null ? null : retainers->GetActiveRetainer();
        if (activeRetainer != null && !string.IsNullOrWhiteSpace(activeRetainer->NameString))
            retainerName = activeRetainer->NameString.Trim();

        ulong rawPrice = inventory->GetRetainerMarketPrice((short)inventorySlot);
        uint unitPrice = rawPrice > uint.MaxValue ? uint.MaxValue : (uint)rawPrice;
        snapshot = new RetainerListingSnapshot(
            visualIndex,
            inventorySlot,
            new MarketListingIdentity(item->ItemId,
                item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality)),
            unitPrice,
            retainerId,
            retainerName,
            GetListingName(visualIndex, item->ItemId));
        return retainerId != 0 && unitPrice != 0;
    }

    internal bool TryGetSelectedListingSnapshot(int visualIndex, out RetainerListingSnapshot snapshot)
    {
        snapshot = default;
        AddonRetainerSell* addon = (AddonRetainerSell*)GetAddon("RetainerSell");
        InventoryManager* inventory = InventoryManager.Instance();
        InventoryContainer* market = inventory == null
            ? null
            : inventory->GetInventoryContainer(InventoryType.RetainerMarket);
        AgentModule* agentModule = AgentModule.Instance();
        AgentInterface* retainerAgent = agentModule == null
            ? null
            : agentModule->GetAgentByInternalId(AgentId.Retainer);
        RetainerManager* retainers = RetainerManager.Instance();
        ulong retainerId = retainers == null ? 0 : retainers->LastSelectedRetainerId;
        if (addon == null || !addon->IsVisible || market == null || !market->IsLoaded ||
            retainerAgent == null || addon->AtkValues == null || addon->AtkValuesCount <= 5 || retainerId == 0)
            return false;

        // AgentRetainer.SelectedSlot is the inventory slot backing the currently open
        // Adjust Price window. BlockedItems[0] is not reliable here: it can retain the
        // preceding listing when Marketbuddy opens comparison windows in quick succession.
        const int AgentRetainerSelectedSlotOffset = 0x5C;
        int inventorySlot = *((byte*)retainerAgent + AgentRetainerSelectedSlotOffset);
        if (inventorySlot < 0 || inventorySlot >= market->Size)
            return false;
        InventoryItem* selected = market->GetInventorySlot(inventorySlot);
        int askingPrice = addon->AtkValues[5].Int;
        if (selected == null || selected->ItemId == 0 || askingPrice <= 0)
            return false;

        string retainerName = "Current retainer";
        RetainerManager.Retainer* activeRetainer = retainers->GetActiveRetainer();
        if (activeRetainer != null && !string.IsNullOrWhiteSpace(activeRetainer->NameString))
            retainerName = activeRetainer->NameString.Trim();

        string itemName = addon->ItemName == null
            ? $"Item {selected->ItemId}"
            : new AddonMaster.RetainerSell(addon).ItemName.Trim();
        snapshot = new RetainerListingSnapshot(
            visualIndex,
            inventorySlot,
            new MarketListingIdentity(selected->ItemId,
                selected->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality)),
            (uint)askingPrice,
            retainerId,
            retainerName,
            string.IsNullOrWhiteSpace(itemName) ? $"Item {selected->ItemId}" : itemName);
        return true;
    }

    internal bool TryAlignListingWithMarketSearch(
        RetainerListingSnapshot selected,
        out RetainerListingSnapshot aligned)
    {
        aligned = selected;
        AgentModule* agentModule = AgentModule.Instance();
        AgentItemSearch* agent = agentModule == null
            ? null
            : (AgentItemSearch*)agentModule->GetAgentByInternalId(AgentId.ItemSearch);
        if (agent == null || agent->InfoProxyItemSearch == null)
            return false;

        InfoProxyItemSearch* search = agent->InfoProxyItemSearch;
        if (search->WaitingForListings || search->SearchItemId == 0)
            return false;

        if (selected.Identity.ItemId != search->SearchItemId)
        {
            aligned = selected with
            {
                Identity = new MarketListingIdentity(search->SearchItemId, selected.Identity.IsHighQuality),
            };
        }

        return true;
    }

    internal bool TryGetInventoryListingSnapshot(RetainerListingSnapshot expected, out RetainerListingSnapshot snapshot)
    {
        snapshot = default;
        InventoryManager* inventory = InventoryManager.Instance();
        InventoryContainer* container = inventory == null
            ? null
            : inventory->GetInventoryContainer(InventoryType.RetainerMarket);
        RetainerManager* retainers = RetainerManager.Instance();
        if (container == null || !container->IsLoaded || expected.InventorySlot < 0 ||
            expected.InventorySlot >= container->Size || retainers == null)
            return false;

        InventoryItem* item = container->GetInventorySlot(expected.InventorySlot);
        ulong retainerId = retainers->LastSelectedRetainerId;
        ulong rawPrice = inventory->GetRetainerMarketPrice((short)expected.InventorySlot);
        if (item == null || item->ItemId == 0 || retainerId == 0 || rawPrice == 0)
            return false;

        uint unitPrice = rawPrice > uint.MaxValue ? uint.MaxValue : (uint)rawPrice;
        snapshot = expected with
        {
            Identity = new MarketListingIdentity(item->ItemId,
                item->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality)),
            UnitPrice = unitPrice,
            RetainerId = retainerId,
        };
        return true;
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

        int resultCount = addon->Results->GetItemCount();
        if (resultCount > 0)
        {
            AtkComponentListItemRenderer* first = addon->Results->GetItemRenderer(0);
            AtkTextNode* price = first == null ? null : first->GetTextNodeById(5);
            if (price != null && !string.IsNullOrWhiteSpace(price->NodeText.ToString()))
                return MarketResultsState.ReadyWithListings;
        }

        AgentModule* agentModule = AgentModule.Instance();
        AgentItemSearch* agent = agentModule == null
            ? null
            : (AgentItemSearch*)agentModule->GetAgentByInternalId(AgentId.ItemSearch);
        if (agent == null || agent->InfoProxyItemSearch == null)
            return MarketResultsState.Waiting;

        var search = agent->InfoProxyItemSearch;
        if (search->WaitingForListings || search->SearchItemId == 0)
            return MarketResultsState.Waiting;

        if (resultCount <= 0)
            return search->ListingCount == 0
                ? MarketResultsState.ReadyWithoutListings
                : MarketResultsState.Waiting;
        return MarketResultsState.Waiting;
    }

    internal ExternalListingState GetBestExternalMarketListing(
        MarketListingIdentity selectedIdentity,
        out ExternalMarketListing result)
    {
        result = default;
        AddonItemSearchResult* addon = (AddonItemSearchResult*)GetAddon("ItemSearchResult");
        if (addon == null || !addon->IsVisible || addon->Results == null || addon->Results->GetItemCount() <= 0)
            return ExternalListingState.Waiting;

        AgentModule* agentModule = AgentModule.Instance();
        AgentItemSearch* agent = agentModule == null
            ? null
            : (AgentItemSearch*)agentModule->GetAgentByInternalId(AgentId.ItemSearch);
        if (agent == null || agent->InfoProxyItemSearch == null)
            return ExternalListingState.Waiting;

        InfoProxyItemSearch* search = agent->InfoProxyItemSearch;
        if (search->WaitingForListings || search->SearchItemId != selectedIdentity.ItemId)
            return ExternalListingState.Waiting;

        if (!TryGetOwnedRetainerIds(search, out HashSet<ulong> ownedRetainerIds))
            return ExternalListingState.Waiting;

        int resultCount = Math.Min((int)search->ListingCount, 100);
        uint lowestPrice = uint.MaxValue;
        int lowestIndex = -1;
        ulong lowestRetainer = 0;
        for (int i = 0; i < resultCount; i++)
        {
            MarketBoardListing listing = search->Listings[i];
            if (listing.ItemId != selectedIdentity.ItemId ||
                listing.IsHqItem != selectedIdentity.IsHighQuality ||
                listing.RetainerId == 0 ||
                ownedRetainerIds.Contains(listing.RetainerId) ||
                listing.UnitPrice == 0 ||
                listing.UnitPrice >= lowestPrice)
                continue;

            lowestPrice = listing.UnitPrice;
            lowestIndex = i;
            lowestRetainer = listing.RetainerId;
        }

        if (lowestIndex < 0)
            return ExternalListingState.None;

        result = new ExternalMarketListing(
            lowestIndex,
            lowestPrice,
            lowestRetainer,
            GetMarketResultRetainerName(addon, lowestIndex));
        return ExternalListingState.Ready;
    }

    internal bool ClickMarketListing(int resultIndex)
    {
        AddonItemSearchResult* addon = (AddonItemSearchResult*)GetAddon("ItemSearchResult");
        if (addon == null || !addon->IsVisible || addon->Results == null ||
            resultIndex < 0 || resultIndex >= addon->Results->GetItemCount())
            return false;

        addon->Results->ScrollToItem((short)resultIndex);
        addon->Results->UpdateListItems();
        addon->Results->DispatchItemEvent(resultIndex, AtkEventType.ListItemClick);
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

    internal void ApplyOwnershipAwareHighlighting(IReadOnlyCollection<OwnedAwareMarketAssessment> assessments)
    {
        AtkUnitBase* addon = GetAddon("RetainerSellList");
        AtkComponentList* list = addon == null ? null : addon->GetComponentListById(11);
        if (list == null || assessments.Count == 0)
            return;

        DateTime freshAfter = DateTime.UtcNow - TimeSpan.FromMinutes(30);
        RetainerManager* retainers = RetainerManager.Instance();
        ulong currentRetainerId = retainers == null ? 0 : retainers->LastSelectedRetainerId;
        Dictionary<int, OwnedAwareMarketAssessment> fresh = assessments
            .Where(x => x.CheckedAt >= freshAfter && x.RetainerId == currentRetainerId)
            .GroupBy(x => x.VisualIndex)
            .ToDictionary(x => x.Key, x => x.OrderByDescending(y => y.CheckedAt).First());
        if (fresh.Count == 0)
            return;

        for (int i = 0; i < list->ListLength; i++)
        {
            AtkComponentListItemRenderer* renderer = list->ItemRendererList[i].AtkComponentListItemRenderer;
            if (renderer == null || renderer->ListItemIndex < 0)
                continue;
            AtkTextNode* text = renderer->GetTextNodeById(3);
            if (text == null || IsAllaganUndercutColor(text->TextColor.R, text->TextColor.G, text->TextColor.B) ||
                IsAllaganNeedsCheckColor(text->TextColor.R, text->TextColor.G, text->TextColor.B))
                continue;
            if (fresh.TryGetValue(renderer->ListItemIndex, out OwnedAwareMarketAssessment? assessment))
                normalItemColors[assessment.ItemId] = text->TextColor;
        }

        for (int i = 0; i < list->ListLength; i++)
        {
            AtkComponentListItemRenderer* renderer = list->ItemRendererList[i].AtkComponentListItemRenderer;
            if (renderer == null || renderer->ListItemIndex < 0)
                continue;
            AtkTextNode* text = renderer->GetTextNodeById(3);
            if (text == null || !IsAllaganUndercutColor(text->TextColor.R, text->TextColor.G, text->TextColor.B) ||
                !fresh.TryGetValue(renderer->ListItemIndex, out OwnedAwareMarketAssessment? assessment))
                continue;

            bool currentAgainstExternalMarket = assessment.CheapestExternalPrice == 0 ||
                                                assessment.OwnedUnitPrice <= assessment.CheapestExternalPrice;
            if (!currentAgainstExternalMarket)
                continue;

            text->TextColor = normalItemColors.TryGetValue(assessment.ItemId, out ByteColor normal)
                ? normal
                : new ByteColor { R = 238, G = 238, B = 238, A = 255 };
        }
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

    private bool TryGetOwnedRetainerIds(InfoProxyItemSearch* search, out HashSet<ulong> result)
    {
        var liveRetainerIds = new HashSet<ulong>();
        bool completeRetainerListAvailable = false;
        RetainerManager* manager = RetainerManager.Instance();
        if (manager != null)
        {
            if (manager->LastSelectedRetainerId != 0)
                liveRetainerIds.Add(manager->LastSelectedRetainerId);

            int assignedRetainerCount = 0;
            foreach (RetainerManager.Retainer retainer in manager->Retainers)
            {
                if (retainer.RetainerId != 0)
                {
                    assignedRetainerCount++;
                    liveRetainerIds.Add(retainer.RetainerId);
                }
            }

            // IsReady can briefly clear while moving between market searches even though the
            // populated retainer array is still authoritative for this active retainer session.
            completeRetainerListAvailable = manager->IsReady || assignedRetainerCount > 0;
        }

        int playerRetainerCount = Math.Min((int)search->PlayerRetainerCount, 10);
        if (playerRetainerCount > 0)
            completeRetainerListAvailable = true;
        for (int i = 0; i < playerRetainerCount; i++)
        {
            ulong retainerId = search->PlayerRetainers[i].RetainerId;
            if (retainerId != 0)
                liveRetainerIds.Add(retainerId);
        }

        if (completeRetainerListAvailable && liveRetainerIds.Count > 0)
        {
            ownedRetainerIds.Clear();
            ownedRetainerIds.UnionWith(liveRetainerIds);
        }

        result = [.. ownedRetainerIds];
        return result.Count > 0;
    }

    private string GetListingName(int visualIndex, uint itemId)
    {
        AtkUnitBase* addon = GetAddon("RetainerSellList");
        AtkComponentList* list = addon == null ? null : addon->GetComponentListById(11);
        if (list != null)
        {
            for (int i = 0; i < list->ListLength; i++)
            {
                AtkComponentListItemRenderer* renderer = list->ItemRendererList[i].AtkComponentListItemRenderer;
                if (renderer == null || renderer->ListItemIndex != visualIndex)
                    continue;
                AtkTextNode* text = renderer->GetTextNodeById(3);
                string name = text == null ? string.Empty : text->NodeText.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(name))
                    return name;
            }
        }

        return $"Item {itemId}";
    }

    private static string GetMarketResultRetainerName(AddonItemSearchResult* addon, int resultIndex)
    {
        addon->Results->ScrollToItem((short)resultIndex);
        addon->Results->UpdateListItems();
        AtkComponentListItemRenderer* renderer = addon->Results->GetItemRenderer(resultIndex);
        AtkTextNode* text = renderer == null ? null : renderer->GetTextNodeById(10);
        string name = text == null ? string.Empty : text->NodeText.ToString().Trim();
        return string.IsNullOrWhiteSpace(name) ? "External retainer" : name;
    }
}
