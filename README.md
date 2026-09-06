# VieriAutoMarket

VieriAutoMarket adds guarded, one-click workflows to the retainer sell list:

- **Check For Undercuts** opens each listing's price comparison so Allagan Market can refresh its status.
- **Adjust Undercut Pricing** verifies every active listing and updates external undercuts or owned-retainer price mismatches against the correct market reference.
- **Auto Check/Adjust Undercuts** refreshes every listing, then updates every confirmed external undercut or owned-retainer price mismatch.

Marketbuddy remains responsible for the user's configured gil/percentage undercut, rounding, price input, and confirmation behavior. VieriAutoMarket never invents a separate price rule.

VieriAutoMarket identifies every retainer owned by the current player. When another owned retainer has the market-lowest price for the same item and quality, the current listing matches that price exactly instead of undercutting it. If an external seller is lower, Marketbuddy's configured undercut rule remains authoritative. HQ/NQ listings are evaluated separately, prices are never raised by owned-retainer matching, and every saved price is verified before an adjustment advances.

## Requirements

- Marketbuddy
- Allagan Market, with retainer sell-list highlighting enabled

Both dependencies are shown in `/vamarket` and can be installed from that window.

## Safety

The automation only starts from an open retainer sell list. Every expected game window is verified, each wait is bounded, and an unexpected state stops the operation and returns toward the sell list without changing additional prices. If complete retainer ownership data is unavailable, repricing waits and then stops safely rather than risking a self-undercut.

The settings window keeps a local report for the last run, including the item, owned retainer, quality, old price, reference retainer and price, verified final price, and outcome.

## Commands

- `/vamarket` — settings and dependency status
- `/vamarket check`
- `/vamarket adjust`
- `/vamarket auto`
- `/vamarket stop`
