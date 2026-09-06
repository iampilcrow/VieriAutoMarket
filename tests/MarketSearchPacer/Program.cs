var origin = new DateTime(2026, 9, 5, 12, 0, 0, DateTimeKind.Utc);
var pacer = new VieriAutoMarket.MarketSearchPacer(
    TimeSpan.FromMilliseconds(2500),
    TimeSpan.FromSeconds(4));

if (!pacer.CanStart(origin)) throw new Exception("First search was delayed");
pacer.RecordStarted(origin);
if (pacer.CanStart(origin.AddMilliseconds(2499))) throw new Exception("Overlapping search was allowed");
if (pacer.Remaining(origin.AddSeconds(1)) != TimeSpan.FromMilliseconds(1500)) throw new Exception("Cooldown remainder was wrong");
if (!pacer.CanStart(origin.AddMilliseconds(2500))) throw new Exception("Search stayed blocked after cooldown");
pacer.RecordRejected(origin.AddMilliseconds(2600));
if (pacer.CanStart(origin.AddMilliseconds(6599))) throw new Exception("Rejected search backoff was too short");
if (!pacer.CanStart(origin.AddMilliseconds(6600))) throw new Exception("Rejected search backoff did not expire");
if (!VieriAutoMarket.MarketSearchPacer.IsThrottleMessage("Please wait and try your search again.")) throw new Exception("Throttle message was not recognized");
if (VieriAutoMarket.MarketSearchPacer.IsThrottleMessage("Market search complete.")) throw new Exception("Unrelated message was treated as a throttle");

Console.WriteLine("8 market-search pacing and rejection checks passed.");
