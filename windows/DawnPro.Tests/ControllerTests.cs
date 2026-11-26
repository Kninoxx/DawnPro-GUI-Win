using DawnPro.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace DawnPro.Tests;

[TestClass]
public sealed class ControllerTests
{
    [TestMethod]
    public async Task ReadsUsingTheOriginalDawnProReportLayout()
    {
        var reports = new FakeReports();
        DawnProState state = await new DawnProController(reports).ReadAsync();

        Assert.AreEqual(new DawnProSettings(45, GainMode.Low, LedMode.On,
            ReconstructionFilter.FastRollOffLowLatency), state.Settings);
        CollectionAssert.AreEqual(new byte[] { 0x00, 0xC0, 0xA5, 0xA3, 0, 0, 0, 0 }, reports.Sent[0]);
        CollectionAssert.AreEqual(new byte[] { 0x00, 0xC0, 0xA5, 0xA2, 0, 0, 0, 0 }, reports.Sent[1]);
        Assert.IsEmpty(reports.Writes);
    }

    [TestMethod]
    public async Task RetriesShortAndStaleReplies()
    {
        var reports = new FakeReports();
        reports.Pending.Enqueue([0, 0xA0, 0xA5, 0xA3]);
        reports.Pending.Enqueue([0, 0xA0, 0xA5, 0xA2, 0, 0, 0, 0, 0]);

        Assert.AreEqual(reports.State, await new DawnProController(reports).ReadAsync());
        CollectionAssert.AreEqual(new byte[] { 0xA3, 0xA3, 0xA2 }, reports.Sent.Select(item => item[3]).ToArray());
    }

    [TestMethod]
    public async Task FailsWhenRepliesNeverMatchTheCommand()
    {
        var reports = new FakeReports();
        for (int attempt = 0; attempt < 6; attempt++)
            reports.Pending.Enqueue([0, 0xA0, 0xA5, 0xA2, 0, 0, 0, 0, 0]);

        await Assert.ThrowsExactlyAsync<InvalidDataException>(() => new DawnProController(reports).ReadAsync());
        Assert.HasCount(3, reports.Sent);
        Assert.IsEmpty(reports.Writes);
    }

    [TestMethod]
    public async Task RejectsOutOfRangeVolumeBeforeAnyReportIsSent()
    {
        var reports = new FakeReports();
        var controller = new DawnProController(reports);

        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            controller.ApplyAsync(reports.State, reports.State.Settings with { VolumeStep = -1 }));
        await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() =>
            controller.ApplyAsync(reports.State, reports.State.Settings with { VolumeStep = 61 }));
        Assert.IsEmpty(reports.Sent);
    }

    [TestMethod]
    public async Task ChecksTheBaselineBeforeWriting()
    {
        var reports = new FakeReports();
        DawnProState expected = reports.State;
        reports.State = expected with { VolumeCode = 0x20 };

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new DawnProController(reports).ApplyAsync(expected, expected.Settings with { Gain = GainMode.High }));
        Assert.IsEmpty(reports.Writes);
    }

    [TestMethod]
    public async Task LowersVolumeBeforeRaisingGainAndConfirmsAllFourSettings()
    {
        var reports = new FakeReports();
        var desired = new DawnProSettings(44, GainMode.High, LedMode.TemporarilyOff,
            ReconstructionFilter.NonOversampling);
        DawnProState actual = await new DawnProController(reports).ApplyAsync(reports.State, desired);

        CollectionAssert.AreEqual(new byte[] { 0x04, 0x02, 0x01, 0x06 }, reports.Writes);
        Assert.AreEqual(desired, actual.Settings);
        Assert.AreEqual((byte)0x20, actual.VolumeCode);
    }

    [TestMethod]
    public async Task ReadsBackLowerVolumeBeforeChangingGain()
    {
        var reports = new FakeReports { IgnoredCommand = 0x04 };
        DawnProSettings desired = reports.State.Settings with { VolumeStep = 40, Gain = GainMode.High };

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            new DawnProController(reports).ApplyAsync(reports.State, desired));
        CollectionAssert.AreEqual(new byte[] { 0x04 }, reports.Writes);
    }

    [TestMethod]
    public async Task StopsFurtherWritesWhenTheDacDisconnectsDuringReadback()
    {
        var reports = new FakeReports { DisconnectAfterVolumeWrite = true };
        DawnProSettings desired = reports.State.Settings with { VolumeStep = 40, Gain = GainMode.High };

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            new DawnProController(reports).ApplyAsync(reports.State, desired));
        CollectionAssert.AreEqual(new byte[] { 0x04 }, reports.Writes);
    }

    [TestMethod]
    public async Task PreservesOffTableAttenuationWhenVolumeIsUntouched()
    {
        var reports = new FakeReports();
        reports.State = reports.State with { VolumeCode = 0x1F };
        DawnProSettings desired = reports.State.Settings with { Led = LedMode.Off };

        DawnProState actual = await new DawnProController(reports).ApplyAsync(reports.State, desired);

        Assert.AreEqual((byte)0x1F, actual.VolumeCode);
        Assert.AreEqual(LedMode.Off, actual.Led);
        CollectionAssert.AreEqual(new byte[] { 0x06 }, reports.Writes);
    }

    [TestMethod]
    public async Task ReportsAnUnconfirmedSetting()
    {
        var reports = new FakeReports { IgnoredCommand = 0x06 };
        DawnProSettings desired = reports.State.Settings with { Led = LedMode.Off };

        await Assert.ThrowsExactlyAsync<IOException>(() =>
            new DawnProController(reports).ApplyAsync(reports.State, desired));
    }

    private sealed class FakeReports : IDawnProReports
    {
        private byte _lastCommand;
        private bool _disconnected;

        public DawnProState State { get; set; } = new(0x1E, GainMode.Low, LedMode.On,
            ReconstructionFilter.FastRollOffLowLatency);
        public List<byte[]> Sent { get; } = [];
        public byte[] Writes => Sent.Select(item => item[3]).Where(command => command < 0xA2).ToArray();
        public Queue<byte[]> Pending { get; } = new();
        public byte IgnoredCommand { get; init; }
        public bool DisconnectAfterVolumeWrite { get; init; }

        public void Write(byte[] report)
        {
            Sent.Add(report);
            _lastCommand = report[3];
            if (_lastCommand == 0x04 && DisconnectAfterVolumeWrite)
                _disconnected = true;
            if (_lastCommand == IgnoredCommand)
                return;

            byte value = report[4];
            State = _lastCommand switch
            {
                0x01 => State with { Filter = (ReconstructionFilter)value },
                0x02 => State with { Gain = (GainMode)value },
                0x04 => State with { VolumeCode = value },
                0x06 => State with { Led = (LedMode)value },
                _ => State
            };
        }

        public byte[] Read()
        {
            if (_disconnected)
                throw new IOException("Device disconnected.");
            if (Pending.TryDequeue(out byte[]? reply))
                return reply;
            if (_lastCommand == 0xA2)
                return [0, 0xA0, 0xA5, 0xA2, 0, State.VolumeCode, 0, 0, 0];
            return [0, 0xA0, 0xA5, 0xA3, (byte)State.Filter, (byte)State.Gain, (byte)State.Led, 0, 0];
        }
    }
}
