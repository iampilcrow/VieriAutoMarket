namespace VieriAutoMarket;

internal sealed class MarketSearchPacer(
    TimeSpan? minimumInterval = null,
    TimeSpan? rejectionBackoff = null)
{
    private readonly TimeSpan minimumInterval = minimumInterval ?? TimeSpan.FromMilliseconds(2500);
    private readonly TimeSpan rejectionBackoff = rejectionBackoff ?? TimeSpan.FromSeconds(4);
    private DateTime nextAllowedUtc = DateTime.MinValue;

    internal bool CanStart(DateTime nowUtc) => nowUtc >= nextAllowedUtc;

    internal TimeSpan Remaining(DateTime nowUtc) =>
        nowUtc >= nextAllowedUtc ? TimeSpan.Zero : nextAllowedUtc - nowUtc;

    internal void RecordStarted(DateTime nowUtc) =>
        nextAllowedUtc = nowUtc + minimumInterval;

    internal void RecordRejected(DateTime nowUtc)
    {
        DateTime backedOffUntil = nowUtc + rejectionBackoff;
        if (backedOffUntil > nextAllowedUtc)
            nextAllowedUtc = backedOffUntil;
    }

    internal static bool IsThrottleMessage(string message) =>
        message.Contains("Please wait and try your search again", StringComparison.OrdinalIgnoreCase);
}
