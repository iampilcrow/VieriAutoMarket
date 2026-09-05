using Dalamud.Configuration;
using Dalamud.Plugin;

namespace VieriAutoMarket;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;
    public float ToolbarOffsetX { get; set; } = 504f;
    public float ToolbarOffsetY { get; set; } = 10f;
    public int ActionDelayMilliseconds { get; set; } = 100;
    public bool PrintCompletionToChat { get; set; } = true;
    public DateTime LastRunAt { get; set; }
    public string LastRunSummary { get; set; } = "No completed run yet.";
    public List<MarketRunReportEntry> LastRunReport { get; set; } = [];
    public List<OwnedAwareMarketAssessment> MarketAssessments { get; set; } = [];

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    internal void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    internal void ApplyMigrations()
    {
        bool changed = false;
        if (Version < 2)
        {
            ActionDelayMilliseconds = 100;
            Version = 2;
            changed = true;
        }

        if (Version < 3)
        {
            ToolbarOffsetX += 192f;
            Version = 3;
            changed = true;
        }

        if (Version < 4)
        {
            ToolbarOffsetX -= 48f;
            Version = 4;
            changed = true;
        }

        LastRunReport ??= [];
        MarketAssessments ??= [];
        if (changed)
            Save();
    }

    internal void Save() => pluginInterface?.SavePluginConfig(this);
}
