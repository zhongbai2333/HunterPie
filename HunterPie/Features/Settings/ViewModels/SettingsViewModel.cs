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
    private PresetOptionViewModel? _activePreset;
    private bool _restoringPresetSelection;

    public ObservableCollection<GameProcessType> ConfigurableGames { get; }
    public Observable<GameProcessType> SelectedGameConfiguration { get; }
    public UpdateFetchStatus UpdateStatus { get; set => SetValue(ref field, value); } = UpdateFetchStatus.Fetching;
    public int CurrentTabIndex { get; set => SetValue(ref field, value); }

    private ObservableCollection<IConfigurationCategory> _categories;
    public ObservableCollection<IConfigurationCategory> Categories { get => _categories; set => SetValue(ref _categories, value); }
    public DateTime SynchronizedAt { get; set => SetValue(ref field, value); } = DateTime.Now;
    public ObservableCollection<PresetOptionViewModel> Presets { get; } = new();
    public PresetOptionViewModel? SelectedPreset
    {
        get;
        set
        {
            SetValue(ref field, value);
            CanUsePreset = value is not null;
        }
    }
    public bool CanUsePreset { get; set => SetValue(ref field, value); }
    public bool IsRestoringPresetSelection => _restoringPresetSelection;
    public bool HasUnsavedPresetChanges => _activePreset?.IsDirty == true;
    public string ActivePresetName => _activePreset?.Name ?? string.Empty;
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
            RememberSelectedPreset(preset);
            RefreshPresets();
            PresetStatus = PresetLocalization.Format("SAVED", preset.Name);
        });
    }

    public bool ApplyPreset(PresetOptionViewModel option)
    {
        PresetOptionViewModel? previousPreset = _activePreset;
        try
        {
            GameConfigurationPreset preset = option.Preset;
            _activePreset = option;
            ConfigManager.RunBatched(() =>
            {
                Store.Apply(preset, ClientConfigHelper.GetGameConfigBy(preset.Game));
                RememberSelectedPreset(preset);
            });
            if (!ReferenceEquals(previousPreset, option))
                previousPreset?.SetDirty(false);
            PresetName = preset.Name;
            PresetStatus = PresetLocalization.Format("APPLIED", preset.Name);
            RefreshSelectedPresetMatch();
            return true;
        }
        catch (Exception exception)
        {
            _activePreset = previousPreset;
            PresetStatus = PresetLocalization.Error(exception);
            RestoreSelectedPreset();
            return false;
        }
    }

    public void DeleteSelectedPreset()
    {
        RunPresetAction(() =>
        {
            GameConfigurationPreset preset = RequireSelection();
            Store.Delete(preset);
            if (LastSelectedPresetNames.TryGetValue(preset.Game, out string? selectedName)
                && string.Equals(selectedName, preset.Name, StringComparison.OrdinalIgnoreCase))
            {
                LastSelectedPresetNames.Remove(preset.Game);
                ConfigManager.Save(ClientConfig.CONFIG_NAME);
            }
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
                if (SelectedGameConfiguration.Value == preset.Game)
                    RefreshPresets();
                else
                {
                    SelectedGameConfiguration.Value = preset.Game;
                    ChangeSettingsGroup();
                }
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
        is { } option ? option.Preset : throw new InvalidOperationException("Select a preset first.");

    public void RestoreSelectedPreset()
    {
        _restoringPresetSelection = true;
        try
        {
            SelectedPreset = _activePreset;
        }
        finally
        {
            _restoringPresetSelection = false;
        }
    }

    public bool IsActivePreset(PresetOptionViewModel option) => ReferenceEquals(_activePreset, option);

    public void RefreshSelectedPresetMatch()
    {
        if (_activePreset is null)
            return;

        try
        {
            bool wasDirty = _activePreset.IsDirty;
            bool isDirty = !Store.MatchesCurrent(
                _activePreset.Preset,
                ClientConfigHelper.GetGameConfigBy(_activePreset.Preset.Game));
            _activePreset.SetDirty(isDirty);
            if (isDirty)
                PresetStatus = PresetLocalization.Format("MODIFIED", _activePreset.Name);
            else if (wasDirty)
                PresetStatus = PresetLocalization.Format("MATCHES", _activePreset.Name);
        }
        catch (Exception exception)
        {
            _activePreset.SetDirty(true);
            PresetStatus = PresetLocalization.Error(exception);
        }
    }

    private static void RememberSelectedPreset(GameConfigurationPreset preset)
    {
        LastSelectedPresetNames[preset.Game] = preset.Name;
        ConfigManager.Save(ClientConfig.CONFIG_NAME);
    }

    private static Dictionary<GameProcessType, string> LastSelectedPresetNames =>
        ClientConfig.Config.Client.LastSelectedConfigurationPresets ??= new();

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
        _restoringPresetSelection = true;
        try
        {
            SelectedPreset = null;
            _activePreset = null;
            Presets.Clear();
            if (_presetStore is null)
                return;

            foreach (GameConfigurationPreset preset in _presetStore.Presets
                .Where(it => it.Game == SelectedGameConfiguration.Value)
                .OrderBy(it => it.Name))
                Presets.Add(new(preset));

            LastSelectedPresetNames.TryGetValue(
                SelectedGameConfiguration.Value, out string? selectedName);
            _activePreset = Presets.FirstOrDefault(it =>
                string.Equals(it.Name, selectedName, StringComparison.OrdinalIgnoreCase));
            SelectedPreset = _activePreset;
            PresetName = _activePreset?.Name ?? string.Empty;
            PresetStatus = PresetLocalization.Get("INSTRUCTIONS");
            RefreshSelectedPresetMatch();
        }
        finally
        {
            _restoringPresetSelection = false;
        }
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