using HunterPie.Core.Architecture;
using HunterPie.Core.Client;
using HunterPie.Core.Client.Events;
using HunterPie.Core.Domain.Dialog;
using HunterPie.Features.Settings.Localization;
using HunterPie.Features.Settings.ViewModels;
using HunterPie.UI.Architecture.Bindings;
using HunterPie.UI.Architecture.Tree;
using HunterPie.UI.Controls.TextBox.Events;
using HunterPie.UI.Settings.Converter.Model;
using HunterPie.UI.Settings.ViewModels;
using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AppResources = HunterPie.UI.Assets.Application.Resources;

namespace HunterPie.Features.Settings.Views;
/// <summary>
/// Interaction logic for SettingsView.xaml
/// </summary>
public partial class SettingsView : UserControl
{
    private readonly PropertyCondition _defaultCondition = new PropertyCondition(
        Property: new Observable<bool>(true),
        Value: true
    );
    private readonly Storyboard _disableSettingComponentAnimation;
    private readonly Storyboard _enableSettingComponentAnimation;
    private readonly DispatcherTimer _presetMatchRefreshTimer = new() { Interval = System.TimeSpan.FromMilliseconds(200) };
    private bool _isWatchingConfiguration;

    public SettingsView()
    {
        InitializeComponent();

        _disableSettingComponentAnimation = AppResources.Get<Storyboard>("Animations.Scale.Hide");
        _enableSettingComponentAnimation = AppResources.Get<Storyboard>("Animations.Scale.Show");
        _presetMatchRefreshTimer.Tick += OnPresetMatchRefreshTimerTick;
    }

    private void OnSearchTextChanged(object? sender, SearchTextChangedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        vm.Search(e.Text);
    }

    private void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        vm.ChangeSettingsGroup();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        if (!_isWatchingConfiguration)
        {
            ConfigManager.OnSaved += OnConfigurationChanged;
            ConfigManager.OnSync += OnConfigurationChanged;
            _isWatchingConfiguration = true;
        }
        SchedulePresetMatchRefresh();
        vm.FetchVersion();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_isWatchingConfiguration)
            return;

        ConfigManager.OnSaved -= OnConfigurationChanged;
        ConfigManager.OnSync -= OnConfigurationChanged;
        _presetMatchRefreshTimer.Stop();
        _isWatchingConfiguration = false;
    }

    private void OnConfigurationChanged(object? sender, ConfigSaveEventArgs e)
    {
        if (!_isWatchingConfiguration || Path.GetFileName(e.Path) != ClientConfig.CONFIG_NAME)
            return;

        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => OnConfigurationChanged(sender, e));
            return;
        }

        SchedulePresetMatchRefresh();
    }

    private void SchedulePresetMatchRefresh()
    {
        _presetMatchRefreshTimer.Stop();
        _presetMatchRefreshTimer.Start();
    }

    private void OnPresetMatchRefreshTimerTick(object? sender, System.EventArgs e)
    {
        _presetMatchRefreshTimer.Stop();
        if (DataContext is SettingsViewModel vm)
            vm.RefreshSelectedPresetMatch();
    }

    private void OnRetryVersionFetchClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        vm.FetchVersion();
    }

    private void OnDownloadVersionClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        vm.ExecuteUpdate();
    }

    private void OnSavePresetClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        if (vm.Presets.Any(it => string.Equals(it.Name, vm.PresetName.Trim(), System.StringComparison.OrdinalIgnoreCase))
            && DialogManager.Warn(PresetLocalization.ConfirmationTitle, PresetLocalization.Get("REPLACE_CONFIRM"),
                NativeDialogButtons.Accept | NativeDialogButtons.Cancel) != NativeDialogResult.Accept)
            return;

        vm.SaveCurrentPreset();
    }

    private void OnPresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm
            || vm.IsRestoringPresetSelection
            || e.AddedItems.Count != 1
            || e.AddedItems[0] is not PresetOptionViewModel option
            || vm.IsActivePreset(option))
            return;

        vm.RefreshSelectedPresetMatch();
        if (!ConfirmDiscardChanges(vm, option) || !vm.ApplyPreset(option))
        {
            vm.RestoreSelectedPreset();
            if (sender is ComboBox comboBox)
                comboBox.SelectedItem = vm.SelectedPreset;
        }
    }

    private static bool ConfirmDiscardChanges(SettingsViewModel vm, PresetOptionViewModel target) =>
        !vm.HasUnsavedPresetChanges
        || DialogManager.Warn(PresetLocalization.ConfirmationTitle,
            PresetLocalization.Format("DISCARD_CONFIRM", vm.ActivePresetName, target.Name),
            NativeDialogButtons.Accept | NativeDialogButtons.Cancel) == NativeDialogResult.Accept;

    private void OnDeletePresetClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || vm.SelectedPreset is null)
            return;

        if (DialogManager.Warn(PresetLocalization.ConfirmationTitle,
                PresetLocalization.Format("DELETE_CONFIRM", vm.SelectedPreset.Name),
                NativeDialogButtons.Accept | NativeDialogButtons.Cancel) == NativeDialogResult.Accept)
            vm.DeleteSelectedPreset();
    }

    private void OnImportPresetClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
            return;

        var dialog = new OpenFileDialog
        {
            DefaultExt = ".json",
            Filter = PresetLocalization.Get("FILE_FILTER")
        };

        if (dialog.ShowDialog() == true)
            vm.ImportPreset(dialog.FileName);
    }

    private void OnExportPresetClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm || vm.SelectedPreset is null)
            return;

        var dialog = new SaveFileDialog
        {
            DefaultExt = ".json",
            Filter = PresetLocalization.Get("FILE_FILTER"),
            FileName = "hunterpie-configuration-preset.json"
        };

        if (dialog.ShowDialog() == true)
            vm.ExportSelectedPreset(dialog.FileName);
    }

    private void OnTitleClick(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void OnSettingPropertyLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ConfigurationPropertyViewModel vm } element)
            return;

        var dataTrigger = new MultiDataTrigger();

        IReadOnlyCollection<PropertyCondition> conditions = vm.Conditions.Count > 0
            ? vm.Conditions
            : new[] { _defaultCondition };

        foreach (PropertyCondition condition in conditions)
        {
            BindingBase binding = Binder.Create(condition.Property);

            dataTrigger.Conditions.Add(
                item: new Condition(
                    binding: binding,
                    conditionValue: condition.Value
                )
            );
        }

        dataTrigger.EnterActions.Add(
            value: new BeginStoryboard
            {
                Storyboard = _enableSettingComponentAnimation
            }
        );
        dataTrigger.ExitActions.Add(
            value: new BeginStoryboard
            {
                Storyboard = _disableSettingComponentAnimation
            }
        );

        var style = new Style(element.GetType());

        style.Triggers.Add(dataTrigger);

        style.Setters.Add(new Setter(OpacityProperty, 0.0));

        element.Style = style;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not FrameworkElement element)
            return;

        if (element.TryFindParent<Border>() is not { } parent)
            return;

        bool hasChildrenVisible = e.NewSize.Height > 0;

        parent.Visibility = hasChildrenVisible
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}