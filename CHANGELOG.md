# Changelog

## 1.0.0.4

- Prevent stale empty market data from being mistaken for a competitor disappearing during adjustment.
- Recheck Allagan Market after an empty result and retry listings that remain red.
- Report bounded adjustment failures explicitly instead of incorrectly counting them as competitor-free.

## 1.0.0.3

- Fixed checks stalling when identical items appear in consecutive retainer slots.
- Accept populated market rows immediately even when the game's duplicate-search flag does not reset correctly.
- Query each unique item and quality combination once, then verify every matching retainer row for undercuts.
- Give Allagan Market a balanced observation interval before closing and classifying each result.

## 1.0.0.2

- Wait for the game's definitive market-response state before deciding that a listing has no competitors.
- Inspect every retainer row, including scrolled-off rows, before adjusting undercuts.
- Speed up normal checking and repricing steps while retaining verified window transitions.
- Replace the three oversized toolbar labels with compact icon buttons and descriptive hover help.

## 1.0.0.1

- Treat an empty market comparison as a successful undercut check instead of waiting until timeout.
- Leave a listing unchanged and continue safely if its competitors disappear before the adjustment pass.

## 1.0.0.0

- Added retainer-list controls for checking undercuts, adjusting confirmed undercuts, and running both steps together.
- Integrated Allagan Market's red undercut state and Marketbuddy's existing pricing behavior.
- Added dependency status and installation controls for Marketbuddy and Allagan Market.
- Added guarded window transitions, timeouts, progress status, cancellation, and chat summaries.
