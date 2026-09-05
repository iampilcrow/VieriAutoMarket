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
    CloseMarketResults,
    WaitForPriceWindowAfterCheck,
    ClosePriceWindow,
    WaitForListingAfterCheck,
    CaptureCheckedStatus,
    ClickBestListing,
    WaitForAdjustment,
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

internal readonly record struct MarketListingIdentity(uint ItemId, bool IsHighQuality);

internal readonly record struct MarketListingRow(int VisualIndex, MarketListingIdentity Identity);

internal static class AutomationPlan
{
    internal static int[] ListingRows(int listingCount) =>
        Enumerable.Range(0, Math.Clamp(listingCount, 0, 20)).ToArray();

    internal static int[] NormalizeUndercutRows(IEnumerable<int> rows, int listingCount) =>
        rows.Where(x => x >= 0 && x < Math.Clamp(listingCount, 0, 20)).Distinct().Order().ToArray();
}
