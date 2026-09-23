using HunterPie.Core.Domain.Enums;
using Newtonsoft.Json.Linq;

namespace HunterPie.Core.Client.ConfigurationPresets;

/// <summary>A snapshot of one game's complete configuration.</summary>
public sealed class GameConfigurationPreset
{
    public int FormatVersion { get; set; } = 1;
    public string Name { get; set; } = string.Empty;
    public GameProcessType Game { get; set; }
    public JObject Configuration { get; set; } = new();
}
