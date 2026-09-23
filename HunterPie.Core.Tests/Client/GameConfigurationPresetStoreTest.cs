using HunterPie.Core.Client.Configuration.Games;
using HunterPie.Core.Client.Configuration.Overlay;
using HunterPie.Core.Client.Configuration.Overlay.Monster;
using HunterPie.Core.Client.ConfigurationPresets;
using HunterPie.Core.Domain.Enums;
using HunterPie.Core.Domain.Mapper;
using HunterPie.Core.Domain.Mapper.Internal;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace HunterPie.Core.Tests.Client;

[TestClass]
public class GameConfigurationPresetStoreTest
{
    [ClassInitialize]
    public static void Initialize(TestContext _)
    {
        MapFactory.Add(new XmlNodeToAilmentDefinitionMapper());
    }

    [TestMethod]
    public void SaveCurrentRequiresPresetName()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "presets.json");
            var store = new GameConfigurationPresetStore(path);
            InvalidDataException error = Assert.ThrowsException<InvalidDataException>(
                () => store.SaveCurrent("   ", GameProcessType.MonsterHunterWorld, new MHWConfig()));

            Assert.AreEqual("Preset name is required.", error.Message);
            Assert.IsFalse(File.Exists(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void PresetRestoresWidgetPositionAndSettingsWithoutReplacingBoundObjects()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var config = new MHWConfig();
            config.Overlay.BossesWidget.Position.X = 321;
            config.Overlay.BossesWidget.IsCompactModeEnabled.Value = false;
            var tray = new AbnormalityWidgetConfig();
            tray.Position.Y = 432;
            config.Overlay.AbnormalityTray.Trays.Trays.Add(tray);
            var position = config.Overlay.BossesWidget.Position;
            var compactMode = config.Overlay.BossesWidget.IsCompactModeEnabled;

            var store = new GameConfigurationPresetStore(Path.Combine(directory, "presets.json"));
            GameConfigurationPreset preset = store.SaveCurrent("World desk", GameProcessType.MonsterHunterWorld, config);

            config.Overlay.BossesWidget.Position.X = 999;
            config.Overlay.BossesWidget.IsCompactModeEnabled.Value = true;
            tray.Position.Y = 999;
            store.Apply(preset, config);

            Assert.AreEqual(321, config.Overlay.BossesWidget.Position.X);
            Assert.IsFalse(config.Overlay.BossesWidget.IsCompactModeEnabled.Value);
            Assert.AreSame(position, config.Overlay.BossesWidget.Position);
            Assert.AreSame(compactMode, config.Overlay.BossesWidget.IsCompactModeEnabled);
            Assert.AreEqual(432, tray.Position.Y);
            Assert.AreSame(tray, config.Overlay.AbnormalityTray.Trays.Trays[0]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void PresetRestoresMonsterDetailOverridesWithExistingMonsterId()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var config = new MHWConfig();
            var part = new MonsterPartConfiguration
            {
                Id = 10,
                StringId = "test-part",
                IsEnabled = false
            };
            var monster = new MonsterConfiguration
            {
                Id = 1,
                Parts = new() { part },
                Ailments = new()
            };
            var overrides = config.Overlay.BossesWidget.Details.Monsters;
            overrides.Add(monster);
            string path = Path.Combine(directory, "presets.json");
            var store = new GameConfigurationPresetStore(path);
            store.SaveCurrent("Details", GameProcessType.MonsterHunterWorld, config);

            part.IsEnabled.Value = true;
            var reloaded = new GameConfigurationPresetStore(path);
            reloaded.Apply(reloaded.Presets.Single(), config);

            Assert.AreSame(overrides, config.Overlay.BossesWidget.Details.Monsters);
            Assert.IsFalse(config.Overlay.BossesWidget.Details.Monsters.Single().Parts.Single().IsEnabled.Value);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ExportedPresetCanBeImportedAndAppliedToItsGame()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var rise = new MHRConfig();
            rise.Overlay.PlayerHudWidget.Position.Y = 765;
            var source = new GameConfigurationPresetStore(Path.Combine(directory, "source.json"));
            GameConfigurationPreset preset = source.SaveCurrent("Rise laptop", GameProcessType.MonsterHunterRise, rise);
            string exportPath = Path.Combine(directory, "export.json");
            source.Export(preset, exportPath);

            var destination = new GameConfigurationPresetStore(Path.Combine(directory, "destination.json"));
            GameConfigurationPreset imported = destination.Import(exportPath);
            rise.Overlay.PlayerHudWidget.Position.Y = 0;
            destination.Apply(imported, rise);

            Assert.AreEqual(GameProcessType.MonsterHunterRise, imported.Game);
            Assert.AreEqual(765, rise.Overlay.PlayerHudWidget.Position.Y);
            Assert.AreEqual(1, new GameConfigurationPresetStore(Path.Combine(directory, "destination.json")).Presets.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void ImportRejectsClrTypeMetadata()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            string path = Path.Combine(directory, "malicious.json");
            File.WriteAllText(path, """
                {"FormatVersion":1,"Name":"bad","Game":"MonsterHunterWorld",
                 "Configuration":{"Overlay":{"$type":"Unknown.Type"},"RichPresence":{}}}
                """);

            var store = new GameConfigurationPresetStore(Path.Combine(directory, "presets.json"));
            Assert.ThrowsException<InvalidDataException>(() => store.Import(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [TestMethod]
    public void WildsPresetStaysSeparateFromWorldPreset()
    {
        string directory = CreateTemporaryDirectory();
        try
        {
            var store = new GameConfigurationPresetStore(Path.Combine(directory, "presets.json"));
            var world = new MHWConfig();
            var wilds = new MHWildsConfig();
            world.Overlay.ChatWidget.Position.X = 100;
            wilds.Overlay.ChatWidget.Position.X = 200;
            store.SaveCurrent("Desktop", GameProcessType.MonsterHunterWorld, world);
            store.SaveCurrent("Desktop", GameProcessType.MonsterHunterWilds, wilds);

            Assert.IsTrue(new FileInfo(Path.Combine(directory, "presets.json")).Length < 1024 * 1024);

            Assert.AreEqual(2, store.Presets.Count);
            Assert.ThrowsException<InvalidOperationException>(() => store.Apply(store.Presets[0], wilds));
            wilds.Overlay.ChatWidget.Position.X = 300;
            store.Apply(store.Presets[1], wilds);
            Assert.AreEqual(200, wilds.Overlay.ChatWidget.Position.X);
            Assert.AreEqual(100, world.Overlay.ChatWidget.Position.X);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"hunterpie-presets-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
