# Changelog

## 1.0.0.1

- Treat an empty market comparison as a successful undercut check instead of waiting until timeout.
- Leave a listing unchanged and continue safely if its competitors disappear before the adjustment pass.

## 1.0.0.0

- Added retainer-list controls for checking undercuts, adjusting confirmed undercuts, and running both steps together.
- Integrated Allagan Market's red undercut state and Marketbuddy's existing pricing behavior.
- Added dependency status and installation controls for Marketbuddy and Allagan Market.
- Added guarded window transitions, timeouts, progress status, cancellation, and chat summaries.
