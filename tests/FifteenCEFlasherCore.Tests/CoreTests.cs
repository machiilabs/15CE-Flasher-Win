using FifteenCEFlasherCore;
using Xunit;

namespace FifteenCEFlasherCore.Tests;

public class FlashLayoutTests
{
    [Fact]
    public void ApplicationSizeMatchesOfficialDump()
    {
        Assert.Equal(112 * 1024, FlashLayout.ExpectedFirmwareByteCount);
        Assert.Equal(0x4000u, FlashLayout.ApplicationStart);
        Assert.Equal(0x1C000u, FlashLayout.ApplicationSize);
    }

    [Fact]
    public void SafeRangeRejectsBootloader()
    {
        Assert.False(FlashLayout.IsSafeApplicationRange(0, 16));
        Assert.False(FlashLayout.IsSafeApplicationRange(0x3FF0, 32));
        Assert.True(FlashLayout.IsSafeApplicationRange(0x4000, 0x1C000));
        Assert.False(FlashLayout.IsSafeApplicationRange(0x4000, 0x1C001));
        Assert.False(FlashLayout.IsSafeApplicationRange(0x4000, 0));
    }
}

public class SambaClientTests
{
    [Fact]
    public void ConnectReadsVersion()
    {
        using var mock = new SimulatedCalculatorTransport();
        var client = new SambaClient(mock);
        client.Connect();
        Assert.Contains("v1.1", client.Version);
    }

    [Fact]
    public void ReadAndWriteWord()
    {
        using var mock = new SimulatedCalculatorTransport();
        var client = new SambaClient(mock);
        client.Connect();
        Assert.Equal(0xAB0A07E0u, client.ReadWord(FlashCalw.ChipIdCidr));
        client.WriteWord(0x20000000, 0x11223344);
        Assert.Equal(0x11223344u, client.ReadWord(0x20000000));
    }

    [Fact]
    public void BlockReadWrite()
    {
        using var mock = new SimulatedCalculatorTransport();
        var client = new SambaClient(mock);
        client.Connect();
        var payload = Enumerable.Range(0, 1024).Select(i => (byte)(i & 0xFF)).ToArray();
        client.Write(0x20001000, payload);
        Assert.Equal(payload, client.Read(0x20001000, payload.Length));
    }
}

public class FlashCalwTests
{
    [Fact]
    public void IdentifyATSAM4LC2C()
    {
        using var mock = new SimulatedCalculatorTransport();
        var client = new SambaClient(mock);
        client.Connect();
        var identity = new FlashCalw(client).Identify();
        Assert.Equal("ATSAM4LC2C", identity.Name);
        Assert.True(identity.IsSupported15C);
    }

    [Fact]
    public void WriteApplicationVerifies()
    {
        using var mock = new SimulatedCalculatorTransport();
        var client = new SambaClient(mock);
        client.Connect();
        var image = new byte[FlashLayout.ExpectedFirmwareByteCount];
        for (var i = 0; i < image.Length; i += 17)
            image[i] = (byte)(i & 0xFF);

        new FlashCalw(client).WriteApplication(image);
        Assert.Equal(image, client.Read(FlashLayout.ApplicationStart, image.Length));
        Assert.False(mock.Memory.ContainsKey(0x0000));
    }
}

public class SambaFlashAppletTests
{
    [Fact]
    public void OfficialImageHasVectorTable()
    {
        var image = SambaFlashApplet.Image.ToArray();
        Assert.Equal(2652, image.Length);
        var sp = BitConverter.ToUInt32(image, 0);
        var reset = BitConverter.ToUInt32(image, 4);
        Assert.Equal(0x20007FF0u, sp);
        Assert.Equal(0x20002809u, reset);
    }

    [Fact]
    public void InitializeAndWriteOnePage()
    {
        using var mock = new SimulatedCalculatorTransport();
        var client = new SambaClient(mock);
        client.Connect();
        var applet = new SambaFlashApplet(client);
        var info = applet.LoadAndInitialize();
        Assert.Equal(0x20000u, info.MemorySize);
        Assert.Equal(0x200u, info.BufferSize);
        Assert.Equal(0x200u, info.PageSize);
        Assert.Equal(32u, info.AppStartPage);
        Assert.Equal(0x20002C00u, info.BufferAddress);
        Assert.Equal(~(uint)SambaAppletCommand.Initialize, client.ReadWord(SambaFlashApplet.MailboxAddress));

        var page = Enumerable.Range(0, 512).Select(i => (byte)(i & 0xFF)).ToArray();
        Assert.Equal(512, applet.Write(0x4000, page));
        Assert.Equal(~(uint)SambaAppletCommand.Write, client.ReadWord(SambaFlashApplet.MailboxAddress));
        Assert.Equal(page, client.Read(0x4000, 512));
    }
}

