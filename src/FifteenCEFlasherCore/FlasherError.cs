namespace FifteenCEFlasherCore;

public sealed class FlasherError : Exception
{
    public FlasherErrorKind Kind { get; }
    public string? Detail { get; }

    private FlasherError(FlasherErrorKind kind, string message, string? detail = null)
        : base(message)
    {
        Kind = kind;
        Detail = detail;
    }

    public static FlasherError FirmwareNotFound(string path) =>
        new(FlasherErrorKind.FirmwareNotFound, $"Firmware file not found: {path}");

    public static FlasherError FirmwareEmpty() =>
        new(FlasherErrorKind.FirmwareEmpty, "Firmware file is empty.");

    public static FlasherError FirmwareTooLarge(int actual, int maximum) =>
        new(FlasherErrorKind.FirmwareTooLarge, $"Firmware is {actual} bytes; maximum application size is {maximum} bytes.");

    public static FlasherError FirmwareWrongSize(int actual, int expected) =>
        new(FlasherErrorKind.FirmwareWrongSize, $"Firmware is {actual} bytes; expected {expected} bytes (0x{expected:X}).");

    public static FlasherError WriteWouldTouchBootloader(uint address) =>
        new(FlasherErrorKind.WriteWouldTouchBootloader, $"Refusing write at 0x{address:X}; bootloader occupies 0x0000–0x3FFF.");

    public static FlasherError NoProgrammingCable() =>
        new(FlasherErrorKind.NoProgrammingCable,
            "No HP programming cable detected. Put the calculator in programming mode and reconnect.");

    public static FlasherError SerialOpenFailed(string path) =>
        new(FlasherErrorKind.SerialOpenFailed, $"Could not open serial port {path}.");

    public static FlasherError SerialPortClosed() =>
        new(FlasherErrorKind.SerialPortClosed,
            "The programming cable disconnected. Hold ERASE, press RESET, then release ERASE and wait for the port to reappear.");

    public static FlasherError SambaTimeout(string detail = "") =>
        new(FlasherErrorKind.SambaTimeout,
            string.IsNullOrEmpty(detail)
                ? "Timed out talking to SAM-BA. Leave the cable plugged in. If Status is not Connected, hold ERASE, press RESET, then release ERASE."
                : detail);

    public static FlasherError SambaProtocol(string detail) =>
        new(FlasherErrorKind.SambaProtocol, $"SAM-BA protocol error: {detail}");

    public static FlasherError UnsupportedDevice(string name, uint cidr, uint exid) =>
        new(FlasherErrorKind.UnsupportedDevice,
            $"Unsupported chip {name} (CIDR=0x{cidr:X8} EXID=0x{exid:X8}). This tool is for post-2015 Voyager calculators (15c CE, 16c CE, 12c) with an ATSAM4LC2C.");

    public static FlasherError VerifyMismatch() =>
        new(FlasherErrorKind.VerifyMismatch,
            "Verify failed: flash contents do not match the firmware file. Leave the cable in programming mode and try again.");

    public static FlasherError AppletFailed(uint status) =>
        new(FlasherErrorKind.AppletFailed,
            $"SAM-BA flash applet failed (status=0x{status:X8}). Leave the cable plugged in. If Status is not Connected, hold ERASE, press RESET, then release ERASE.");

    public static FlasherError NotConnected() =>
        new(FlasherErrorKind.NotConnected, "Not connected to a programming cable.");
}

public enum FlasherErrorKind
{
    FirmwareNotFound,
    FirmwareEmpty,
    FirmwareTooLarge,
    FirmwareWrongSize,
    WriteWouldTouchBootloader,
    NoProgrammingCable,
    SerialOpenFailed,
    SerialPortClosed,
    SambaTimeout,
    SambaProtocol,
    UnsupportedDevice,
    VerifyMismatch,
    FlashControllerError,
    AppletFailed,
    NotConnected,
}
