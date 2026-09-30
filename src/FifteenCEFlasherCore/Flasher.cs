namespace FifteenCEFlasherCore;

public enum FlashProgressPhase
{
    Reading,
    Writing,
    Verifying,
}

public sealed class FlashPlan
{
    public FirmwareImage Image { get; init; } = null!;
    public uint Address { get; init; }
    public SerialPortInfo? Port { get; init; }
    public int ByteCount => Image.ByteCount;
}

public sealed class ConnectedTarget
{
    public SerialPortInfo Port { get; init; } = null!;
    public SambaClient Client { get; init; } = null!;
    public DeviceIdentity Identity { get; init; } = null!;
}

public sealed class Flasher
{
    public ISerialPortListing Ports { get; init; } = new WindowsComPortListing();
    public bool RequireExactFirmwareSize { get; init; } = true;
    public Func<SerialPortInfo, IByteTransport> OpenTransport { get; init; } =
        port => new WindowsSerialLink(port.PortName);
    public TimeSpan FlashCommandSettleSeconds { get; init; } = TimeSpan.FromMilliseconds(10);

    public IReadOnlyList<SerialPortInfo> ListProgrammingCables() =>
        Ports.ListPorts().ProgrammingCables().ToList();

    public SerialPortInfo RequireProgrammingCable()
    {
        var first = PreferredProgrammingCables().FirstOrDefault()
            ?? throw FlasherError.NoProgrammingCable();
        return first;
    }

    public IReadOnlyList<SerialPortInfo> PreferredProgrammingCables()
    {
        return ListProgrammingCables()
            .OrderBy(p => PortPriority(p))
            .ToList();
    }

    private static int PortPriority(SerialPortInfo port) => port.Kind switch
    {
        UsbPortKind.AtmelSamBa => 0,
        UsbPortKind.Ftdi => 1,
        _ => 2,
    };

    public FlashPlan PlanWrite(string firmwarePath, uint address = FlashLayout.ApplicationStart)
    {
        var image = FirmwareImage.Load(firmwarePath, RequireExactFirmwareSize);
        if (!FlashLayout.IsSafeApplicationRange(address, (uint)image.ByteCount))
            throw FlasherError.WriteWouldTouchBootloader(address);

        SerialPortInfo? port = null;
        try
        {
            port = RequireProgrammingCable();
        }
        catch (FlasherError ex) when (ex.Kind == FlasherErrorKind.NoProgrammingCable)
        {
            // Planning without cable is allowed.
        }

        return new FlashPlan { Image = image, Address = address, Port = port };
    }

    public ConnectedTarget Connect(TimeSpan? timeout = null)
    {
        var cables = PreferredProgrammingCables()
            .Where(p => p.Kind == UsbPortKind.AtmelSamBa)
            .ToList();

        if (cables.Count == 0)
            throw FlasherError.NoProgrammingCable();

        Exception? lastError = FlasherError.NoProgrammingCable();
        foreach (var port in cables)
        {
            try
            {
                return OpenAndIdentify(port, timeout ?? TimeSpan.FromSeconds(3));
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        throw lastError;
    }

    private ConnectedTarget OpenAndIdentify(SerialPortInfo port, TimeSpan timeout)
    {
        var transport = OpenTransport(port);
        var client = new SambaClient(transport);
        try
        {
            client.Connect(timeout);
            var identity = new FlashCalw(client).Identify();
            return new ConnectedTarget { Port = port, Client = client, Identity = identity };
        }
        catch
        {
            client.Close();
            throw;
        }
    }

    public void Write(
        string firmwarePath,
        uint address = FlashLayout.ApplicationStart,
        SambaClient? client = null,
        bool verify = true,
        Action<double, FlashProgressPhase>? progress = null,
        Action<FlashPageWrite>? pageProgress = null)
    {
        var plan = PlanWrite(firmwarePath, address);
        if (client is null && plan.Port is null)
            throw FlasherError.NoProgrammingCable();

        WithClient(client, samba =>
        {
            var flash = new FlashCalw(samba, FlashCommandSettleSeconds);
            flash.WriteApplication(
                plan.Image.Data,
                progress: f => progress?.Invoke(f, FlashProgressPhase.Writing),
                verifyProgress: f => progress?.Invoke(f, FlashProgressPhase.Verifying),
                pageProgress: pageProgress,
                verify: verify);
        });
    }

    public byte[] Read(string filePath, SambaClient? client = null, Action<double, FlashProgressPhase>? progress = null)
    {
        var saved = ReadApplication(client, progress);
        File.WriteAllBytes(filePath, saved);
        return saved;
    }

    /// <summary>Reads the application region (0x04000, 112 KB) without writing a file.</summary>
    public byte[] ReadApplication(SambaClient? client = null, Action<double, FlashProgressPhase>? progress = null)
    {
        byte[] saved = [];
        WithClient(client, samba =>
        {
            var flash = new FlashCalw(samba);
            var identity = flash.Identify();
            if (!identity.IsSupported15C)
                throw FlasherError.UnsupportedDevice(identity.Name, identity.Cidr, identity.Exid);

            saved = flash.ReadApplication(f => progress?.Invoke(f, FlashProgressPhase.Reading));
        });
        return saved;
    }

    private void WithClient(SambaClient? existing, Action<SambaClient> body)
    {
        if (existing is not null)
        {
            body(existing);
            return;
        }

        var connected = Connect();
        try
        {
            if (!connected.Identity.IsSupported15C)
                throw FlasherError.UnsupportedDevice(connected.Identity.Name, connected.Identity.Cidr, connected.Identity.Exid);
            body(connected.Client);
        }
        finally
        {
            connected.Client.Close();
        }
    }
}
