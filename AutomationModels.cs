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

internal static class AutomationPlan
{
    internal static int[] ListingRows(int listingCount) =>
        Enumerable.Range(0, Math.Clamp(listingCount, 0, 20)).ToArray();

    internal static int[] NormalizeUndercutRows(IEnumerable<int> rows, int listingCount) =>
        rows.Where(x => x >= 0 && x < Math.Clamp(listingCount, 0, 20)).Distinct().Order().ToArray();
}
