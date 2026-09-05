using Dalamud.Configuration;
using Dalamud.Plugin;

namespace VieriAutoMarket;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;
    public float ToolbarOffsetX { get; set; } = 360f;
    public float ToolbarOffsetY { get; set; } = 10f;
    public int ActionDelayMilliseconds { get; set; } = 100;
    public bool PrintCompletionToChat { get; set; } = true;

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    internal void Initialize(IDalamudPluginInterface pi) => pluginInterface = pi;

    internal void ApplyMigrations()
    {
        if (Version < 2)
        {
            ActionDelayMilliseconds = 100;
            Version = 2;
            Save();
        }
    }

    internal void Save() => pluginInterface?.SavePluginConfig(this);
}
