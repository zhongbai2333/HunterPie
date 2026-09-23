using HunterPie.Core.Client.Configuration.Games;
using HunterPie.Core.Domain.Enums;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HunterPie.Core.Client.ConfigurationPresets;

/// <summary>Stores game configuration snapshots separately from the active config.json.</summary>
public sealed class GameConfigurationPresetStore
{
    private const int MaxFileSize = 4 * 1024 * 1024;

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        TypeNameHandling = TypeNameHandling.None,
        MetadataPropertyHandling = MetadataPropertyHandling.Ignore,
        Converters = { new StringEnumConverter() }
    };

    private readonly string _path;
    private readonly List<GameConfigurationPreset> _presets;

    public IReadOnlyList<GameConfigurationPreset> Presets => _presets;

    public GameConfigurationPresetStore(string path)
    {
        _path = path;
        _presets = File.Exists(path)
            ? ReadList(path)
            : new List<GameConfigurationPreset>();
    }

    public GameConfigurationPreset SaveCurrent(string name, GameProcessType game, GameConfig configuration)
    {
        var preset = new GameConfigurationPreset
        {
            Name = name?.Trim() ?? string.Empty,
            Game = game,
            Configuration = JObject.FromObject(configuration, JsonSerializer.Create(JsonSettings))
        };

        Validate(preset);
        var next = _presets.ToList();
        int existingIndex = next.FindIndex(it => it.Game == game
            && string.Equals(it.Name, preset.Name, StringComparison.OrdinalIgnoreCase));

        if (existingIndex >= 0)
            next[existingIndex] = preset;
        else
            next.Add(preset);

        Persist(next);
        _presets.Clear();
        _presets.AddRange(next);
        return preset;
    }

    public void Apply(GameConfigurationPreset preset, GameConfig configuration)
    {
        Validate(preset);
        // Populate a fresh instance first so malformed imports cannot partly change live settings.
        GameConfig candidate = CreateConfiguration(preset.Game);
        if (configuration.GetType() != candidate.GetType())
            throw new InvalidOperationException("The preset belongs to a different game.");

        string snapshot = preset.Configuration.ToString(Formatting.None);
        JsonConvert.PopulateObject(snapshot, candidate, JsonSettings);
        JsonConvert.PopulateObject(snapshot, configuration, JsonSettings);
    }

    public void Delete(GameConfigurationPreset preset)
    {
        var next = _presets.Where(it => it != preset).ToList();
        Persist(next);
        _presets.Clear();
        _presets.AddRange(next);
    }

    public GameConfigurationPreset Import(string path)
    {
        GameConfigurationPreset preset = ReadPreset(path);
        Validate(preset);

        if (_presets.Any(it => it.Game == preset.Game
            && string.Equals(it.Name, preset.Name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("A preset with this name already exists for this game.");

        var next = _presets.Append(preset).ToList();
        Persist(next);
        _presets.Clear();
        _presets.AddRange(next);
        return preset;
    }

    public void Export(GameConfigurationPreset preset, string path)
    {
        Validate(preset);
        File.WriteAllText(path, JsonConvert.SerializeObject(preset, Formatting.Indented, JsonSettings));
    }

    private void Persist(List<GameConfigurationPreset> presets)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        string temporaryPath = _path + ".tmp";
        File.WriteAllText(temporaryPath, JsonConvert.SerializeObject(presets, Formatting.Indented, JsonSettings));
        File.Move(temporaryPath, _path, overwrite: true);
    }

    private static List<GameConfigurationPreset> ReadList(string path)
    {
        CheckFileSize(path);
        List<GameConfigurationPreset> presets = JsonConvert.DeserializeObject<List<GameConfigurationPreset>>(
            File.ReadAllText(path), JsonSettings)
            ?? throw new InvalidDataException("The preset file is empty.");

        foreach (GameConfigurationPreset preset in presets)
            Validate(preset);

        return presets;
    }

    private static GameConfigurationPreset ReadPreset(string path)
    {
        CheckFileSize(path);
        return JsonConvert.DeserializeObject<GameConfigurationPreset>(File.ReadAllText(path), JsonSettings)
            ?? throw new InvalidDataException("The preset file is empty.");
    }

    private static void CheckFileSize(string path)
    {
        if (new FileInfo(path).Length > MaxFileSize)
            throw new InvalidDataException("The preset file is too large.");
    }

    private static void Validate(GameConfigurationPreset preset)
    {
        if (preset.FormatVersion != 1
            || preset.Game is not (GameProcessType.MonsterHunterWorld
                or GameProcessType.MonsterHunterRise
                or GameProcessType.MonsterHunterWilds)
            || string.IsNullOrWhiteSpace(preset.Name)
            || preset.Name.Length > 80
            || preset.Configuration?["Overlay"] is not JObject
            || preset.Configuration["RichPresence"] is not JObject)
            throw new InvalidDataException("Invalid HunterPie game configuration preset.");

        // Do not allow imported JSON to instantiate CLR types through metadata.
        if (preset.Configuration.DescendantsAndSelf().OfType<JProperty>()
            .Any(property => property.Name is "$type" or "$ref" or "$id"))
            throw new InvalidDataException("The preset contains unsupported metadata.");

        GameConfig candidate = CreateConfiguration(preset.Game);
        JsonConvert.PopulateObject(preset.Configuration.ToString(Formatting.None), candidate, JsonSettings);
    }

    private static GameConfig CreateConfiguration(GameProcessType game) => game switch
    {
        GameProcessType.MonsterHunterWorld => new MHWConfig(),
        GameProcessType.MonsterHunterRise => new MHRConfig(),
        GameProcessType.MonsterHunterWilds => new MHWildsConfig(),
        _ => throw new InvalidDataException("Unsupported game in preset.")
    };
}
