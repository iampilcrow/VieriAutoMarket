using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Game.Text.SeStringHandling;

namespace VieriAutoMarket;

internal sealed class MarketAutomationController : IDisposable
{
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan MarketDataTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan EmptyResultConfirmation = TimeSpan.FromMilliseconds(1500);
    private static readonly TimeSpan PriceVerificationTimeout = TimeSpan.FromSeconds(8);
    private const int MaxEmptyResultRetries = 2;
    private const int MaxAdjustmentRetries = 2;
    private const int MaxSearchThrottleRetries = 3;

    private readonly IFramework framework;
    private readonly IChatGui chat;
    private readonly IToastGui toasts;
    private readonly IPluginLog log;
    private readonly IDalamudPluginInterface pi;
    private readonly Configuration config;
    private readonly DependencyService dependencies;
    private readonly RetainerMarketUi ui;
    private readonly MarketSearchPacer searchPacer = new();
    private readonly List<int> checkedUndercuts = [];
    private readonly List<MarketRunReportEntry> runReport = [];
    private int[] allRows = [];
    private int[] rows = [];
    private int position;
    private DateTime nextActionUtc;
    private DateTime stepStartedUtc;
    private bool adjustmentPass;
    private bool skipAdjustmentAfterClose;
    private int adjustedCount;
    private int skippedNoCompetitorCount;
    private int skippedAlreadyCompetitiveCount;
    private int failedAdjustmentCount;
    private int currentRowRetryCount;
    private int currentAdjustmentRetryCount;
    private int currentSearchThrottleRetries;
    private RetainerListingSnapshot currentListing;
    private RetainerListingSnapshot originalListing;
    private ExternalMarketListing currentCompetitor;
    private string pendingSkipOutcome = string.Empty;
    private bool searchThrottleRejected;

    internal MarketAutomationController(IFramework framework, IChatGui chat, IToastGui toasts, IPluginLog log,
        IDalamudPluginInterface pi, Configuration config, DependencyService dependencies, RetainerMarketUi ui)
    {
        this.framework = framework;
        this.chat = chat;
        this.toasts = toasts;
        this.log = log;
        this.pi = pi;
        this.config = config;
        this.dependencies = dependencies;
        this.ui = ui;
        framework.Update += OnFrameworkUpdate;
        toasts.ErrorToast += OnErrorToast;
    }

    internal bool IsRunning => Step != AutomationStep.Idle;
    internal AutomationStep Step { get; private set; }
    internal AutomationMode Mode { get; private set; }
    internal string Status { get; private set; } = "Ready";
    internal int CurrentNumber => IsRunning && rows.Length > 0 ? Math.Min(position + 1, rows.Length) : 0;
    internal int Total => rows.Length;

    internal void Start(AutomationMode mode)
    {
        if (IsRunning)
            return;

        runReport.Clear();

        if (!dependencies.IsLoaded(DependencyService.MarketbuddyName))
        {
            Fail("Marketbuddy must be installed and loaded.");
            return;
        }
        if (!dependencies.IsLoaded(DependencyService.AllaganMarketName))
        {
            Fail("Allagan Market must be installed and loaded.");
            return;
        }
        if (!ui.IsReady("RetainerSellList"))
        {
            Fail("Open a retainer's sell list before starting.");
            return;
        }
        if (IsMarketbuddyLocked())
        {
            Fail("Marketbuddy is currently locked by another plugin.");
            return;
        }

        int listingCount = ui.GetListingCount();
        if (listingCount <= 0)
        {
            Fail("This retainer has no active market listings.");
            return;
        }

        Mode = mode;
        ui.BeginAutomationRun();
        // A new pass must begin from Allagan Market's unmodified state. Fresh ownership-aware
        // assessments are rebuilt item-by-item as the pass receives authoritative game results.
        config.MarketAssessments.Clear();
        checkedUndercuts.Clear();
        adjustedCount = 0;
        skippedNoCompetitorCount = 0;
        skippedAlreadyCompetitiveCount = 0;
        failedAdjustmentCount = 0;
        currentRowRetryCount = 0;
        currentAdjustmentRetryCount = 0;
        currentSearchThrottleRetries = 0;
        currentListing = default;
        originalListing = default;
        currentCompetitor = default;
        pendingSkipOutcome = string.Empty;
        skipAdjustmentAfterClose = false;
        adjustmentPass = mode == AutomationMode.Adjust;
        allRows = AutomationPlan.ListingRows(listingCount);
        rows = allRows;
        position = 0;

        Status = mode switch
        {
            AutomationMode.Check => "Checking every listing for undercuts",
            AutomationMode.Adjust => "Finding every listing marked undercut",
            _ => "Checking every listing, then adjusting undercuts",
        };
        MoveTo(mode == AutomationMode.Adjust
            ? AutomationStep.ShowListingForDiscovery
            : AutomationStep.SelectListing, TimeSpan.Zero);
        chat.Print(Status + ". Keep the retainer market windows open.", Plugin.Tag);
    }

