using HunterPie.Core.Architecture;
using HunterPie.Core.Client;
using HunterPie.Core.Client.ConfigurationPresets;
using HunterPie.Core.Domain.Enums;
using HunterPie.Core.Extensions;
using HunterPie.Core.Search;
using HunterPie.Features.Settings.Localization;
using HunterPie.Integrations.Poogie.Common.Models;
using HunterPie.Integrations.Poogie.Version;
using HunterPie.Integrations.Poogie.Version.Models;
using HunterPie.UI.Architecture;
using HunterPie.UI.Settings.Models;
using HunterPie.UI.Settings.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HunterPie.Features.Settings.ViewModels;

internal class SettingsViewModel : ViewModel
{
    private readonly PoogieVersionConnector _connector;
    private readonly GameConfigurationPresetStore? _presetStore;
    private readonly Dictionary<GameProcessType, ObservableCollection<IConfigurationCategory>> _configurations;

    public ObservableCollection<GameProcessType> ConfigurableGames { get; }
    public Observable<GameProcessType> SelectedGameConfiguration { get; }
    public UpdateFetchStatus UpdateStatus { get; set => SetValue(ref field, value); } = UpdateFetchStatus.Fetching;
    public int CurrentTabIndex { get; set => SetValue(ref field, value); }

    private ObservableCollection<IConfigurationCategory> _categories;
    public ObservableCollection<IConfigurationCategory> Categories { get => _categories; set => SetValue(ref _categories, value); }
    public DateTime SynchronizedAt { get; set => SetValue(ref field, value); } = DateTime.Now;
    public ObservableCollection<GameConfigurationPreset> Presets { get; } = new();
    public GameConfigurationPreset? SelectedPreset
    {
        get;
        set
        {
            SetValue(ref field, value);
            CanUsePreset = value is not null;
        }
    }
    public bool CanUsePreset { get; set => SetValue(ref field, value); }
    public string PresetName { get; set => SetValue(ref field, value); } = string.Empty;
    public string PresetStatus { get; set => SetValue(ref field, value); } = string.Empty;

    public SettingsViewModel(
        Dictionary<GameProcessType, ObservableCollection<ConfigurationCategoryGroup>> configurations,
        ObservableCollection<GameProcessType> configurableGames,
        Observable<GameProcessType> currentConfiguredGame,
        PoogieVersionConnector connector)
    {
        _configurations = BuildConfigurationViewModels(configurations);
        ConfigurableGames = configurableGames;
        SelectedGameConfiguration = currentConfiguredGame;
        _connector = connector;
        _categories = _configurations[currentConfiguredGame.Value];

        try
        {
            _presetStore = new GameConfigurationPresetStore(ClientInfo.GetPathFor("configuration-presets.json"));
            RefreshPresets();
        }
        catch (Exception exception)
        {
            PresetStatus = PresetLocalization.Format("LOAD_FAILED", PresetLocalization.Error(exception));
        }

        NavigateToFirstTab();
    }

    public async void FetchVersion()
    {
        UpdateStatus = UpdateFetchStatus.Fetching;
        PoogieResult<VersionResponse> response = await _connector.Latest();

        switch (response)
        {
            case { Error: { } }:
                UpdateStatus = UpdateFetchStatus.Error;
                return;
            case { Response: { } versionResponse }:
                var version = new Version(versionResponse.LatestVersion);
                UpdateStatus = ClientInfo.IsVersionGreaterOrEq(version)
                    ? UpdateFetchStatus.Latest
                    : UpdateFetchStatus.NeedsUpdate;
                break;
        }
    }

    public void Search(string query)
    {
        Categories
            .TryCast<ConfigurationCategoryTab>()
            .Select(it => it.Category)
            .SelectMany(it => it.Groups.SelectMany(group => group.Properties))
            .ForEach(it =>
            {
                if (it is not ConfigurationPropertyViewModel vm)
                    return;

                vm.IsMatch = SearchEngine.IsMatch($"{vm.Name} {vm.Description}", query);
            });
    }

    public void ChangeSettingsGroup()
    {
        ObservableCollection<IConfigurationCategory> newCategories = _configurations[SelectedGameConfiguration];

        if (Categories == newCategories)
            return;

        Categories = newCategories;
        NavigateToFirstTab();
        RefreshPresets();
    }

