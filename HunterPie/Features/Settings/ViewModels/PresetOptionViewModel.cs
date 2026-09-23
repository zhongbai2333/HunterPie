using HunterPie.Core.Client.ConfigurationPresets;
using HunterPie.UI.Architecture;

namespace HunterPie.Features.Settings.ViewModels;

internal sealed class PresetOptionViewModel(GameConfigurationPreset preset) : ViewModel
{
    public GameConfigurationPreset Preset { get; } = preset;
    public string Name => Preset.Name;
    public bool IsDirty { get; private set => SetValue(ref field, value); }
    public string DisplayName { get; private set => SetValue(ref field, value); } = preset.Name;

    public void SetDirty(bool isDirty)
    {
        if (IsDirty == isDirty)
            return;

        IsDirty = isDirty;
        DisplayName = isDirty ? $"*{Name}" : Name;
    }
}