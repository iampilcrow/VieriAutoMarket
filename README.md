# VieriAutoMarket

VieriAutoMarket adds guarded, one-click workflows to the retainer sell list:

- **Check For Undercuts** opens each listing's price comparison so Allagan Market can refresh its status.
- **Adjust Undercut Pricing** updates listings currently marked red by Allagan Market against the cheapest matching external seller.
- **Auto Check/Adjust Undercuts** refreshes every listing, then updates only confirmed external undercuts.

Marketbuddy remains responsible for the user's configured gil/percentage undercut, rounding, price input, and confirmation behavior. VieriAutoMarket never invents a separate price rule.

VieriAutoMarket identifies every retainer owned by the current player and excludes all of them from competitor selection. Duplicate listings use the same external market price, and HQ/NQ listings are evaluated separately. Saved retainer prices are verified before an adjustment advances.

## Requirements

- Marketbuddy
- Allagan Market, with retainer sell-list highlighting enabled

Both dependencies are shown in `/vamarket` and can be installed from that window.

## Safety

The automation only starts from an open retainer sell list. Every expected game window is verified, each wait is bounded, and an unexpected state stops the operation and returns toward the sell list without changing additional prices. If complete retainer ownership data is unavailable, repricing waits and then stops safely rather than risking a self-undercut.

The settings window keeps a local report for the last run, including the item, owned retainer, quality, old price, external seller and price, verified final price, and outcome.

## Commands

- `/vamarket` — settings and dependency status
- `/vamarket check`
- `/vamarket adjust`
- `/vamarket auto`
- `/vamarket stop`