    internal void Stop(string reason = "Stopped by user")
    {
        if (!IsRunning)
            return;
        TryReturnToSellList();
        Step = AutomationStep.Idle;
        Status = reason;
        SaveRunReport(reason);
        chat.Print(reason + ".", Plugin.Tag);
    }

    private void OnFrameworkUpdate(IFramework _)
    {
        if (!IsRunning || DateTime.UtcNow < nextActionUtc)
            return;

        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            log.Error(ex, "Market automation failed in {Step}", Step);
            Fail($"Stopped safely during {FriendlyStep(Step)}: {ex.Message}");
        }
    }

    private void Tick()
    {
        if (!dependencies.IsLoaded(DependencyService.MarketbuddyName) ||
            !dependencies.IsLoaded(DependencyService.AllaganMarketName))
        {
            Fail("A required market plugin was unloaded.");
            return;
        }

        if (IsMarketbuddyLocked())
        {
            Fail("Marketbuddy became locked by another plugin.");
            return;
        }

        if (searchThrottleRejected)
        {
            searchThrottleRejected = false;
            currentSearchThrottleRetries++;
            if (currentSearchThrottleRetries > MaxSearchThrottleRetries)
            {
                Fail("The game repeatedly rejected market searches after safe backoff retries.");
                return;
            }

            DateTime now = DateTime.UtcNow;
            searchPacer.RecordRejected(now);
            TryReturnToSellList();
            Status = $"The game requested a market-search pause; retrying item {position + 1} of {rows.Length} safely";
            MoveTo(AutomationStep.RecoverFromSearchThrottle, searchPacer.Remaining(now));
            return;
        }

        switch (Step)
        {
            case AutomationStep.ShowListingForDiscovery:
                if (!ui.IsReady("RetainerSellList"))
                {
                    WaitOrFail(WindowTimeout, "the retainer sell list");
                    return;
                }
                if (!ui.ShowListingRow(rows[position]))
                {
                    Fail("Could not inspect the next retainer listing.");
                    return;
                }
                Status = $"Finding undercuts: {position + 1} of {rows.Length}";
                MoveTo(AutomationStep.CaptureDiscoveredListing);
                break;

            case AutomationStep.CaptureDiscoveredListing:
                ListingPriceState priceState = ui.GetListingPriceState(rows[position]);
                if (priceState == ListingPriceState.Unknown)
                {
                    WaitOrFail(TimeSpan.FromSeconds(2), "Allagan Market to paint the selected listing");
                    return;
                }
                if (priceState == ListingPriceState.Undercut)
                    checkedUndercuts.Add(rows[position]);
                position++;
                if (position < rows.Length)
                {
                    MoveTo(AutomationStep.ShowListingForDiscovery, TimeSpan.Zero);
                    return;
                }

                rows = AutomationPlan.NormalizeUndercutRows(checkedUndercuts, ui.GetListingCount());
                position = 0;
                if (Mode == AutomationMode.Check)
                {
                    Complete($"Undercut check complete: {rows.Length} of {allRows.Length} listing(s) are undercut.");
                    return;
                }
                adjustmentPass = true;
                if (rows.Length == 0)
                {
                    Complete("Allagan Market does not currently show any undercut listings.");
                    return;
                }
                Status = $"Adjusting {rows.Length} undercut listing(s)";
                MoveTo(AutomationStep.SelectListing, TimeSpan.FromMilliseconds(150));
                break;

            case AutomationStep.SelectListing:
                if (!ui.IsReady("RetainerSellList"))
                {
                    WaitOrFail(WindowTimeout, "the retainer sell list");
                    return;
                }
                if (!ui.ShowListingRow(rows[position]))
                {
                    Fail("Could not show the next retainer listing.");
                    return;
                }
                if (!ui.SelectListing(rows[position]))
                {
                    Fail("Could not select the next retainer listing.");
                    return;
                }
                Status = $"Opening listing {position + 1} of {rows.Length}";
                MoveTo(AutomationStep.WaitForContextMenu);
                break;

            case AutomationStep.WaitForContextMenu:
                if (!ui.IsReady("ContextMenu"))
                {
                    WaitOrFail(WindowTimeout, "the listing menu");
                    return;
                }
                MoveTo(AutomationStep.OpenAdjustPrice, TimeSpan.Zero);
                break;

            case AutomationStep.OpenAdjustPrice:
                DateTime searchStart = DateTime.UtcNow;
                if (!searchPacer.CanStart(searchStart))
                {
                    Status = $"Waiting for the market-search cooldown before item {position + 1} of {rows.Length}";
                    nextActionUtc = searchStart + searchPacer.Remaining(searchStart);
                    return;
                }
                if (!ui.HasAdjustPriceEntry() || !ui.SelectAdjustPrice())
                {
                    Fail("The selected listing did not offer Adjust Price.");
                    return;
                }
                searchPacer.RecordStarted(searchStart);
                MoveTo(AutomationStep.WaitForPriceWindow);
                break;

            case AutomationStep.WaitForPriceWindow:
                if (!ui.IsReady("RetainerSell"))
                {
                    WaitOrFail(WindowTimeout, "the Adjust Price window");
                    return;
                }
                if (!ui.TryGetSelectedListingSnapshot(rows[position], out currentListing))
                {
                    WaitOrFail(WindowTimeout, "the selected listing details");
                    return;
                }
                if (currentAdjustmentRetryCount == 0)
                {
                    originalListing = currentListing;
                    currentCompetitor = default;
                    pendingSkipOutcome = string.Empty;
                }
                MoveTo(AutomationStep.WaitForMarketResults);
                break;

            case AutomationStep.WaitForMarketResults:
                MarketResultsState marketResults = ui.GetMarketResultsState();
                if (marketResults == MarketResultsState.Waiting)
                {
                    WaitOrFail(MarketDataTimeout, "market results from Marketbuddy");
                    return;
                }

                currentSearchThrottleRetries = 0;

                if (marketResults == MarketResultsState.ReadyWithoutListings)
                {
                    if (!Expired(EmptyResultConfirmation))
                        return;
                    RecordAssessment(currentListing, 0);
                    skipAdjustmentAfterClose = adjustmentPass;
                    pendingSkipOutcome = "Unchanged: no external competitor; owned listings ignored";
                    if (!adjustmentPass)
                        AddReport(currentListing, 0, currentListing.UnitPrice,
                            "Checked: no external competitor; owned listings ignored");
                    Status = adjustmentPass
                        ? $"Verifying the empty result for item {position + 1} of {rows.Length}"
                        : $"Allagan Market checked item {position + 1} of {rows.Length}; no competing listings";
                    MoveTo(AutomationStep.CloseMarketResults,
                        TimeSpan.FromMilliseconds(Math.Max(250, config.ActionDelayMilliseconds)));
                    return;
                }

                ExternalListingState externalState = ui.GetBestExternalMarketListing(
                    currentListing.Identity,
                    out ExternalMarketListing externalListing);
                if (externalState == ExternalListingState.Waiting)
                {
                    WaitOrFail(MarketDataTimeout, "market ownership and quality details");
                    return;
                }

                if (externalState == ExternalListingState.None)
                {
                    RecordAssessment(currentListing, 0);
                    skipAdjustmentAfterClose = adjustmentPass;
                    pendingSkipOutcome = "Unchanged: no external competitor; owned listings ignored";
                    if (!adjustmentPass)
                        AddReport(currentListing, 0, currentListing.UnitPrice,
                            "Checked: no external competitor; owned listings ignored");
                    Status = adjustmentPass
                        ? $"No external competitor for item {position + 1} of {rows.Length}; your retainers will not undercut one another"
                        : $"Checked item {position + 1} of {rows.Length}; only your own listings remain";
                    MoveTo(AutomationStep.CloseMarketResults,
                        TimeSpan.FromMilliseconds(Math.Max(350, config.ActionDelayMilliseconds)));
                    return;
                }

                currentCompetitor = externalListing;
                RecordAssessment(currentListing, currentCompetitor.UnitPrice);
                if (!adjustmentPass)
                {
                    AddReport(currentListing, currentCompetitor.UnitPrice, currentListing.UnitPrice,
                        currentListing.UnitPrice <= currentCompetitor.UnitPrice
                            ? "Checked: current against the external market"
                            : "Checked: undercut by an external seller",
                        currentCompetitor.RetainerName);
                }

                if (adjustmentPass && currentListing.UnitPrice <= currentCompetitor.UnitPrice)
                {
                    skipAdjustmentAfterClose = true;
                    pendingSkipOutcome = "Unchanged: already at or below the cheapest external competitor";
                    Status = $"Item {position + 1} of {rows.Length} is already competitive; ignoring lower prices from your own retainers";
                    MoveTo(AutomationStep.CloseMarketResults,
                        TimeSpan.FromMilliseconds(Math.Max(350, config.ActionDelayMilliseconds)));
                    return;
                }

                Status = adjustmentPass
                    ? $"Applying Marketbuddy pricing against an external seller for item {position + 1} of {rows.Length}"
                    : $"Allagan Market checked item {position + 1} of {rows.Length}";
                MoveTo(adjustmentPass ? AutomationStep.ClickBestListing : AutomationStep.CloseMarketResults,
                    TimeSpan.FromMilliseconds(Math.Max(350, config.ActionDelayMilliseconds)));
                break;

            case AutomationStep.RecoverFromSearchThrottle:
                if (!ui.IsReady("RetainerSellList"))
                {
                    WaitOrFail(WindowTimeout, "the retainer sell list after the market-search cooldown");
                    return;
                }
                MoveTo(AutomationStep.SelectListing, TimeSpan.Zero);
                break;

            case AutomationStep.CloseMarketResults:
                if (!ui.Close("ItemSearchResult"))
                {
                    Fail("Could not close the market results after checking the listing.");
                    return;
                }
                MoveTo(AutomationStep.WaitForPriceWindowAfterCheck);
                break;

            case AutomationStep.WaitForPriceWindowAfterCheck:
                if (!ui.IsReady("RetainerSell"))
                {
                    WaitOrFail(WindowTimeout, "the Adjust Price window after checking");
                    return;
                }
                MoveTo(AutomationStep.ClosePriceWindow, TimeSpan.Zero);
                break;

            case AutomationStep.ClosePriceWindow:
                if (!ui.Close("RetainerSell"))
                {
                    Fail("Could not leave the Adjust Price window safely.");
                    return;
                }
                MoveTo(AutomationStep.WaitForListingAfterCheck);
                break;

            case AutomationStep.WaitForListingAfterCheck:
                if (!ui.IsReady("RetainerSellList"))
                {
                    WaitOrFail(WindowTimeout, "the retainer sell list after checking");
                    return;
                }
                if (skipAdjustmentAfterClose)
                {
                    skipAdjustmentAfterClose = false;
                    MoveTo(AutomationStep.VerifyEmptyAdjustment, TimeSpan.FromMilliseconds(400));
                }
                else
                {
                    MoveTo(AutomationStep.CaptureCheckedStatus,
                        TimeSpan.FromMilliseconds(Math.Max(250, config.ActionDelayMilliseconds)));
                }
                break;

            case AutomationStep.VerifyEmptyAdjustment:
                if (!ui.ShowListingRow(rows[position]) ||
                    !ui.TryGetInventoryListingSnapshot(originalListing, out RetainerListingSnapshot emptyResultListing))
                {
                    WaitOrFail(TimeSpan.FromSeconds(3), "the listing after an empty market result");
                    return;
                }

                if (emptyResultListing.Identity != originalListing.Identity ||
                    emptyResultListing.RetainerId != originalListing.RetainerId)
                {
                    if (currentRowRetryCount < MaxEmptyResultRetries)
                    {
                        currentRowRetryCount++;
                        Status = $"Listing context changed; retrying item {position + 1} of {rows.Length} ({currentRowRetryCount}/{MaxEmptyResultRetries})";
                        MoveTo(AutomationStep.SelectListing, TimeSpan.FromMilliseconds(300));
                        return;
                    }

                    failedAdjustmentCount++;
                    AddReport(originalListing, 0, emptyResultListing.UnitPrice,
                        "Failed: listing context changed before it could be verified");
                    log.Warning("Listing row {Row} changed identity after {Attempts} verified attempts",
                        rows[position], MaxEmptyResultRetries + 1);
                    AdvanceOrCompleteAdjustment(adjusted: false);
                    break;
                }

                if (currentCompetitor.UnitPrice == 0)
                    skippedNoCompetitorCount++;
                else
                    skippedAlreadyCompetitiveCount++;
                AddReport(originalListing, currentCompetitor.UnitPrice, emptyResultListing.UnitPrice,
                    pendingSkipOutcome,
                    currentCompetitor.RetainerName);
                AdvanceOrCompleteAdjustment(adjusted: false);
                break;

            case AutomationStep.CaptureCheckedStatus:
                AdvanceOrFinishCheckPass();
                break;

            case AutomationStep.ClickBestListing:
                ExternalListingState refreshedState = ui.GetBestExternalMarketListing(
                    currentListing.Identity,
                    out ExternalMarketListing refreshedCompetitor);
                if (refreshedState == ExternalListingState.Waiting)
                {
                    WaitOrFail(MarketDataTimeout, "a verified external market listing");
                    return;
                }
                if (refreshedState == ExternalListingState.None)
                {
                    RecordAssessment(currentListing, 0);
                    currentCompetitor = default;
                    pendingSkipOutcome = "Unchanged: no external competitor; owned listings ignored";
                    skipAdjustmentAfterClose = true;
                    MoveTo(AutomationStep.CloseMarketResults, TimeSpan.FromMilliseconds(350));
                    return;
                }

                currentCompetitor = refreshedCompetitor;
                RecordAssessment(currentListing, currentCompetitor.UnitPrice);
                if (!ui.ClickMarketListing(currentCompetitor.ResultIndex))
                {
                    Fail("The cheapest verified external market listing could not be selected.");
                    return;
                }
                MoveTo(AutomationStep.WaitForAdjustment);
                break;

            case AutomationStep.WaitForAdjustment:
                if (!ui.IsReady("RetainerSellList"))
                {
                    if (Expired(WindowTimeout))
                        Fail("Marketbuddy did not confirm the new price. Enable its Auto Input New Price and Auto Confirm New Price options.");
                    return;
                }
                MoveTo(AutomationStep.VerifyAdjustment, TimeSpan.FromMilliseconds(350));
                break;

            case AutomationStep.VerifyAdjustment:
                if (!ui.ShowListingRow(rows[position]) ||
                    !ui.TryGetInventoryListingSnapshot(originalListing, out RetainerListingSnapshot verifiedListing))
                {
                    WaitOrFail(PriceVerificationTimeout, "the saved retainer price");
                    return;
                }

                bool sameListing = verifiedListing.Identity == originalListing.Identity &&
                                   verifiedListing.RetainerId == originalListing.RetainerId;
                bool validPrice = sameListing && verifiedListing.UnitPrice > 0 &&
                                  verifiedListing.UnitPrice <= currentCompetitor.UnitPrice;
                if (validPrice)
                {
                    string outcome = verifiedListing.UnitPrice == originalListing.UnitPrice
                        ? "Verified: already priced at or below the external competitor"
                        : "Updated through Marketbuddy and verified";
                    AddReport(originalListing, currentCompetitor.UnitPrice, verifiedListing.UnitPrice, outcome,
                        currentCompetitor.RetainerName);
                    AdvanceOrCompleteAdjustment(adjusted: true);
                    break;
                }

                if (!Expired(PriceVerificationTimeout))
                    return;

                if (currentAdjustmentRetryCount < MaxAdjustmentRetries)
                {
                    currentAdjustmentRetryCount++;
                    currentListing = verifiedListing;
                    Status = $"Saved price was not verified; retrying item {position + 1} of {rows.Length} ({currentAdjustmentRetryCount}/{MaxAdjustmentRetries})";
                    MoveTo(AutomationStep.SelectListing, TimeSpan.FromMilliseconds(400));
                    return;
                }

                failedAdjustmentCount++;
                AddReport(originalListing, currentCompetitor.UnitPrice, verifiedListing.UnitPrice,
                    sameListing
                        ? $"Failed: final price was not competitive after {MaxAdjustmentRetries + 1} attempts"
                        : "Failed: listing identity changed during adjustment",
                    currentCompetitor.RetainerName);
                log.Warning("Listing row {Row} failed final-price verification after {Attempts} attempts",
                    rows[position], MaxAdjustmentRetries + 1);
                AdvanceOrCompleteAdjustment(adjusted: false);
                break;
        }
    }

    private void AdvanceOrFinishCheckPass()
    {
        position++;
        if (position < rows.Length)
        {
            MoveTo(AutomationStep.SelectListing);
            return;
        }

        rows = allRows;
        position = 0;
        checkedUndercuts.Clear();
        Status = "Market checks complete; confirming every Allagan Market row";
        MoveTo(AutomationStep.ShowListingForDiscovery, TimeSpan.Zero);
    }

    private void AdvanceOrCompleteAdjustment(bool adjusted)
    {
        if (adjusted)
            adjustedCount++;
        position++;
        currentRowRetryCount = 0;
        currentAdjustmentRetryCount = 0;
        currentSearchThrottleRetries = 0;
        currentListing = default;
        originalListing = default;
        currentCompetitor = default;
        pendingSkipOutcome = string.Empty;
        if (position < rows.Length)
        {
            MoveTo(AutomationStep.SelectListing, TimeSpan.FromMilliseconds(Math.Max(200, config.ActionDelayMilliseconds)));
            return;
        }

        string skipped = skippedNoCompetitorCount > 0
            ? $" {skippedNoCompetitorCount} listing(s) had no external competitor and were left unchanged."
            : string.Empty;
        string competitive = skippedAlreadyCompetitiveCount > 0
            ? $" {skippedAlreadyCompetitiveCount} listing(s) were already competitive once owned retainers were ignored."
            : string.Empty;
        string failed = failedAdjustmentCount > 0
            ? $" {failedAdjustmentCount} listing(s) failed ownership or final-price verification."
            : string.Empty;
        Complete($"Pricing adjustment complete: {adjustedCount} undercut listing(s) updated through Marketbuddy.{skipped}{competitive}{failed}");
    }

    private void RecordAssessment(RetainerListingSnapshot listing, uint cheapestExternalPrice)
    {
        config.MarketAssessments.RemoveAll(x =>
            x.RetainerId == listing.RetainerId && x.VisualIndex == listing.VisualIndex);
        config.MarketAssessments.Add(new OwnedAwareMarketAssessment
        {
            ItemId = listing.Identity.ItemId,
            IsHighQuality = listing.Identity.IsHighQuality,
            VisualIndex = listing.VisualIndex,
            RetainerId = listing.RetainerId,
            OwnedUnitPrice = listing.UnitPrice,
            CheapestExternalPrice = cheapestExternalPrice,
            CheckedAt = DateTime.UtcNow,
        });
        config.MarketAssessments.RemoveAll(x => x.CheckedAt < DateTime.UtcNow - TimeSpan.FromDays(1));
    }

    private void AddReport(
        RetainerListingSnapshot listing,
        uint competitorPrice,
        uint finalPrice,
        string outcome,
        string competitor = "")
    {
        var entry = new MarketRunReportEntry
        {
            Item = listing.ItemName,
            Retainer = listing.RetainerName,
            Quality = listing.Identity.IsHighQuality ? "HQ" : "NQ",
            OldPrice = listing.UnitPrice,
            Competitor = competitor,
            CompetitorPrice = competitorPrice,
            FinalPrice = finalPrice,
            Outcome = outcome,
        };
        runReport.Add(entry);
        log.Information(
            "Market result: {Item} ({Quality}) on {Retainer}: {OldPrice} -> {FinalPrice}; external {Competitor} at {CompetitorPrice}; {Outcome}",
            entry.Item, entry.Quality, entry.Retainer, entry.OldPrice, entry.FinalPrice,
            string.IsNullOrWhiteSpace(entry.Competitor) ? "none" : entry.Competitor,
            entry.CompetitorPrice, entry.Outcome);
    }

    private void SaveRunReport(string summary)
    {
        config.LastRunAt = DateTime.Now;
        config.LastRunSummary = summary;
        config.LastRunReport = runReport.TakeLast(50).ToList();
        config.Save();
    }

    private bool IsMarketbuddyLocked()
    {
        try
        {
            return pi.GetIpcSubscriber<string, bool>("Marketbuddy.IsLocked").InvokeFunc(null!);
        }
        catch
        {
            return false;
        }
    }

    private void OnErrorToast(ref SeString message, ref bool isHandled)
    {
        if (IsRunning && MarketSearchPacer.IsThrottleMessage(message.TextValue))
            searchThrottleRejected = true;
    }

    private void MoveTo(AutomationStep step, TimeSpan? delay = null)
    {
        Step = step;
        stepStartedUtc = DateTime.UtcNow;
        nextActionUtc = stepStartedUtc + (delay ?? TimeSpan.FromMilliseconds(config.ActionDelayMilliseconds));
    }

    private bool Expired(TimeSpan timeout) => DateTime.UtcNow - stepStartedUtc >= timeout;

    private void WaitOrFail(TimeSpan timeout, string awaited)
    {
        if (Expired(timeout))
            Fail($"Timed out waiting for {awaited}.");
    }

    private void Complete(string message)
    {
        Step = AutomationStep.Idle;
        Status = message;
        SaveRunReport(message);
        if (config.PrintCompletionToChat)
            chat.Print(message, Plugin.Tag);
    }

    private void Fail(string message)
    {
        TryReturnToSellList();
        Step = AutomationStep.Idle;
        Status = message;
        SaveRunReport(message);
        chat.PrintError(message, Plugin.Tag);
    }

    private void TryReturnToSellList()
    {
        ui.Close("ItemSearchResult");
        ui.Close("RetainerSell");
        ui.Close("ContextMenu");
    }

    private static string FriendlyStep(AutomationStep step) => step.ToString().Replace("WaitFor", "waiting for ");

    public void Dispose()
    {
        framework.Update -= OnFrameworkUpdate;
        toasts.ErrorToast -= OnErrorToast;
    }
}