public class FlasherTests
{
    [Fact]
    public void WriteWithMockCableProgramsApplicationFlash()
    {
        var path = Path.Combine(Path.GetTempPath(), $"hp15c-fw-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(path, new byte[FlashLayout.ExpectedFirmwareByteCount]);
        try
        {
            using var mock = new SimulatedCalculatorTransport();
            var client = new SambaClient(mock);
            client.Connect();
            var flasher = new Flasher
            {
                Ports = new FixedPortListing([SimulatedCalculatorTransport.DemoPort]),
                FlashCommandSettleSeconds = TimeSpan.Zero,
            };
            flasher.Write(path, client: client);
            var dumped = client.Read(FlashLayout.ApplicationStart, FlashLayout.ExpectedFirmwareByteCount);
            Assert.Equal(FlashLayout.ExpectedFirmwareByteCount, dumped.Length);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public class WindowsComPortListingTests
{
    [Theory]
    [InlineData(@"USB\VID_03EB&PID_6124\5&ABC&0&1", 0x03EB, 0x6124, UsbPortKind.AtmelSamBa)]
    [InlineData(@"USB\VID_03EB&PID_6124&MI_00\6&DEF&0&0000", 0x03EB, 0x6124, UsbPortKind.AtmelSamBa)]
    [InlineData(@"USB\VID_0403&PID_6015\A1B2C3D4", 0x0403, 0x6015, UsbPortKind.Ftdi)]
    [InlineData(@"FTDIBUS\VID_0403+PID_6015+A1B2C3D4A\0000", 0x0403, 0x6015, UsbPortKind.Ftdi)]
    [InlineData(@"USB\VID_046D&PID_C077\6&1234", 0x046D, 0xC077, UsbPortKind.Unknown)]
    public void ParseVidPidFromWindowsPnpId(string pnp, int vid, int pid, UsbPortKind kind)
    {
        var (parsedVid, parsedPid) = WindowsComPortListing.ParseVidPid(pnp);
        Assert.Equal((ushort)vid, parsedVid);
        Assert.Equal((ushort)pid, parsedPid);
        Assert.Equal(kind, WindowsComPortListing.Classify(parsedVid, parsedPid));
    }

    [Fact]
    public void ParseVidPidIgnoresBareComName()
    {
        var (vid, pid) = WindowsComPortListing.ParseVidPid("COM3");
        Assert.Null(vid);
        Assert.Null(pid);
        Assert.Equal(UsbPortKind.Unknown, WindowsComPortListing.Classify(vid, pid));
    }
}

internal sealed class FixedPortListing(IReadOnlyList<SerialPortInfo> ports) : ISerialPortListing
{
    public IReadOnlyList<SerialPortInfo> ListPorts() => ports;
}

public class KnownFirmwareTests
{
    [Fact]
    public void EmbeddedListLoads()
    {
        Assert.NotEmpty(KnownFirmware.All);
        Assert.Equal("HP 15c Collector’s Edition", KnownFirmware.Find(0x9090)?.Model);
        Assert.Equal("HP 16c Collector’s Edition", KnownFirmware.Find(0x0E0E)?.Model);
        Assert.Null(KnownFirmware.Find(0x1234));
    }

    [Fact]
    public void EveryChecksumRepeatsItsByte()
    {
        // The calculator shows the 8-bit sum twice, so a real checksum is always 0xXYXY.
        foreach (var entry in KnownFirmware.All)
            Assert.Equal(entry.Checksum >> 8, entry.Checksum & 0xFF);
    }

    [Theory]
    [InlineData("""{"firmware":[{"checksum":"0x9090","model":"HP 15c Collector’s Edition","fileName":"x","description":"a"},{"checksum":"9090","model":"HP 15c Collector’s Edition","fileName":"x","description":"b"}]}""")]
    [InlineData("""{"firmware":[{"checksum":"0x9090","model":"15C","fileName":"x","description":"a"}]}""")]
    [InlineData("""{"firmware":[{"checksum":"0x9090","model":"15c Collector’s Edition","fileName":"x","description":"a"}]}""")]
    [InlineData("""{"firmware":[{"checksum":"0xZZ","model":"HP 15c Collector’s Edition","fileName":"x","description":"a"}]}""")]
    [InlineData("""{"firmware":[{"checksum":"0x9090","model":"HP 15c Collector’s Edition","fileName":"HP 15c","description":"a"}]}""")]
    [InlineData("""{"firmware":[{"checksum":"0x9090","model":"HP 15c Collector’s Edition","description":"a"}]}""")]
    public void ParseRejectsBadLists(string json)
    {
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        Assert.Throws<FormatException>(() => KnownFirmware.Parse(stream));
    }
}

public class FirmwareAssessmentTests
{
    private static BackupChecksumAssessment Backup(ushort checksum) => new(checksum);

    [Theory]
    [InlineData(0x9090, "hp15c-ce-original-9090h-20260929.bin")]
    [InlineData(0x0E0E, "hp16c-ce-original-0E0Eh-20260929.bin")]
    [InlineData(0x1212, "firmware-1212h-20260929.bin")]
    public void DefaultBackupFileName(int checksum, string expected) =>
        Assert.Equal(expected, Backup((ushort)checksum).DefaultBackupFileName(new DateTime(2026, 9, 29)));

    [Fact]
    public void BackupNamesKnownFirmware()
    {
        var backup = Backup(0x0A0A);
        Assert.NotNull(backup.Known);
        Assert.Contains("June 2024", backup.Message);
    }

    [Fact]
    public void BackupOfUnlistedFirmwareIsNotBlamedOnTheUser()
    {
        var backup = Backup(0x1212);
        Assert.Null(backup.Known);
        Assert.Contains("not in the list of known versions", backup.Message);
    }

    [Theory]
    [InlineData(0x0A0A, 0x0A0A, FirmwareFileKind.AlreadyOnCalculator)]
    [InlineData(0x0A0A, 0x9090, FirmwareFileKind.Known)]
    [InlineData(0x9090, 0x0E0E, FirmwareFileKind.OtherModel)]
    [InlineData(0x0E0E, 0x0A0A, FirmwareFileKind.OtherModel)]
    [InlineData(0x0E0E, 0x8989, FirmwareFileKind.Known)]
    [InlineData(0x8989, 0x9090, FirmwareFileKind.OtherModel)]
    [InlineData(0x1212, 0x0E0E, FirmwareFileKind.Known)]
    [InlineData(0x9090, 0x1212, FirmwareFileKind.Unrecognized)]
    public void FirmwareFileAgainstBackup(int onCalculator, int file, FirmwareFileKind expected)
    {
        var assessment = new FirmwareFileAssessment((ushort)file, Backup((ushort)onCalculator));
        Assert.Equal(expected, assessment.Kind);
    }

    [Fact]
    public void SkippedBackupAsksTheUserToCheckTheModel()
    {
        var assessment = new FirmwareFileAssessment(0x0E0E, backup: null);
        Assert.Equal(FirmwareFileKind.Known, assessment.Kind);
        Assert.Contains("Make sure your calculator is an HP 16c", assessment.Message);
    }

    [Fact]
    public void SummaryDropsSafeToProceed()
    {
        var backup = Backup(0x0A0A);
        Assert.Contains("safe to proceed", backup.Message);
        Assert.DoesNotContain("safe to proceed", backup.Summary);
        Assert.EndsWith("backup and restore.", backup.Summary);

        var checkedFile = new FirmwareFileAssessment(0x0A0A, Backup(0x9090));
        Assert.False(checkedFile.IsCaution);
        Assert.Contains("safe to proceed", checkedFile.Message);
        Assert.Equal($"Checksum 0A0Ah: {KnownFirmware.Find(0x0A0A)!.DisplayName}.", checkedFile.Summary);
    }

    [Fact]
    public void CautionWhenModelCannotBeConfirmed()
    {
        Assert.True(new FirmwareFileAssessment(0x0E0E, backup: null).IsCaution);
        Assert.True(new FirmwareFileAssessment(0x0E0E, Backup(0x9090)).IsCaution);
        Assert.True(new FirmwareFileAssessment(0x1212, Backup(0x9090)).IsCaution);
    }

    [Fact]
    public void OtherModelNamesBothModels()
    {
        var assessment = new FirmwareFileAssessment(0x0E0E, Backup(0x9090));
        Assert.Contains("16c Collector’s Edition original firmware", assessment.Message);
        Assert.Contains("Your calculator has HP 15c Collector’s Edition firmware", assessment.Message);
    }
}
