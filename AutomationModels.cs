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
    CloseMarketResultsForOwnedMatch,
    SetOwnedMatchPrice,
    ConfirmOwnedMatchPrice,
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

internal readonly record struct MarketPriceSnapshot(
    ExternalMarketListing BestExternal,
    ExternalMarketListing BestOtherOwned,
    bool IgnoredSuspiciousLowPrices = false)
{
    internal bool HasExternal => BestExternal.UnitPrice > 0;
    internal bool HasOtherOwned => BestOtherOwned.UnitPrice > 0;
}

internal static class MarketPriceSafeguard
{
    private const decimal SuspiciousGapRatio = 20m;
    private const uint SuspiciousGapGil = 100;

    // A 1-gil listing activates outlier protection. Walk upward through the ordered
    // prices and use the market above the largest clearly artificial discontinuity.
    // Example: 1, 5, 10,000, 10,200 selects 10,000 rather than merely selecting 5.
    internal static uint SelectReferenceFloor(IEnumerable<uint> unitPrices, out bool ignoredSuspiciousLowPrices)
    {
        uint[] prices = unitPrices.Where(x => x > 0).Distinct().Order().ToArray();
        ignoredSuspiciousLowPrices = false;
        if (prices.Length == 0)
            return 0;
        if (prices[0] != 1)
            return prices[0];

        int bestSplit = -1;
        decimal bestRatio = 0;
        for (int i = 0; i < prices.Length - 1; i++)
        {
            uint lower = prices[i];
            uint higher = prices[i + 1];
            uint gap = higher - lower;
            decimal ratio = (decimal)higher / lower;
            if (gap < SuspiciousGapGil || ratio < SuspiciousGapRatio || ratio <= bestRatio)
                continue;

            bestSplit = i + 1;
            bestRatio = ratio;
        }

        if (bestSplit >= 0)
        {
            ignoredSuspiciousLowPrices = true;
            return prices[bestSplit];
        }

        // With no credible higher market cluster, 1 gil is not a usable undercut
        // reference. Use the next real price if one exists; otherwise do nothing.
        ignoredSuspiciousLowPrices = true;
        return prices.Length > 1 ? prices[1] : 0;
    }
}

internal enum MarketPricingAction
{
    None,
    MatchOtherOwned,
    UndercutExternal,
}

internal static class MarketPricingDecision
{
    internal static MarketPricingAction Choose(uint currentPrice, uint externalPrice, uint otherOwnedPrice)
    {
        if (otherOwnedPrice > 0 && otherOwnedPrice < currentPrice &&
            (externalPrice == 0 || otherOwnedPrice <= externalPrice))
            return MarketPricingAction.MatchOtherOwned;

        if (externalPrice > 0 && currentPrice > externalPrice)
            return MarketPricingAction.UndercutExternal;

        return MarketPricingAction.None;
    }
}

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
    public uint CheapestOtherOwnedPrice { get; set; }
    public DateTime CheckedAt { get; set; }
}

internal static class AutomationPlan
{
    internal static int[] ListingRows(int listingCount) =>
        Enumerable.Range(0, Math.Clamp(listingCount, 0, 20)).ToArray();

    internal static int[] NormalizeUndercutRows(IEnumerable<int> rows, int listingCount) =>
        rows.Where(x => x >= 0 && x < Math.Clamp(listingCount, 0, 20)).Distinct().Order().ToArray();

    internal static bool RequiresOwnedPriceMatch(OwnedAwareMarketAssessment assessment) =>
        MarketPricingDecision.Choose(
            assessment.OwnedUnitPrice,
            assessment.CheapestExternalPrice,
            assessment.CheapestOtherOwnedPrice) == MarketPricingAction.MatchOtherOwned;

    internal static int[] AdjustmentRows(
        IEnumerable<int> undercutRows,
        IEnumerable<OwnedAwareMarketAssessment> assessments,
        int listingCount)
    {
        IEnumerable<int> ownedMatchRows = assessments
            .Where(RequiresOwnedPriceMatch)
            .Select(x => x.VisualIndex);
        return NormalizeUndercutRows(undercutRows.Concat(ownedMatchRows), listingCount);
    }
}
