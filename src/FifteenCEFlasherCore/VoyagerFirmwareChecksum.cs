namespace FifteenCEFlasherCore;

public static class VoyagerFirmwareChecksum
{
    public static ushort DisplayedValue(byte[] data)
    {
        var payload = TrimTrailingZeros(data);
        if (payload.Length < 2)
            return 0;

        ushort sum = 0;
        for (var i = 0; i < payload.Length - 1; i++)
            sum = (ushort)((sum + payload[i]) & 0xFF);

        return (ushort)((sum << 8) | sum);
    }

    public static string Formatted(ushort value) => $"{value:X4}h";

    public static string Formatted(byte[] data) => Formatted(DisplayedValue(data));

    public static string TestMenuDisplay(ushort value) => $"ChE - - {Formatted(value)}";

    public static BackupChecksumAssessment BackupAssessment(byte[] data) =>
        new(DisplayedValue(data));

    public static FirmwareFileAssessment FirmwareFileAssessment(byte[] data, BackupChecksumAssessment? backup, bool backupSkipped) =>
        new(DisplayedValue(data), backupSkipped ? null : backup);

    private static byte[] TrimTrailingZeros(byte[] data)
    {
        var end = data.Length;
        while (end > 0 && data[end - 1] == 0)
            end--;
        return data.AsSpan(0, end).ToArray();
    }
}

public sealed class BackupChecksumAssessment
{
    public ushort Displayed { get; }

    /// <summary>The firmware now on the calculator, when its checksum is in the known list.</summary>
    public KnownFirmwareEntry? Known { get; }

    public BackupChecksumAssessment(ushort displayed)
    {
        Displayed = displayed;
        Known = KnownFirmware.Find(displayed);
    }

    /// <summary>Default name for saving this firmware, for example hp15c-ce-original-9090h-20260929.bin.</summary>
    public string DefaultBackupFileName(DateTime date) =>
        $"{Known?.FileName ?? "firmware"}-{VoyagerFirmwareChecksum.Formatted(Displayed)}-{date:yyyyMMdd}.bin";

    public string Message
    {
        get
        {
            var label = VoyagerFirmwareChecksum.Formatted(Displayed);
            return Known is not null
                ? $"Checksum {label}: {Known.DisplayName}. It is safe to proceed."
                : $"Checksum {label}. This firmware is not in the list of known versions. If your calculator runs firmware that isn't listed yet, or a custom version, proceed at your own risk. If it runs a listed version, there may be a problem with the backup.";
        }
    }
}

public enum FirmwareFileKind
{
    AlreadyOnCalculator,
    Known,
    OtherModel,
    Unrecognized,
}

public sealed class FirmwareFileAssessment
{
    public FirmwareFileKind Kind { get; }
    public ushort Displayed { get; }

    /// <summary>The chosen file, when its checksum is in the known list.</summary>
    public KnownFirmwareEntry? Known { get; }

    /// <summary>The firmware on the calculator, from the backup. Null when the backup was skipped or is not listed.</summary>
    public KnownFirmwareEntry? OnCalculator { get; }

    public FirmwareFileAssessment(ushort displayed, BackupChecksumAssessment? backup)
    {
        Displayed = displayed;
        Known = KnownFirmware.Find(displayed);
        OnCalculator = backup?.Known;

        if (backup is not null && backup.Displayed == displayed)
            Kind = FirmwareFileKind.AlreadyOnCalculator;
        else if (Known is null)
            Kind = FirmwareFileKind.Unrecognized;
        else if (OnCalculator is not null && OnCalculator.Model != Known.Model)
            Kind = FirmwareFileKind.OtherModel;
        else
            Kind = FirmwareFileKind.Known;
    }

    public string Message
    {
        get
        {
            var label = VoyagerFirmwareChecksum.Formatted(Displayed);
            return Kind switch
            {
                FirmwareFileKind.AlreadyOnCalculator =>
                    $"Checksum {label}. This firmware is already on the calculator. You don't need to install it again.",
                FirmwareFileKind.OtherModel =>
                    $"Checksum {label}: {Known!.DisplayName}. Your calculator has {OnCalculator!.ModelName} firmware, so this file is for a different model. Are you sure you want to install it?",
                FirmwareFileKind.Known when OnCalculator is null =>
                    $"Checksum {label}: {Known!.DisplayName}. Make sure your calculator is an {Known.ModelName}.",
                FirmwareFileKind.Known =>
                    $"Checksum {label}: {Known!.DisplayName}. It is safe to proceed.",
                _ =>
                    $"Checksum {label}. This firmware is not in the list of known versions. Make sure it is made for your calculator's model. Are you sure you want to install it?",
            };
        }
    }
}