    public void SaveCurrentPreset()
    {
        if (string.IsNullOrWhiteSpace(PresetName))
        {
            PresetStatus = PresetLocalization.Get("NAME_REQUIRED");
            return;
        }

        RunPresetAction(() =>
        {
            GameConfigurationPreset preset = Store.SaveCurrent(
                PresetName,
                SelectedGameConfiguration.Value,
                ClientConfigHelper.GetGameConfigBy(SelectedGameConfiguration.Value));
            RefreshPresets();
            PresetName = preset.Name;
            PresetStatus = PresetLocalization.Format("SAVED", preset.Name);
        });
    }

    public void ApplyPreset(GameConfigurationPreset preset)
    {
        RunPresetAction(() =>
        {
            ConfigManager.RunBatched(() =>
            {
                Store.Apply(preset, ClientConfigHelper.GetGameConfigBy(preset.Game));
                ConfigManager.Save(ClientConfig.CONFIG_NAME);
            });
            PresetName = preset.Name;
            PresetStatus = PresetLocalization.Format("APPLIED", preset.Name);
        });
    }

    public void DeleteSelectedPreset()
    {
        RunPresetAction(() =>
        {
            GameConfigurationPreset preset = RequireSelection();
            Store.Delete(preset);
            RefreshPresets();
            PresetStatus = PresetLocalization.Format("DELETED", preset.Name);
        });
    }

    public void ImportPreset(string path)
    {
        RunPresetAction(() =>
        {
            GameConfigurationPreset preset = Store.Import(path);
            if (ConfigurableGames.Contains(preset.Game))
            {
                SelectedGameConfiguration.Value = preset.Game;
                ChangeSettingsGroup();
                RefreshPresets();
            }

            PresetStatus = PresetLocalization.Format("IMPORTED", preset.Name);
        });
    }

    public void ExportSelectedPreset(string path)
    {
        RunPresetAction(() =>
        {
            GameConfigurationPreset preset = RequireSelection();
            Store.Export(preset, path);
            PresetStatus = PresetLocalization.Format("EXPORTED", preset.Name);
        });
    }

    private GameConfigurationPresetStore Store => _presetStore
        ?? throw new InvalidOperationException("Preset storage is unavailable.");

    private GameConfigurationPreset RequireSelection() => SelectedPreset
        ?? throw new InvalidOperationException("Select a preset first.");

    private void RunPresetAction(Action action)
    {
        try
        {
            action();
        }
        catch (Exception exception)
        {
            PresetStatus = PresetLocalization.Error(exception);
        }
    }

    private void RefreshPresets()
    {
        SelectedPreset = null;
        Presets.Clear();
        if (_presetStore is null)
            return;

        foreach (GameConfigurationPreset preset in _presetStore.Presets
            .Where(it => it.Game == SelectedGameConfiguration.Value)
            .OrderBy(it => it.Name))
            Presets.Add(preset);

        PresetName = string.Empty;
        PresetStatus = PresetLocalization.Get("INSTRUCTIONS");
    }

    public void ExecuteUpdate() => App.Restart();

    private Dictionary<GameProcessType, ObservableCollection<IConfigurationCategory>> BuildConfigurationViewModels(
        Dictionary<GameProcessType, ObservableCollection<ConfigurationCategoryGroup>> configurations
    )
    {
        return configurations.ToDictionary(
            keySelector: it => it.Key,
            elementSelector: it =>
                it.Value.SelectMany(group =>
                {
                    List<IConfigurationCategory> viewModels = new(group.Categories.Count + 1)
                    {
                        new ConfigurationCategoryTitle { Title = group.Name }
                    };

                    viewModels.AddRange(group.Categories.Select(category => new ConfigurationCategoryTab { Category = category }));

                    return viewModels;
                }).ToObservableCollection()
        );
    }

    private void NavigateToFirstTab()
    {
        IConfigurationCategory? firstTab = Categories.FirstOrDefault(it => it is ConfigurationCategoryTab);

        if (firstTab is null)
            return;

        int index = Categories.IndexOf(firstTab);

        CurrentTabIndex = index;
    }
}
