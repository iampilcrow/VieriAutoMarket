using static VieriAutoMarket.MarketPricingAction;
using VieriAutoMarket;

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

if (MarketPricingDecision.Choose(200, 0, 100) != MatchOtherOwned) throw new Exception("Owned-only lowest price was not matched");
if (MarketPricingDecision.Choose(200, 150, 100) != MatchOtherOwned) throw new Exception("Market-lowest owned price was not preferred");
if (MarketPricingDecision.Choose(200, 100, 150) != UndercutExternal) throw new Exception("Lower external seller was not undercut");
if (MarketPricingDecision.Choose(200, 100, 100) != MatchOtherOwned) throw new Exception("Owned listing tied for lowest was not matched");
if (MarketPricingDecision.Choose(100, 0, 100) != None) throw new Exception("Equal owned price caused a redundant update");
if (MarketPricingDecision.Choose(90, 0, 100) != None) throw new Exception("A lower current price was raised");
if (MarketPricingDecision.Choose(90, 100, 0) != None) throw new Exception("Competitive external price caused an update");
if (MarketPricingDecision.Choose(200, 0, 0) != None) throw new Exception("Missing references caused an update");

Console.WriteLine("16 market pacing and owned-retainer pricing checks passed.");
