namespace DawnPro.Core;

public enum GainMode : byte { Low, High }

public enum LedMode : byte { On, TemporarilyOff, Off }

public enum ReconstructionFilter : byte
{
    FastRollOffLowLatency,
    FastRollOffPhaseCompensated,
    SlowRollOffLowLatency,
    SlowRollOffPhaseCompensated,
    NonOversampling
}

public readonly record struct DawnProSettings(
    int VolumeStep, GainMode Gain, LedMode Led, ReconstructionFilter Filter)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(VolumeStep);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(VolumeStep, 60);
        if (!Enum.IsDefined(Gain) || !Enum.IsDefined(Led) || !Enum.IsDefined(Filter))
            throw new ArgumentException("Choose a supported gain, LED mode, and filter.");
    }
}

public readonly record struct DawnProState(
    byte VolumeCode, GainMode Gain, LedMode Led, ReconstructionFilter Filter)
{
    public DawnProSettings Settings => new(DawnProController.ToVolumeStep(VolumeCode), Gain, Led, Filter);
}
