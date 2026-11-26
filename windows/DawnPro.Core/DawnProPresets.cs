using System.Text.Json;
using System.Text.Json.Nodes;

namespace DawnPro.Core;

public static class DawnProPresets
{
    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    public static DawnProSettings Load(string path, DawnProSettings fallback)
    {
        JsonObject root = ReadConfig(path);
        if (root["default_settings"] is not JsonObject settings)
            throw new InvalidDataException("This file has no Dawn Pro default_settings section.");

        var values = new DawnProSettings(
            settings["DEFAULT_VOLUME"]?.GetValue<int>() ?? fallback.VolumeStep,
            ReadMode(settings, "DEFAULT_GAIN", fallback.Gain, static value => value.ToString()),
            ReadMode(settings, "DEFAULT_LED_STATUS", fallback.Led, Label),
            ReadMode(settings, "DEFAULT_FILTER", fallback.Filter, Label));
        values.Validate();
        return values;
    }

    public static void Save(string path, DawnProSettings values)
    {
        values.Validate();
        string absolutePath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        JsonObject root;

        try
        {
            root = ReadConfig(absolutePath);
        }
        catch (FileNotFoundException)
        {
            root = new JsonObject();
        }

        JsonObject settings;
        if (root["default_settings"] is null)
        {
            settings = new JsonObject();
            root["default_settings"] = settings;
        }
        else if (root["default_settings"] is JsonObject existing)
        {
            settings = existing;
        }
        else
        {
            throw new InvalidDataException("The saved default_settings section must be a JSON object.");
        }

        settings["DEFAULT_VOLUME"] = values.VolumeStep;
        settings["DEFAULT_GAIN"] = values.Gain.ToString();
        settings["DEFAULT_LED_STATUS"] = Label(values.Led);
        settings["DEFAULT_FILTER"] = Label(values.Filter);

        string temporaryPath = $"{absolutePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporaryPath, root.ToJsonString(WriteOptions));
            File.Move(temporaryPath, absolutePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static string Label(LedMode mode) =>
        mode == LedMode.TemporarilyOff ? "Temporarily Off" : mode.ToString();

    public static string Label(ReconstructionFilter filter) => filter switch
    {
        ReconstructionFilter.FastRollOffLowLatency => "Fast Roll-Off Low Latency",
        ReconstructionFilter.FastRollOffPhaseCompensated => "Fast Roll-Off Phase Compensated",
        ReconstructionFilter.SlowRollOffLowLatency => "Slow Roll-Off Low Latency",
        ReconstructionFilter.SlowRollOffPhaseCompensated => "Slow Roll-Off Phase Compensated",
        ReconstructionFilter.NonOversampling => "Non-Oversampling",
        _ => throw new ArgumentOutOfRangeException(nameof(filter))
    };

    private static JsonObject ReadConfig(string path) =>
        JsonNode.Parse(File.ReadAllText(path)) as JsonObject
        ?? throw new InvalidDataException("A Dawn Pro config must contain a JSON object.");

    private static T ReadMode<T>(JsonObject settings, string key, T fallback, Func<T, string> label)
        where T : struct, Enum
    {
        string? text = settings[key]?.GetValue<string>();
        if (text is null)
            return fallback;
        foreach (T value in Enum.GetValues<T>())
        {
            if (string.Equals(label(value), text, StringComparison.Ordinal))
                return value;
        }

        throw new InvalidDataException($"Unknown setting for {key}.");
    }
}
