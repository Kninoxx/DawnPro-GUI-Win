using System.Text.Json;
using DawnPro.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DawnPro.Tests;

[TestClass]
public sealed class PresetTests
{
    private static readonly DawnProSettings Fallback = new(30, GainMode.Low, LedMode.On,
        ReconstructionFilter.SlowRollOffPhaseCompensated);

    [TestMethod]
    public void LoadsAPartialLegacyConfigAndUsesTheCurrentValuesForMissingFields()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(folder.Config, """
            {"default_settings": {"DEFAULT_LED_STATUS": "Temporarily Off", "DEFAULT_VOLUME": 45}}
            """);

        Assert.AreEqual(Fallback with { Led = LedMode.TemporarilyOff, VolumeStep = 45 },
            DawnProPresets.Load(folder.Config, Fallback));
    }

    [TestMethod]
    public void SavesCompatibleSettingsAndPreservesOtherConfigFields()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(folder.Config, """
            {"ui_metrics": {"WINDOW_WIDTH": 400}, "default_settings": {"custom": true}}
            """);
        var desired = new DawnProSettings(60, GainMode.High, LedMode.TemporarilyOff,
            ReconstructionFilter.NonOversampling);

        DawnProPresets.Save(folder.Config, desired);

        Assert.AreEqual(desired, DawnProPresets.Load(folder.Config, Fallback));
        using var document = JsonDocument.Parse(File.ReadAllText(folder.Config));
        Assert.AreEqual(400, document.RootElement.GetProperty("ui_metrics").GetProperty("WINDOW_WIDTH").GetInt32());
        Assert.IsTrue(document.RootElement.GetProperty("default_settings").GetProperty("custom").GetBoolean());
        Assert.HasCount(1, Directory.GetFiles(folder.Path));
    }

    [TestMethod]
    public void RejectsInvalidLegacyVolume()
    {
        using var folder = new TemporaryFolder();
        File.WriteAllText(folder.Config, """
            {"default_settings": {"DEFAULT_VOLUME": -1}}
            """);

        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DawnProPresets.Load(folder.Config, Fallback));
    }

    private sealed class TemporaryFolder : IDisposable
    {
        public string Path { get; } = Directory.CreateTempSubdirectory("dawnpro-tests-").FullName;
        public string Config => System.IO.Path.Combine(Path, "config.json");

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
