using HunterPie.Core.Client.Localization;
using HunterPie.DI;
using System;

namespace HunterPie.Features.Settings.Localization;

internal static class PresetLocalization
{
    private static ILocalizationRepository Repository => DependencyContainer.Get<ILocalizationRepository>();

    public static string Get(string id) =>
        Repository.FindStringBy($"//Strings/Client/Presets/String[@Id='{id}']");

    public static string Format(string id, params object[] args) =>
        string.Format(Get(id), args);

    public static string Error(Exception exception)
    {
        string? id = exception.Message switch
        {
            "Preset storage is unavailable." => "STORE_UNAVAILABLE",
            "Select a preset first." => "SELECT_FIRST",
            "A preset with this name already exists for this game." => "NAME_EXISTS",
            "Invalid HunterPie game configuration preset." => "INVALID",
            "The preset file is empty." => "EMPTY_FILE",
            "The preset file is too large." => "LARGE_FILE",
            "The preset contains unsupported metadata." => "UNSUPPORTED_METADATA",
            "The preset belongs to a different game." => "DIFFERENT_GAME",
            "Unsupported game in preset." => "UNSUPPORTED_GAME",
            _ => null
        };

        return id is not null
            ? Get(id)
            : Format("OPERATION_FAILED", exception.Message);
    }
}
