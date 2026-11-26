namespace DawnPro.Core;

public sealed class DawnProController(IDawnProReports reports)
{
    public const ushort VendorId = 0x2FC6;
    public const ushort ProductId = 0xF06A;
    public const int InputReportLength = 9;
    public const int OutputReportLength = 8;

    private const byte SetFilter = 0x01;
    private const byte SetGain = 0x02;
    private const byte SetVolume = 0x04;
    private const byte SetLed = 0x06;
    private const byte GetVolume = 0xA2;
    private const byte GetStatus = 0xA3;

    private static readonly TimeSpan TransferDelay = TimeSpan.FromMilliseconds(100);
    private static readonly byte[] VolumeCodes =
    [
        0xFF, 0xC8, 0xB4, 0xAA, 0xA0, 0x96, 0x8C, 0x82, 0x7A, 0x74,
        0x6E, 0x6A, 0x66, 0x62, 0x5E, 0x5A, 0x58, 0x56, 0x54, 0x52,
        0x50, 0x4E, 0x4C, 0x4A, 0x48, 0x46, 0x44, 0x42, 0x40, 0x3E,
        0x3C, 0x3A, 0x38, 0x36, 0x34, 0x32, 0x30, 0x2E, 0x2C, 0x2A,
        0x28, 0x26, 0x24, 0x22, 0x20, 0x1E, 0x1C, 0x1A, 0x18, 0x16,
        0x14, 0x12, 0x10, 0x0E, 0x0C, 0x0A, 0x08, 0x06, 0x04, 0x02,
        0x00
    ];

    public async Task<DawnProState> ReadAsync(CancellationToken cancellationToken = default)
    {
        byte[] status = await QueryAsync(GetStatus, cancellationToken).ConfigureAwait(false);
        var filter = (ReconstructionFilter)status[4];
        var gain = (GainMode)status[5];
        var led = (LedMode)status[6];
        if (!Enum.IsDefined(filter) || !Enum.IsDefined(gain) || !Enum.IsDefined(led))
            throw new InvalidDataException("The DAC returned an unknown filter, gain, or LED mode.");

        byte volume = await ReadVolumeAsync(cancellationToken).ConfigureAwait(false);
        return new DawnProState(volume, gain, led, filter);
    }

    public async Task<DawnProState> ApplyAsync(
        DawnProState expected, DawnProSettings desired, CancellationToken cancellationToken = default)
    {
        desired.Validate();
        DawnProState current = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (current != expected)
            throw new InvalidOperationException("The DAC settings changed. Refresh and review the current values before applying.");

        DawnProSettings original = current.Settings;
        if (original == desired)
            return current;

        byte volumeCode = desired.VolumeStep == original.VolumeStep
            ? current.VolumeCode
            : VolumeCodes[desired.VolumeStep];

        // Apply and verify lower volume before a possible gain increase.
        if (volumeCode > current.VolumeCode)
            await SetVolumeAsync(volumeCode, cancellationToken).ConfigureAwait(false);

        if (desired.Gain != original.Gain)
        {
            await SendAsync(SetGain, (byte)desired.Gain, cancellationToken).ConfigureAwait(false);
            // The volume query also re-latches the attenuator after a gain change.
            await ReadVolumeAsync(cancellationToken).ConfigureAwait(false);
            byte[] status = await QueryAsync(GetStatus, cancellationToken).ConfigureAwait(false);
            if (status[5] != (byte)desired.Gain)
                throw new IOException("The DAC did not confirm the requested gain.");
        }

        if (desired.Filter != original.Filter)
            await SendAsync(SetFilter, (byte)desired.Filter, cancellationToken).ConfigureAwait(false);
        if (desired.Led != original.Led)
            await SendAsync(SetLed, (byte)desired.Led, cancellationToken).ConfigureAwait(false);
        if (volumeCode < current.VolumeCode)
            await SetVolumeAsync(volumeCode, cancellationToken).ConfigureAwait(false);

        DawnProState confirmed = await ReadAsync(cancellationToken).ConfigureAwait(false);
        if (confirmed.VolumeCode != volumeCode || confirmed.Settings != desired)
            throw new IOException("The DAC readback differs from the requested settings. Refresh to see the current values.");

        return confirmed;
    }

    internal static int ToVolumeStep(byte code)
    {
        // A front-panel adjustment can leave an attenuation code between slider steps.
        int nearestStep = 0;
        for (int step = 1; step < VolumeCodes.Length; step++)
        {
            if (Math.Abs(VolumeCodes[step] - code) < Math.Abs(VolumeCodes[nearestStep] - code))
                nearestStep = step;
        }

        return nearestStep;
    }

    private async Task<byte> ReadVolumeAsync(CancellationToken cancellationToken)
    {
        byte[] reply = await QueryAsync(GetVolume, cancellationToken).ConfigureAwait(false);
        return reply[5];
    }

    private async Task SetVolumeAsync(byte code, CancellationToken cancellationToken)
    {
        await SendAsync(SetVolume, code, cancellationToken).ConfigureAwait(false);
        if (await ReadVolumeAsync(cancellationToken).ConfigureAwait(false) != code)
            throw new IOException("The DAC did not confirm the requested volume.");
    }

    private async Task<byte[]> QueryAsync(byte command, CancellationToken cancellationToken)
    {
        // The DAC retains its latest reply and may return the previous command after a new request.
        for (int attempt = 0; attempt < 6; attempt++)
        {
            if (attempt % 2 == 0)
                await SendAsync(command, 0, cancellationToken).ConfigureAwait(false);
            else
                await Task.Delay(TransferDelay, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            byte[] reply = reports.Read();
            if (reply.Length == InputReportLength && reply[0] == 0 &&
                reply[1] == 0xA0 && reply[2] == 0xA5 && reply[3] == command)
            {
                return reply;
            }
        }

        throw new InvalidDataException($"The DAC did not return a valid reply for command 0x{command:X2}.");
    }

    private async Task SendAsync(byte command, byte value, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        reports.Write([0x00, 0xC0, 0xA5, command, value, 0, 0, 0]);
        await Task.Delay(TransferDelay, cancellationToken).ConfigureAwait(false);
    }
}
