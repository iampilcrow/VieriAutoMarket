using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace VieriAutoMarket;

internal sealed class MarketAutomationController : IDisposable
{
    private static readonly TimeSpan WindowTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan MarketDataTimeout = TimeSpan.FromSeconds(25);
    private static readonly TimeSpan EmptyResultConfirmation = TimeSpan.FromMilliseconds(1500);
    private const int MaxEmptyResultRetries = 2;

    private readonly IFramework framework;
    private readonly IChatGui chat;
    private readonly IPluginLog log;
    private readonly IDalamudPluginInterface pi;
    private readonly Configuration config;
    private readonly DependencyService dependencies;
    private readonly RetainerMarketUi ui;
    private readonly List<int> checkedUndercuts = [];
    private int[] allRows = [];
    private int[] rows = [];
    private int position;
    private DateTime nextActionUtc;
    private DateTime stepStartedUtc;
    private bool adjustmentPass;
    private bool skipAdjustmentAfterClose;
    private int adjustedCount;
    private int skippedNoCompetitorCount;
    private int failedAdjustmentCount;
    private int currentRowRetryCount;

    internal MarketAutomationController(IFramework framework, IChatGui chat, IPluginLog log,
        IDalamudPluginInterface pi, Configuration config, DependencyService dependencies, RetainerMarketUi ui)
    {
        this.framework = framework;
        this.chat = chat;
        this.log = log;
        this.pi = pi;
        this.config = config;
        this.dependencies = dependencies;
        this.ui = ui;
        framework.Update += OnFrameworkUpdate;
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
        checkedUndercuts.Clear();
        adjustedCount = 0;
        skippedNoCompetitorCount = 0;
        failedAdjustmentCount = 0;
        currentRowRetryCount = 0;
        skipAdjustmentAfterClose = false;
        adjustmentPass = mode == AutomationMode.Adjust;
        allRows = AutomationPlan.ListingRows(listingCount);
        rows = mode == AutomationMode.Adjust
            ? allRows
            : ui.GetUniqueMarketCheckRows(listingCount);
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
                if (!ui.HasAdjustPriceEntry() || !ui.SelectAdjustPrice())
                {
                    Fail("The selected listing did not offer Adjust Price.");
                    return;
                }
                MoveTo(AutomationStep.WaitForPriceWindow);
                break;

            case AutomationStep.WaitForPriceWindow:
                if (!ui.IsReady("RetainerSell"))
                {
                    WaitOrFail(WindowTimeout, "the Adjust Price window");
                    return;
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

                if (marketResults == MarketResultsState.ReadyWithoutListings)
                {
                    if (!Expired(EmptyResultConfirmation))
                        return;
                    skipAdjustmentAfterClose = adjustmentPass;
                    Status = adjustmentPass
                        ? $"Verifying the empty result for item {position + 1} of {rows.Length}"
                        : $"Allagan Market checked item {position + 1} of {rows.Length}; no competing listings";
                    MoveTo(AutomationStep.CloseMarketResults,
                        TimeSpan.FromMilliseconds(Math.Max(250, config.ActionDelayMilliseconds)));
                    return;
                }

                Status = adjustmentPass
                    ? $"Applying Marketbuddy pricing to item {position + 1} of {rows.Length}"
                    : $"Allagan Market checked item {position + 1} of {rows.Length}";
                MoveTo(adjustmentPass ? AutomationStep.ClickBestListing : AutomationStep.CloseMarketResults,
                    TimeSpan.FromMilliseconds(Math.Max(250, config.ActionDelayMilliseconds)));
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
                ListingPriceState verifiedState = ui.GetListingPriceState(rows[position]);
                if (verifiedState == ListingPriceState.Unknown)
                {
                    WaitOrFail(TimeSpan.FromSeconds(3), "Allagan Market to verify the listing after an empty result");
                    return;
                }

                if (verifiedState is ListingPriceState.Undercut or ListingPriceState.NeedsCheck)
                {
                    if (currentRowRetryCount < MaxEmptyResultRetries)
                    {
                        currentRowRetryCount++;
                        Status = $"Listing is still undercut; retrying item {position + 1} of {rows.Length} ({currentRowRetryCount}/{MaxEmptyResultRetries})";
                        MoveTo(AutomationStep.SelectListing, TimeSpan.FromMilliseconds(300));
                        return;
                    }

                    failedAdjustmentCount++;
                    log.Warning("Listing row {Row} remained undercut after {Attempts} verified attempts",
                        rows[position], MaxEmptyResultRetries + 1);
                    AdvanceOrCompleteAdjustment(adjusted: false);
                    break;
                }

                skippedNoCompetitorCount++;
                AdvanceOrCompleteAdjustment(adjusted: false);
                break;

            case AutomationStep.CaptureCheckedStatus:
                AdvanceOrFinishCheckPass();
                break;

            case AutomationStep.ClickBestListing:
                if (!ui.ClickBestMarketListing())
                {
                    Fail("The best-priced market listing could not be selected.");
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
                AdvanceOrCompleteAdjustment(adjusted: true);
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
        if (position < rows.Length)
        {
            MoveTo(AutomationStep.SelectListing, TimeSpan.FromMilliseconds(Math.Max(200, config.ActionDelayMilliseconds)));
            return;
        }

        string skipped = skippedNoCompetitorCount > 0
            ? $" {skippedNoCompetitorCount} listing(s) no longer had a competitor and were left unchanged."
            : string.Empty;
        string failed = failedAdjustmentCount > 0
            ? $" {failedAdjustmentCount} listing(s) remained undercut after {MaxEmptyResultRetries + 1} verified attempts."
            : string.Empty;
        Complete($"Pricing adjustment complete: {adjustedCount} undercut listing(s) updated through Marketbuddy.{skipped}{failed}");
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
        if (config.PrintCompletionToChat)
            chat.Print(message, Plugin.Tag);
    }

    private void Fail(string message)
    {
        TryReturnToSellList();
        Step = AutomationStep.Idle;
        Status = message;
        chat.PrintError(message, Plugin.Tag);
    }

    private void TryReturnToSellList()
    {
        ui.Close("ItemSearchResult");
        ui.Close("RetainerSell");
        ui.Close("ContextMenu");
    }

    private static string FriendlyStep(AutomationStep step) => step.ToString().Replace("WaitFor", "waiting for ");

    public void Dispose() => framework.Update -= OnFrameworkUpdate;
}
