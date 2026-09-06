namespace VieriAutoMarket;

internal enum AutomationMode
{
    Check,
    Adjust,
    CheckAndAdjust,
}

internal enum AutomationStep
{
    Idle,
    ShowListingForDiscovery,
    CaptureDiscoveredListing,
    SelectListing,
    WaitForContextMenu,
    OpenAdjustPrice,
    WaitForPriceWindow,
    WaitForMarketResults,
    RecoverFromSearchThrottle,
    CloseMarketResults,
    WaitForPriceWindowAfterCheck,
    ClosePriceWindow,
    WaitForListingAfterCheck,
    VerifyEmptyAdjustment,
    CaptureCheckedStatus,
    ClickBestListing,
    WaitForAdjustment,
    VerifyAdjustment,
    BeginAdjustmentPass,
}

internal enum MarketResultsState
{
    Waiting,
    ReadyWithoutListings,
    ReadyWithListings,
}

internal enum ListingPriceState
{
    Unknown,
    Current,
    NeedsCheck,
    Undercut,
}

internal enum ExternalListingState
{
    Waiting,
    None,
    Ready,
}

internal readonly record struct MarketListingIdentity(uint ItemId, bool IsHighQuality);

internal readonly record struct MarketListingRow(int VisualIndex, MarketListingIdentity Identity);

internal readonly record struct RetainerListingSnapshot(
    int VisualIndex,
    int InventorySlot,
    MarketListingIdentity Identity,
    uint UnitPrice,
    ulong RetainerId,
    string RetainerName,
    string ItemName);

internal readonly record struct ExternalMarketListing(
    int ResultIndex,
    uint UnitPrice,
    ulong RetainerId,
    string RetainerName);

public sealed class MarketRunReportEntry
{
    public string Item { get; set; } = string.Empty;
    public string Retainer { get; set; } = string.Empty;
    public string Quality { get; set; } = string.Empty;
    public uint OldPrice { get; set; }
    public string Competitor { get; set; } = string.Empty;
    public uint CompetitorPrice { get; set; }
    public uint FinalPrice { get; set; }
    public string Outcome { get; set; } = string.Empty;
}

public sealed class OwnedAwareMarketAssessment
{
    public uint ItemId { get; set; }
    public bool IsHighQuality { get; set; }
    public int VisualIndex { get; set; }
    public ulong RetainerId { get; set; }
    public uint OwnedUnitPrice { get; set; }
    public uint CheapestExternalPrice { get; set; }
    public DateTime CheckedAt { get; set; }
}

internal static class AutomationPlan
{
    internal static int[] ListingRows(int listingCount) =>
        Enumerable.Range(0, Math.Clamp(listingCount, 0, 20)).ToArray();

    internal static int[] NormalizeUndercutRows(IEnumerable<int> rows, int listingCount) =>
        rows.Where(x => x >= 0 && x < Math.Clamp(listingCount, 0, 20)).Distinct().Order().ToArray();
}
